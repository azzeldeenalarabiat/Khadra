using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Notifications;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Disputes.Repositories;
using Khadra.Domain.Notifications;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.Bookings.SettleBookings;

/// <summary>
/// Moves every booking whose clock has run out into the state the clock already decided.
/// </summary>
/// <remarks>
/// This is pre-launch checklist item 4. Four rules were correctly enforced wherever anyone asked, and
/// nobody asked on a timer: a request nobody answered, an approval nobody paid, a rental nobody
/// collected, and a return nobody disputed all sat in their old status for ever.
///
/// No car was ever stranded by it — the availability predicate reads the clock, so a lapsed hold stops
/// counting at its deadline with nothing running. What was stranded is the TRUTH: both parties went on
/// reading "Requested" or "Approved" on a booking that had ended, and neither was told. For a phone
/// that is worse than for a console, because a customer's list would show a dead booking under
/// "Upcoming" indefinitely and no client may invent the expiry for itself — the status is the
/// server's word.
///
/// Idempotent by construction: every transition it calls re-checks its own status and deadline, so a
/// second pass over the same booking does nothing. Each booking is committed on its own, so one that
/// fails — a concurrency conflict with a dealer acting at the same instant — cannot hold up the rest.
/// </remarks>
public sealed record SettleDueBookingsCommand : ICommand<Result<SettlementReport, Error>>;

/// <summary>What one pass did. Logged, and returned so a test can assert on it.</summary>
public sealed record SettlementReport(int ExpiredUnanswered, int ExpiredUnpaid, int MarkedNoShow, int Completed, int Failed)
{
    public int Total => ExpiredUnanswered + ExpiredUnpaid + MarkedNoShow + Completed;

    public static readonly SettlementReport Empty = new(0, 0, 0, 0, 0);
}

