using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Notifications;
using Khadra.Application.Payments;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Disputes.Repositories;
using Khadra.Domain.Notifications;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
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
///
/// Two passes touch money (Phase 3, 2026-09-26), and only by RECORDING what is owed; the payment
/// sweep sends it. A paid no-show owes the customer everything above the deposit, recorded in the
/// save that marks it. And a deposit whose dispute window closed cleanly — no claim on it, no penalty
/// against the customer — goes back to the customer (owner, 2026-09-26): the booking decides that
/// from its own frozen window, never from the time alone.
/// </remarks>
public sealed record SettleDueBookingsCommand : ICommand<Result<SettlementReport, Error>>;

/// <summary>What one pass did. Logged, and returned so a test can assert on it.</summary>
public sealed record SettlementReport(
    int ExpiredUnanswered,
    int ExpiredUnpaid,
    int MarkedNoShow,
    int Completed,
    int Failed,
    int DepositsReleased = 0)
{
    public int Total => ExpiredUnanswered + ExpiredUnpaid + MarkedNoShow + Completed + DepositsReleased;

    public static readonly SettlementReport Empty = new(0, 0, 0, 0, 0);
}

public sealed partial class SettleDueBookingsHandler(
    IBookingRepository bookings,
    IPaymentRepository payments,
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
        // Fed per status, so the office is never told an approval lapsed about a request it never
        // approved: a request it let lapse tells it nothing (Wave 3, C7).
        var unanswered = await SettleAsync(
            await bookings.ListDueForDecisionExpiryAsync(now, cancellationToken),
            booking => booking.ExpireUnanswered(now),
            NotificationKind.YourBookingExpired,
            officeKind: null,
            now,
            count => failed += count,
            cancellationToken);

        var unpaid = await SettleAsync(
            await bookings.ListDueForPaymentExpiryAsync(now, cancellationToken),
            booking => booking.ExpireUnpaid(now),
            NotificationKind.YourBookingExpired,
            NotificationKind.BookingExpiredUnpaid,
            now,
            count => failed += count,
            cancellationToken);

        var noShows = await SettleNoShowsAsync(now, count => failed += count, cancellationToken);

        var completed = await SettleCompletionsAsync(now, error => failed += error, cancellationToken);

        var released = await ReleaseCleanDepositsAsync(now, count => failed += count, cancellationToken);

        var report = new SettlementReport(unanswered, unpaid, noShows, completed, failed, released);

        // Silent when there was nothing to do, which is most passes. A line a minute saying "nothing"
        // buries the ones that matter.
        if (report.Total > 0 || report.Failed > 0)
            LogSettled(logger, unanswered, unpaid, noShows, completed, released, failed);

        return report;
    }

    /// <summary>
    /// A rental nobody collected is marked a no-show, and a PAID one records, in the same save, the
    /// refund of everything the customer paid above the deposit (Phase 3). The deposit stays held:
    /// the no-show penalty is assessed against it, and only a dispute can move it.
    /// </summary>
    /// <remarks>
    /// The payment is checked BEFORE the booking changes. A booking whose payment cannot take the
    /// refund is left exactly as it was, logged, and counted as deferred — never marked a no-show with
    /// the money it owes unrecorded. The request handlers can simply throw in that case, because a
    /// request rolls back whole; this pass commits booking by booking, and a throw would abandon the
    /// rest of the pass.
    /// </remarks>
    private async Task<int> SettleNoShowsAsync(
        DateTimeOffset now,
        Action<int> onFailure,
        CancellationToken cancellationToken)
    {
        var due = await bookings.ListDueForNoShowAsync(now, cancellationToken);
        var settled = 0;

        foreach (var booking in due)
        {
            // Only a booking paid above its deposit will owe a refund once marked; only that one
            // needs its payment, and needs it BEFORE the booking changes.
            Payment? payment = null;
            if (!booking.PaidAboveDeposit.IsZero)
            {
                payment = await payments.GetByIdAsync(booking.DepositPaymentId!.Value, cancellationToken);
                if (!BookingEndingRefunds.CanRecord(booking, payment))
                {
                    LogPaymentUnusable(logger, booking.Reference.Value);
                    onFailure(1);
                    continue;
                }
            }

            var result = booking.MarkNoShow(now);
            if (result.IsFailure)
                continue;

            BookingEndingRefunds.Record(booking, payment, now);
            await NotifyBothPartiesAsync(
                booking, NotificationKind.YourBookingMarkedNoShow, NotificationKind.BookingMarkedNoShow, now, cancellationToken);

            if (await CommitAsync(booking, cancellationToken))
                settled++;
            else
                onFailure(1);
        }

        return settled;
    }

    /// <summary>
    /// Returns a held deposit to the customer once its booking's dispute window has closed CLEANLY
    /// (owner, 2026-09-26): the booking ended before pickup, nobody claimed the deposit, and no
    /// penalty stands against the customer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The query leaves out, in SQL, every booking whose deposit something has already decided — a
    /// refund that returned or released it, a dispute not withdrawn, a penalty on the customer — so
    /// this is the handful still inside their window plus the few now due. The BOOKING judges each
    /// one against its own frozen window (<see cref="Booking.DepositReleasedOnCleanClose"/>), and the
    /// claim is asked again here because a ticket can be opened between the query and this line.
    /// </para>
    /// <para>
    /// Nothing about the booking changes, so nothing is announced: the refund row is the record, the
    /// booking shows it, and the customer is told when the money actually arrives.
    /// </para>
    /// </remarks>
    private async Task<int> ReleaseCleanDepositsAsync(
        DateTimeOffset now,
        Action<int> onFailure,
        CancellationToken cancellationToken)
    {
        var due = await bookings.ListDueForDepositReleaseAsync(now, cancellationToken);
        var released = 0;

        foreach (var booking in due)
        {
            // Asked per booking rather than in bulk, for the same reason as the completions: the
            // queries share one scoped DbContext and must not run concurrently.
            var claimed = await disputes.HasClaimOnDepositAsync(booking.Id, cancellationToken);
            var deposit = booking.DepositReleasedOnCleanClose(now, claimed);
            if (deposit.IsZero)
                continue;

            var payment = await payments.GetByIdAsync(booking.DepositPaymentId!.Value, cancellationToken);
            if (!BookingEndingRefunds.CanRecord(booking, payment))
            {
                LogPaymentUnusable(logger, booking.Reference.Value);
                onFailure(1);
                continue;
            }

            // Released already — the query leaves these out; asked again so a pass never counts, or
            // commits, a release it did not make.
            if (payment!.RefundFor(RefundReason.DisputeWindowClosed) is not null)
                continue;

            var refund = payment.RefundHeldDeposit(deposit, now);
            if (refund.IsFailure)
            {
                LogReleaseRefused(logger, booking.Reference.Value, refund.Error.Code);
                onFailure(1);
                continue;
            }

            if (refund.Value is null)
                continue;

            if (await CommitAsync(booking, cancellationToken))
                released++;
            else
                onFailure(1);
        }

        return released;
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

            await NotifyBothPartiesAsync(
                booking, NotificationKind.YourBookingCompleted, NotificationKind.BookingCompleted, now, cancellationToken);

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
        NotificationKind? officeKind,
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

            await NotifyBothPartiesAsync(booking, customerKind, officeKind, now, cancellationToken);

            if (await CommitAsync(booking, cancellationToken))
                settled++;
            else
                onFailure(1);
        }

        return settled;
    }

    /// <summary>
    /// Tells the customer and the office, in the transaction that records the change.
    /// </summary>
    /// <remarks>
    /// Nobody pressed a button here — the platform acted on a timer — so the office's row is Khadra's,
    /// in the console and by email (Fix & Polish Wave 3, C5). It used to mirror only a completion, as
    /// <c>BookingReturned</c> by "A customer": untrue in both languages, and English inside Arabic. An
    /// unpaid expiry and a no-show were not mirrored at all, on the mistaken ground that the office
    /// already had kinds for them. A request that lapsed unanswered still tells the office nothing.
    /// </remarks>
    private async Task NotifyBothPartiesAsync(
        Booking booking,
        NotificationKind customerKind,
        NotificationKind? officeKind,
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

        if (dealer is not null && officeKind is not null)
            await team.NotifyTeamFromPlatformAsync(dealer, officeKind, now, booking.Id, booking.Reference.Value);
    }

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
        "Booking settlement: {Unanswered} unanswered, {Unpaid} unpaid, {NoShows} no-shows, {Completed} completed, "
        + "{Released} deposit(s) released, {Failed} deferred.")]
    private static partial void LogSettled(
        ILogger logger, int unanswered, int unpaid, int noShows, int completed, int released, int failed);

    [LoggerMessage(
        2102,
        LogLevel.Error,
        "Booking {Reference} is paid, but its payment is missing, not applied or not its own. Left unchanged "
        + "rather than ended with the refund it owes unrecorded; a human must look.")]
    private static partial void LogPaymentUnusable(ILogger logger, string reference);

    [LoggerMessage(
        2103,
        LogLevel.Error,
        "The deposit of booking {Reference} is due back to the customer, but its payment refused the refund ({Code}). "
        + "Left for the next pass; a human must look.")]
    private static partial void LogReleaseRefused(ILogger logger, string reference, string code);

    [LoggerMessage(
        2101,
        LogLevel.Information,
        "Booking {Reference} changed while settlement was working on it; leaving it for the next pass.")]
    private static partial void LogConflict(ILogger logger, string reference);
}
