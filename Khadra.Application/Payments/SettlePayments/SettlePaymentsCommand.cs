using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.Payments.SettlePayments;

/// <summary>What one sweep did. Returned so a test can assert on it, and logged.</summary>
public sealed record PaymentSweepReport(int Closed, int RefundsSent, int RefundsFailed)
{
    public static readonly PaymentSweepReport Empty = new(0, 0, 0);
}

public sealed record SettlePaymentsCommand : ICommand<Result<PaymentSweepReport, Error>>;

/// <summary>
/// Closes payment attempts the provider stopped talking about, and sends the refunds this platform
/// owes.
/// </summary>
/// <remarks>
/// <para>
/// Two jobs that look unrelated and are the same job: both exist because a network call can be lost,
/// and neither the platform nor the provider can be trusted to be the only one remembering.
/// </para>
/// <para>
/// <b>Closing a stale attempt.</b> A provider that never delivers its expiry notice leaves a row
/// reading Pending for ever, and the partial unique index means the customer cannot open a
/// replacement inside their own payment window: one lost webhook would cost them the booking. The
/// sweep asks the provider what actually happened rather than assuming — a session this platform
/// believes is dead may have been paid, and closing it blind would strand a capture. Only when the
/// provider agrees nothing was taken is the row failed.
/// </para>
/// <para>
/// <b>Sending a refund.</b> Refunds are RECORDED the instant they are owed, inside the transaction
/// that recorded the capture, and sent afterwards. That split is deliberate: a refund that had to
/// reach the provider before it could be recorded would be lost entirely whenever the provider was
/// down, which is exactly when it matters. With no provider configured the rows simply stay
/// <see cref="RefundStatus.Requested"/> and this says so in the log — visible and owed, rather than
/// quietly dropped.
/// </para>
/// <para>
/// <b>A refused refund waits</b> (Wave 4, B4; checklist 157). It used to be sent again on every tick, with an Error
/// each time. Now each refusal is counted and sets when it may be sent again, by
/// <see cref="IPaymentSettings.RefundRetry"/>; it is never abandoned, and from the policy's alert on it is logged
/// at Error and put on the administrator's work queue.
/// </para>
/// <para>
/// <b>One payment at a time.</b> Each payment with a refund due is loaded, sent and saved on its own. A refund the
/// webhook settled while the sweep held it is refused by its concurrency token at the save, and that costs this
/// payment's sends only: the tracker is discarded and the next payment is read fresh. It used to be one save for the
/// whole tick, and one conflict threw every other payment's sends away with it.
/// </para>
/// </remarks>
public sealed partial class SettlePaymentsHandler(
    IPaymentRepository payments,
    IPaymentProvider provider,
    IPaymentSettings settings,
    IClock clock,
    IUnitOfWork unitOfWork,
    ILogger<SettlePaymentsHandler> logger)
    : IRequestHandler<SettlePaymentsCommand, Result<PaymentSweepReport, Error>>
{
    public async Task<Result<PaymentSweepReport, Error>> Handle(
        SettlePaymentsCommand request,
        CancellationToken cancellationToken)
    {
        await WarnOfUnrecordedEndingRefundsAsync(cancellationToken);

        // Nothing to sweep without a provider to ask. The outstanding refunds are still counted and
        // logged, because "we owe six customers money and cannot send it" is the single most
        // important thing this sweep can say.
        if (!provider.IsConfigured)
        {
            var owed = await payments.ListWithOutstandingRefundsAsync(cancellationToken);
            if (owed.Count > 0)
                LogRefundsStranded(logger, owed.Count);
            return PaymentSweepReport.Empty;
        }

        var now = clock.UtcNow;
        var closed = await CloseStaleAsync(now, cancellationToken);
        var (sent, failed) = await SendRefundsAsync(now, cancellationToken);

        var report = new PaymentSweepReport(closed, sent, failed);
        if (closed + sent + failed > 0)
            LogSwept(logger, closed, sent, failed);
        return report;
    }

    /// <summary>
    /// The safety net under the ending refunds (Phase 3): a payment made in full whose booking ended
    /// before pickup with no refund recorded for that ending, or a booking an administrator cancelled
    /// with no whole-payment refund.
    /// </summary>
    /// <remarks>
    /// Normally nothing. Every way of ending a booking records its refund through one seam, in the
    /// same save; a row here is a new way of ending one that forgot, or a booking ended before the rule
    /// existed. It only SAYS so — recording refunds from a sweep would make a second writer of money
    /// owed, which is how two refunds for one ending come about.
    /// </remarks>
    private async Task WarnOfUnrecordedEndingRefundsAsync(CancellationToken cancellationToken)
    {
        var unrecorded = await payments.ListEndedWithoutEndingRefundAsync(cancellationToken);
        foreach (var payment in unrecorded)
            LogEndingRefundMissing(logger, payment.Id.Value, payment.BookingId.Value);
    }

    private async Task<int> CloseStaleAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var stale = await payments.ListStaleLiveAsync(now.Subtract(settings.StaleAttemptGrace), cancellationToken);
        var closed = 0;

        foreach (var payment in stale)
        {
            // An Initiated row never reached the provider, so there is nothing to ask about: no
            // session exists under that reference and no money can have moved.
            if (payment.ProviderReference is not { } reference)
            {
                if (Close(payment, "never_started", now))
                    closed++;
                continue;
            }

            var state = await provider.QueryAsync(reference, cancellationToken);
            if (state.IsFailure)
            {
                // The provider is unreachable. Leave the row alone: closing it on a guess is how a
                // paid booking gets marked unpaid.
                LogQueryFailed(logger, payment.Id.Value, state.Error.Code);
                continue;
            }

            // The provider says money moved after all. Do NOT resolve it here -- this sweep has no
            // booking loaded and no receipt to write, and resolving a capture outside the webhook
            // handler would be a second, weaker copy of the most delicate code in the system. Say so
            // loudly and leave it; the provider's own retry is the right path, and if it never comes
            // this line is what tells a human to look.
            if (state.Value.Kind == ProviderEventKind.Captured)
            {
                LogStaleButCaptured(logger, payment.Id.Value, reference);
                continue;
            }

            if (Close(payment, state.Value.FailureCode ?? "provider_expired", now))
                closed++;
        }

        if (closed > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);
        return closed;
    }

    private bool Close(Payment payment, string failureCode, DateTimeOffset now)
    {
        var failed = payment.Fail(failureCode, now);
        if (failed.IsSuccess)
            return true;

        LogCloseRefused(logger, payment.Id.Value, failed.Error.Code);
        return false;
    }

    private async Task<(int Sent, int Failed)> SendRefundsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = await payments.ListIdsWithRefundsDueAsync(now, cancellationToken);
        var policy = settings.RefundRetry;
        var sent = 0;
        var failed = 0;
        var reached = 0;
        var unreachable = false;

        foreach (var paymentId in due)
        {
            // The provider stopped answering: every refund left is still due, untouched, and goes on the next tick.
            if (unreachable)
                break;
            reached++;

            // A clean tracker for every payment: nothing one payment did, or failed to save, reaches the next.
            unitOfWork.DiscardChanges();
            var payment = await payments.GetByIdAsync(paymentId, cancellationToken);

            // Only a captured payment has a provider reference to refund against; a Requested refund
            // on anything else is a bug elsewhere, and sending it would be acting on that bug.
            if (payment?.ProviderReference is not { } reference)
                continue;

            // Due by the refund's OWN schedule: one payment can carry a refund that is due and one still waiting.
            var pending = payment.Refunds.Where(refund => refund.IsDueToSend(now)).ToList();
            if (pending.Count == 0)
                continue;

            var (paymentSent, paymentFailed) = (0, 0);
            foreach (var refund in pending)
            {
                // The refund's OWN id is the idempotency key, so a crash between the provider
                // accepting and this row recording it re-sends the same instruction rather than a
                // second refund.
                var result = await provider.RefundAsync(
                    new RefundRequest(refund.Id, reference, refund.Amount),
                    cancellationToken);

                if (result.IsSuccess)
                {
                    refund.MarkSent(result.Value.ProviderReference, now);
                    paymentSent++;
                }
                else if (result.Error.Code == PaymentErrors.ProviderUnavailable.Code)
                {
                    // Nobody refused anything: the provider could not be asked. Counting it would put every refund
                    // on the work queue as "refused three times" after a ten-minute outage, and stretch its waits
                    // long after the provider is back (the advisor's review). The refund stays exactly as it was.
                    unreachable = true;
                    break;
                }
                else
                {
                    refund.RecordRefusedSend(result.Error.Code, now, policy);
                    paymentFailed++;
                    LogRefusal(refund, result.Error.Code, policy);
                }
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                sent += paymentSent;
                failed += paymentFailed;
            }
            catch (ConcurrencyConflictException)
            {
                // Somebody wrote this payment's refunds while they were being sent — the webhook settling one, most
                // likely. Theirs stands. Whatever the provider accepted here is sent again next time under the same
                // idempotency key, and is the same refund.
                LogRefundConflict(logger, payment.Id.Value);
                unitOfWork.DiscardChanges();
            }
        }

        if (unreachable)
            LogProviderUnreachable(logger, due.Count - reached + 1);
        return (sent, failed);
    }

    /// <summary>
    /// A refused send: a warning while the back-off is handling it, an error once the refusals need a person.
    /// </summary>
    private void LogRefusal(Refund refund, string code, RefundRetryPolicy policy)
    {
        var next = refund.NextAttemptAt ?? DateTimeOffset.MinValue;
        if (policy.NeedsAPerson(refund.RefusalCount))
            LogRefundRefusedRepeatedly(logger, refund.Id.Value, code, refund.RefusalCount, next);
        else
            LogRefundRefused(logger, refund.Id.Value, code, refund.RefusalCount, next);
    }

    [LoggerMessage(
        2310,
        LogLevel.Warning,
        "{Count} payment(s) carry a refund that is owed and cannot be sent: no payment provider is configured.")]
    private static partial void LogRefundsStranded(ILogger logger, int count);

    [LoggerMessage(
        2311,
        LogLevel.Information,
        "Payment sweep: {Closed} stale attempt(s) closed, {Sent} refund(s) sent, {Failed} refused.")]
    private static partial void LogSwept(ILogger logger, int closed, int sent, int failed);

    [LoggerMessage(
        2312,
        LogLevel.Warning,
        "Could not ask the provider about payment {PaymentId} ({Code}); leaving it open rather than guessing.")]
    private static partial void LogQueryFailed(ILogger logger, Guid paymentId, string code);

    [LoggerMessage(
        2313,
        LogLevel.Error,
        "Payment {PaymentId} ({Reference}) looked abandoned but the provider says it was CAPTURED. "
        + "Waiting for the provider's own event; if none arrives, this capture needs a human.")]
    private static partial void LogStaleButCaptured(ILogger logger, Guid paymentId, string reference);

    [LoggerMessage(2314, LogLevel.Warning, "Payment {PaymentId} could not be closed ({Code}).")]
    private static partial void LogCloseRefused(ILogger logger, Guid paymentId, string code);

    /// <summary>
    /// A Warning now, not an Error (Wave 4, B4): the back-off is handling it. It was an Error on every tick, which
    /// for a card closed for good was a flood of identical lines.
    /// </summary>
    [LoggerMessage(
        2315,
        LogLevel.Warning,
        "Refund {RefundId} was refused by the provider ({Code}), {Refusals} time(s) so far. It stays owed and is sent "
        + "again at {NextAttemptAt:O}.")]
    private static partial void LogRefundRefused(
        ILogger logger, Guid refundId, string code, int refusals, DateTimeOffset nextAttemptAt);

    [LoggerMessage(
        2323,
        LogLevel.Error,
        "Refund {RefundId} has been refused by the provider {Refusals} times ({Code}). It stays owed, is sent again at "
        + "{NextAttemptAt:O}, and is on the administrator's work queue: a person must look.")]
    private static partial void LogRefundRefusedRepeatedly(
        ILogger logger, Guid refundId, string code, int refusals, DateTimeOffset nextAttemptAt);

    [LoggerMessage(
        2325,
        LogLevel.Warning,
        "The payment provider could not be reached; {Payments} payment(s) with a refund due were left exactly as they "
        + "were and are sent on the next tick. Nothing was refused, so nothing was counted.")]
    private static partial void LogProviderUnreachable(ILogger logger, int payments);

    [LoggerMessage(
        2324,
        LogLevel.Warning,
        "Payment {PaymentId}'s refunds changed while the sweep was sending them; its sends this tick were not recorded "
        + "and are sent again next time under the same idempotency key. The other payments are unaffected.")]
    private static partial void LogRefundConflict(ILogger logger, Guid paymentId);

    [LoggerMessage(
        2316,
        LogLevel.Error,
        "Payment {PaymentId} for booking {BookingId}: the booking ended before the car was collected and owes the "
        + "customer a refund for that ending, but none is recorded. A human must look.")]
    private static partial void LogEndingRefundMissing(ILogger logger, Guid paymentId, Guid bookingId);
}
