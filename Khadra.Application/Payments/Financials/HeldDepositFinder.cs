using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Disputes.Repositories;
using Khadra.Domain.Payments.Repositories;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.Payments.Financials;

/// <summary>A deposit held for a penalty against the customer with no dispute, since its window closed.</summary>
/// <param name="HeldSince">When the booking's dispute window closed: the instant nothing could decide it any more.</param>
public sealed record HeldDeposit(Id BookingId, DateTimeOffset HeldSince);

/// <summary>
/// The deposits pre-launch item 164 is about — held for a penalty against the customer, no dispute
/// opened, the window closed — so the administrator's work queue can SHOW them until payments Phase 8
/// gives them an exit (owner, 2026-09-26). Nothing here decides or moves anything.
/// </summary>
/// <remarks>
/// <para>
/// UNUSED since payments Phase 8: the office payables ledger records these deposits as kept, and the work queue
/// shows the ledger's own holds instead. Left in place, with its repository query and tests, until the owner
/// approves deleting them.
/// </para>
/// <para>
/// The calculator is the one definition. The repository finds CANDIDATES cheaply in SQL — the deposit
/// release query with its penalty condition inverted — and <see cref="BookingFinancialsCalculator"/>
/// keeps only the ones whose deposit it reads as <see cref="DepositStates.HeldUnresolved"/>. The
/// candidates over-approximate by exactly one state, a window still open
/// (<see cref="DepositStates.HeldForAssessedPenalty"/>); any other state is the SQL and the calculator
/// drifting apart, and is logged, because that is the one thing this arrangement must surface.
/// </para>
/// <para>
/// Four reads whatever the count — the candidates, their payments, their tickets — run one after
/// another on the request's context. Unbounded for now: the set only grows until Phase 8 settles these
/// deposits, and at this platform's volume it is tens, not thousands (pre-launch item 164 notes when
/// to cap it).
/// </para>
/// </remarks>
public sealed partial class HeldDepositFinder(
    IBookingRepository bookings,
    IPaymentRepository payments,
    IDisputeTicketRepository tickets,
    ILogger<HeldDepositFinder> logger)
{
    public async Task<IReadOnlyList<HeldDeposit>> FindAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var candidates = await bookings.ListHeldForCustomerPenaltyAsync(now, cancellationToken);
        if (candidates.Count == 0)
            return [];

        var ids = candidates.Select(booking => booking.Id).ToList();
        var allPayments = await payments.ListForBookingsAsync(ids, cancellationToken);
        var claims = await tickets.ListClaimsForBookingsAsync(ids, cancellationToken);

        var held = new List<HeldDeposit>();
        foreach (var booking in candidates)
        {
            var own = allPayments.Where(payment => payment.BookingId == booking.Id).ToList();
            var itsClaims = claims.Where(ticket => ticket.BookingId == booking.Id).ToList();
            var financials = BookingFinancialsCalculator.Calculate(
                booking,
                own,
                itsClaims.Where(ticket => ticket.Status == DisputeStatus.Resolved),
                itsClaims.Exists(ticket => ticket.Status.IsLive),
                now);

            var state = financials.Deposit.State;
            if (state == DepositStates.HeldUnresolved)
                held.Add(new HeldDeposit(booking.Id, booking.DisputeWindowEndsAt ?? booking.FinishedAt ?? now));
            else if (state != DepositStates.HeldForAssessedPenalty)
                LogDrift(logger, booking.Id.Value, state);
        }

        return held;
    }

    [LoggerMessage(
        2411,
        LogLevel.Warning,
        "Booking {BookingId} was found as a deposit held for a customer penalty, but its financial state reads {State}. The candidate query and the calculator disagree; somebody needs to look.")]
    private static partial void LogDrift(ILogger logger, Guid bookingId, string state);
}
