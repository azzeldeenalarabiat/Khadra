using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Bookings;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Application.Notifications;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Notifications;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.Payments.ReceiveProviderEvent;

/// <param name="RawBody">
/// Unparsed and unmodified. Signature verification runs over the exact bytes the provider signed, so
/// anything that deserialises and re-serialises on the way here breaks it.
/// </param>
public sealed record ReceiveProviderEventCommand(
    string RawBody,
    IReadOnlyDictionary<string, string> Headers) : ICommand<UnitResult<Error>>;

/// <summary>
/// Bounds the body before anything looks at it.
/// </summary>
/// <remarks>
/// The endpoint is anonymous, so this runs against whatever the internet sends. A provider's event is
/// a few kilobytes of JSON; anything larger did not come from one, and refusing it here means the
/// signature check never has to run over a megabyte of attacker-chosen bytes. Empty is refused for
/// the same reason: there is nothing to verify.
/// </remarks>
public sealed class ReceiveProviderEventCommandValidator : AbstractValidator<ReceiveProviderEventCommand>
{
    /// <summary>Generous for a provider event, small enough that a flood costs the API nothing.</summary>
    public const int MaximumBodyBytes = 64 * 1024;

    public ReceiveProviderEventCommandValidator() =>
        RuleFor(command => command.RawBody)
            .NotEmpty()
            .MaximumLength(MaximumBodyBytes);
}

