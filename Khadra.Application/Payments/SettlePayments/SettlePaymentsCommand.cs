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
        var owing = await payments.ListWithOutstandingRefundsAsync(cancellationToken);
        var sent = 0;
        var failed = 0;

        foreach (var payment in owing)
        {
            // Only a captured payment has a provider reference to refund against; a Requested refund
            // on anything else is a bug elsewhere, and sending it would be acting on that bug.
            if (payment.ProviderReference is not { } reference)
                continue;

            var pending = payment.Refunds
                .Where(refund => refund.Status == RefundStatus.Requested || refund.Status == RefundStatus.Failed)
                .ToList();

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
                    sent++;
                }
                else
                {
                    refund.MarkFailed(result.Error.Code, now);
                    failed++;
                    LogRefundRefused(logger, refund.Id.Value, result.Error.Code);
                }
            }
        }

        if (sent + failed > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);
        return (sent, failed);
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

    [LoggerMessage(2315, LogLevel.Error, "Refund {RefundId} was refused by the provider ({Code}). It stays owed.")]
    private static partial void LogRefundRefused(ILogger logger, Guid refundId, string code);
}