public sealed partial class SettleDueBookingsHandler(
    IBookingRepository bookings,
    IDisputeTicketRepository disputes,
    IDealerRepository dealers,
    DealerTeamNotifier team,
    IClock clock,
    IUnitOfWork unitOfWork,
    ILogger<SettleDueBookingsHandler> logger)
    : IRequestHandler<SettleDueBookingsCommand, Result<SettlementReport, Error>>
{
    public async Task<Result<SettlementReport, Error>> Handle(
        SettleDueBookingsCommand request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var failed = 0;

        // Order matters only in that each pass reads the database fresh, so a booking settled by an
        // earlier pass is simply not returned by a later one.
        var unanswered = await SettleAsync(
            await bookings.ListDueForDecisionExpiryAsync(now, cancellationToken),
            booking => booking.ExpireUnanswered(now),
            NotificationKind.YourBookingExpired,
            now,
            count => failed += count,
            cancellationToken);

        var unpaid = await SettleAsync(
            await bookings.ListDueForPaymentExpiryAsync(now, cancellationToken),
            booking => booking.ExpireUnpaid(now),
            NotificationKind.YourBookingExpired,
            now,
            count => failed += count,
            cancellationToken);

        var noShows = await SettleAsync(
            await bookings.ListDueForNoShowAsync(now, cancellationToken),
            booking => booking.MarkNoShow(now),
            NotificationKind.YourBookingMarkedNoShow,
            now,
            count => failed += count,
            cancellationToken);

        var completed = await SettleCompletionsAsync(now, error => failed += error, cancellationToken);

        var report = new SettlementReport(unanswered, unpaid, noShows, completed, failed);

        // Silent when there was nothing to do, which is most passes. A line a minute saying "nothing"
        // buries the ones that matter.
        if (report.Total > 0 || report.Failed > 0)
            LogSettled(logger, unanswered, unpaid, noShows, completed, failed);

        return report;
    }

    /// <summary>
    /// A booking that has just returned and whose settlement window has passed completes ON ITS OWN —
    /// unless somebody disputed it, in which case the ticket decides when it closes.
    /// </summary>
    private async Task<int> SettleCompletionsAsync(
        DateTimeOffset now,
        Action<int> onFailure,
        CancellationToken cancellationToken)
    {
        var due = await bookings.ListDueForSettlementAsync(now, cancellationToken);
        var settled = 0;

        foreach (var booking in due)
        {
            // Asked per booking rather than in bulk: the queries share one scoped DbContext, so they
            // must not run concurrently, and this list is the tail of a day's returns, not a table.
            var hasOpenDispute = await disputes.HasLiveTicketAsync(booking.Id, cancellationToken);

            var result = booking.Settle(now, hasOpenDispute);
            if (result.IsFailure)
                continue;

            await NotifyBothPartiesAsync(booking, NotificationKind.YourBookingCompleted, now, cancellationToken);

            if (await CommitAsync(booking, cancellationToken))
                settled++;
            else
                onFailure(1);
        }

        return settled;
    }

    private async Task<int> SettleAsync(
        IReadOnlyList<Booking> due,
        Func<Booking, UnitResult<Error>> transition,
        NotificationKind customerKind,
        DateTimeOffset now,
        Action<int> onFailure,
        CancellationToken cancellationToken)
    {
        var settled = 0;

        foreach (var booking in due)
        {
            // The repository already filtered on status and deadline, but the aggregate is the rule
            // and it checks again. A booking a dealer answered between the query and this line is
            // refused here, which is the correct outcome and not an error.
            var result = transition(booking);
            if (result.IsFailure)
                continue;

            await NotifyBothPartiesAsync(booking, customerKind, now, cancellationToken);

            if (await CommitAsync(booking, cancellationToken))
                settled++;
            else
                onFailure(1);
        }

        return settled;
    }

    /// <summary>
    /// Tells the customer and the gallery, in the transaction that records the change.
    /// </summary>
    /// <remarks>
    /// Nobody pressed a button here — the platform acted on a timer — so the gallery's row goes
    /// through the same door a customer's action uses and names no person.
    /// </remarks>
    private async Task NotifyBothPartiesAsync(
        Booking booking,
        NotificationKind customerKind,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var dealer = await dealers.GetByIdAsync(booking.DealerId, cancellationToken);

        await team.NotifyCustomerAsync(
            booking.CustomerId,
            dealer?.BusinessName.Value ?? string.Empty,
            customerKind,
            now,
            booking.Id,
            booking.Reference.Value);

        if (dealer is not null)
            await NotifyGalleryAsync(dealer, booking, customerKind, now);
    }

    /// <summary>
    /// The gallery's own wording for the same event.
    /// </summary>
    /// <remarks>
    /// A completion is news to both sides and uses the same kind; an expiry and a no-show already
    /// have dealer-side kinds through the team feed, and adding a second row for the same fact would
    /// double every bell. So only the completion is mirrored, and the rest reach the gallery through
    /// the booking book they were already watching.
    /// </remarks>
    private Task NotifyGalleryAsync(Dealer dealer, Booking booking, NotificationKind kind, DateTimeOffset now) =>
        kind == NotificationKind.YourBookingCompleted
            ? team.NotifyTeamOfCustomerActionAsync(dealer, NotificationKind.BookingReturned, now, booking.Id, booking.Reference.Value)
            : Task.CompletedTask;

    /// <summary>
    /// Commits one booking. A conflict is expected traffic, not a fault.
    /// </summary>
    /// <remarks>
    /// The alternative — one transaction for the whole pass — means a single dealer approving a
    /// request at the wrong instant rolls back everything the job did. A conflict here simply means
    /// somebody got there first, and the next pass will find whatever is genuinely still due.
    /// </remarks>
    private async Task<bool> CommitAsync(Booking booking, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (ConcurrencyConflictException)
        {
            LogConflict(logger, booking.Reference.Value);
            return false;
        }
    }

    [LoggerMessage(
        2100,
        LogLevel.Information,
        "Booking settlement: {Unanswered} unanswered, {Unpaid} unpaid, {NoShows} no-shows, {Completed} completed, {Failed} deferred.")]
    private static partial void LogSettled(
        ILogger logger, int unanswered, int unpaid, int noShows, int completed, int failed);

    [LoggerMessage(
        2101,
        LogLevel.Information,
        "Booking {Reference} changed while settlement was working on it; leaving it for the next pass.")]
    private static partial void LogConflict(ILogger logger, string reference);
}