/// <summary>
/// A payment provider telling the platform what became of a checkout.
/// </summary>
/// <remarks>
/// <para>
/// This is the only path by which a booking can become <c>Confirmed</c>, so it is where the whole
/// feature's correctness lives. Five things must all hold before a capture confirms anything, and
/// falling short of any one of them makes it an ORPHAN — money taken that no rental can be built on,
/// and which therefore goes back:
/// </para>
/// <list type="number">
/// <item>the adapter verified the provider's signature over the raw body;</item>
/// <item>this delivery has never been seen before;</item>
/// <item>its reference resolves to a <c>Payment</c> row this platform issued;</item>
/// <item>the captured amount and currency equal what that row asked for;</item>
/// <item>the booking still has room for it.</item>
/// </list>
/// <para>
/// <b>A capture is never persisted unresolved.</b> The row leaves this handler as
/// <see cref="PaymentStatus.Applied"/> or <see cref="PaymentStatus.Orphaned"/> and never in between,
/// because the receipt that makes the delivery un-replayable is written in the SAME transaction: a
/// half-finished capture would be permanently stuck, its provider's retries refused as duplicates of
/// a delivery that never completed.
/// </para>
/// <para>
/// The receipt is not a check-then-act. It is INSERTED, and the unique index on
/// (provider, provider_event_id) is what refuses a duplicate — a read followed by a write leaves a
/// window in which two concurrent deliveries both pass the read.
/// </para>
/// </remarks>
public sealed partial class ReceiveProviderEventHandler(
    IPaymentProvider provider,
    IPaymentRepository payments,
    IProviderEventReceiptRepository receipts,
    IBookingRepository bookings,
    IDealerRepository dealers,
    DealerTeamNotifier team,
    IClock clock,
    IUnitOfWork unitOfWork,
    ILogger<ReceiveProviderEventHandler> logger) : IRequestHandler<ReceiveProviderEventCommand, UnitResult<Error>>
{
    /// <summary>
    /// Receives one provider notification, or refuses it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A lost concurrency race is NOT retried here.</b> It was, in the first draft, and a test of
    /// the exact race proved that wrong: when the settlement job expires the booking first, this
    /// handler's save throws having ALREADY mutated the aggregates in memory -- the payment reads
    /// Applied and the booking reads Confirmed. EF's change tracker keeps those mutations after a
    /// failed <c>SaveChanges</c>, so a second pass through the same scope decides against dirty state
    /// rather than against the database: it finds a payment that is already captured, cannot orphan
    /// it, and leaves a customer's money attached to an expired booking with no refund recorded.
    /// </para>
    /// <para>
    /// So the exception escapes, the endpoint answers 5xx, and the PROVIDER re-delivers -- which is
    /// what webhooks are built to do, and which arrives in a fresh scope with a clean context and
    /// reads the booking as it now is. The receipt row rolled back with the failed transaction, so
    /// the re-delivery is not mistaken for a replay. The settlement job resolves the same conflict
    /// the same way: it logs and leaves the booking for its next pass.
    /// </para>
    /// <para>
    /// This is the hazard <c>UnitOfWork.ExecuteInTransactionAsync</c> already records -- "nothing
    /// resets the change tracker between attempts" -- reaching the one handler that moves money.
    /// </para>
    /// </remarks>
    public async Task<UnitResult<Error>> Handle(
        ReceiveProviderEventCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Signature first, before a single query. An unverified body must never reach a database.
        var parsed = provider.ParseEvent(request.RawBody, request.Headers);
        if (parsed.IsFailure)
            return UnitResult.Failure(parsed.Error);

        try
        {
            return await ApplyAsync(parsed.Value, cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            LogRaceLost(logger, parsed.Value.ProviderEventId);
            throw;
        }
    }

    private async Task<UnitResult<Error>> ApplyAsync(ProviderEvent notification, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var payment = await payments.GetByProviderReferenceAsync(
            provider.Name,
            notification.ProviderReference,
            cancellationToken);

        // A reference this platform never issued. Recorded rather than dropped -- it is exactly the
        // event somebody will need to find -- and answered as a success, because there is nothing the
        // provider could do differently by retrying.
        if (payment is null)
        {
            LogUnknownReference(logger, notification.ProviderEventId, notification.ProviderReference);
            receipts.Add(Receipt(notification, payment: null, ProviderEventOutcome.Unknown, now));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return UnitResult.Success<Error>();
        }

        var outcome = notification.Kind switch
        {
            ProviderEventKind.Captured => await CaptureAsync(payment, notification, now, cancellationToken),
            ProviderEventKind.Failed => Fail(payment, notification, now),
            ProviderEventKind.RefundSettled => SettleRefund(payment, now),
            ProviderEventKind.RefundFailed => FailRefund(payment, notification, now),
            _ => ProviderEventOutcome.Ignored
        };

        receipts.Add(Receipt(notification, payment, outcome, now));

        // ONE save. The capture, the booking's confirmation, the notifications and the receipt that
        // makes this delivery un-replayable all commit together or none of them do.
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UnitResult.Success<Error>();
    }

    private async Task<ProviderEventOutcome> CaptureAsync(
        Payment payment,
        ProviderEvent notification,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // A capture with no amount is not a capture. Refuse to guess: recording the amount we HOPED
        // for would put a figure on the record that the provider never confirmed.
        if (notification.Amount is not { } captured)
        {
            LogCaptureWithoutAmount(logger, payment.Id.Value);
            return ProviderEventOutcome.Ignored;
        }

        // The payment's OWN guards run first -- wrong amount, wrong currency, already captured --
        // before the booking is touched at all. Confirming and then discovering the capture is
        // unusable would leave a mutated Booking on the change tracker with nothing to undo it: the
        // aggregate has no `Unconfirm`, and it should not have one.
        var acceptable = payment.CanAcceptCapture(captured);
        if (acceptable.IsFailure)
        {
            LogCaptureRefused(logger, payment.Id.Value, acceptable.Error.Code);
            Orphan(payment, captured, notification.OccurredAt, acceptable.Error.Code, now);
            return ProviderEventOutcome.Orphaned;
        }

        var booking = await bookings.GetByIdAsync(payment.BookingId, cancellationToken);
        if (booking is null)
        {
            Orphan(payment, captured, notification.OccurredAt, "BookingMissing", now);
            return ProviderEventOutcome.Orphaned;
        }

        // Deliberately NOT gated on the payment deadline. The money has moved; refusing it now would
        // mean keeping it. The deadline is enforced where a checkout opens -- see
        // BookingDepositSettlement.DepositDue and pre-launch item 62.
        var confirmed = BookingDepositSettlement.Confirm(booking, payment.Id, now);
        if (confirmed.IsFailure)
        {
            Orphan(payment, captured, notification.OccurredAt, BookingDepositSettlement.OrphanReasonFor(booking, payment.Id), now);
            return ProviderEventOutcome.Orphaned;
        }

        var applied = payment.Apply(captured, notification.OccurredAt, now);
        if (applied.IsFailure)
        {
            // Unreachable: CanAcceptCapture asked these exact questions a few lines ago and nothing
            // between the two could change the answer. Kept because "unreachable" is a claim about
            // today's code, and the alternative to checking is a silent confirmation with no payment.
            throw new DomainException(
                $"Payment {payment.Id} passed its capture guard and then refused the capture: {applied.Error.Code}.");
        }

        await AnnounceAsync(booking.DealerId, booking.CustomerId, booking.Id, booking.Reference.Value, now, cancellationToken);
        return ProviderEventOutcome.Acted;
    }

    /// <summary>
    /// Records a capture the platform cannot use, and the refund that is owed the instant it lands.
    /// </summary>
    /// <remarks>
    /// The refund is created by the aggregate rather than here, so a capture can never be orphaned
    /// without one. Money taken with no record saying it is owed back is the failure this whole
    /// handler exists to make impossible.
    /// </remarks>
    private void Orphan(Payment payment, Money captured, DateTimeOffset capturedAt, string reason, DateTimeOffset now)
    {
        var orphaned = payment.Orphan(captured, capturedAt, reason, now);
        if (orphaned.IsFailure)
        {
            // Reachable only if the amount does not match AND the booking could not take it. Both
            // facts matter, so the log carries the amount rather than only the code.
            LogOrphanFailed(logger, payment.Id.Value, orphaned.Error.Code, captured.Amount, captured.CurrencyCode);
            return;
        }

        LogOrphaned(logger, payment.Id.Value, captured.Amount, captured.CurrencyCode, reason);
    }

    private static ProviderEventOutcome Fail(Payment payment, ProviderEvent notification, DateTimeOffset now)
    {
        var failed = payment.Fail(notification.FailureCode ?? "provider_failed", now);
        return failed.IsSuccess ? ProviderEventOutcome.Acted : ProviderEventOutcome.Ignored;
    }

    private static ProviderEventOutcome SettleRefund(Payment payment, DateTimeOffset now)
    {
        var outstanding = payment.Refunds.FirstOrDefault(refund => refund.Status == RefundStatus.Sent);
        if (outstanding is null)
            return ProviderEventOutcome.Ignored;

        outstanding.MarkSettled(now);
        return ProviderEventOutcome.Acted;
    }

    private static ProviderEventOutcome FailRefund(Payment payment, ProviderEvent notification, DateTimeOffset now)
    {
        var outstanding = payment.Refunds.FirstOrDefault(refund => refund.Status == RefundStatus.Sent);
        if (outstanding is null)
            return ProviderEventOutcome.Ignored;

        outstanding.MarkFailed(notification.FailureCode ?? "provider_refused", now);
        return ProviderEventOutcome.Acted;
    }

    /// <summary>
    /// Tells the gallery and the customer, staged on this transaction so a notification can never
    /// describe a payment that rolled back.
    /// </summary>
    private async Task AnnounceAsync(
        Id dealerId,
        Id customerId,
        Id bookingId,
        string reference,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var dealer = await dealers.GetByIdAsync(dealerId, cancellationToken);
        if (dealer is null)
            return;

        // The gallery is told a customer paid, and -- like every other customer-driven notification
        // on this platform -- the row names nobody: notifications are never deleted, and a customer's
        // name must not outlive their account.
        await team.NotifyTeamOfCustomerActionAsync(
            dealer,
            NotificationKind.BookingConfirmed,
            now,
            bookingId,
            reference);

        await team.NotifyCustomerAsync(
            customerId,
            dealer.BusinessName.Value,
            NotificationKind.YourBookingConfirmed,
            now,
            bookingId,
            reference);
    }

    private ProviderEventReceipt Receipt(
        ProviderEvent notification,
        Payment? payment,
        ProviderEventOutcome outcome,
        DateTimeOffset now) =>
        ProviderEventReceipt.Record(
            provider.Name,
            notification.ProviderEventId,
            notification.ProviderReference,
            notification.Kind.ToString(),
            payment?.Id,
            outcome,
            notification.Amount,
            now);

    [LoggerMessage(
        2300,
        LogLevel.Warning,
        "Payment event {EventId} lost a concurrency race. Answering 5xx so the provider re-delivers "
        + "it into a clean context; nothing was written.")]
    private static partial void LogRaceLost(ILogger logger, string eventId);

    [LoggerMessage(
        2301,
        LogLevel.Warning,
        "Payment event {EventId} named reference {Reference}, which is not ours. Recorded and ignored.")]
    private static partial void LogUnknownReference(ILogger logger, string eventId, string reference);

    [LoggerMessage(
        2302,
        LogLevel.Error,
        "Payment {PaymentId} received a capture carrying no amount; refusing to assume one.")]
    private static partial void LogCaptureWithoutAmount(ILogger logger, Guid paymentId);

    [LoggerMessage(
        2303,
        LogLevel.Error,
        "Payment {PaymentId} could not accept its own capture: {Code}. It is being orphaned.")]
    private static partial void LogCaptureRefused(ILogger logger, Guid paymentId, string code);

    [LoggerMessage(
        2304,
        LogLevel.Error,
        "Payment {PaymentId} could not be orphaned ({Code}); captured {Amount} {Currency} is UNACCOUNTED FOR.")]
    private static partial void LogOrphanFailed(
        ILogger logger, Guid paymentId, string code, decimal amount, string currency);

    [LoggerMessage(
        2305,
        LogLevel.Warning,
        "Payment {PaymentId} captured {Amount} {Currency} that no booking could take ({Reason}); a refund is recorded.")]
    private static partial void LogOrphaned(
        ILogger logger, Guid paymentId, decimal amount, string currency, string reason);
}

