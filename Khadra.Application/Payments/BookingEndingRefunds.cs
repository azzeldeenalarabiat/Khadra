using CSharpFunctionalExtensions;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;

namespace Khadra.Application.Payments;

/// <summary>
/// The one seam through which Bookings touches a Payment when a booking ENDS before the car was
/// collected: every refund that ending owes, recorded in the SAME save as the transition that ended
/// it (Phase 3, 2026-09-26).
/// </summary>
/// <remarks>
/// <para>
/// The BOOKING decides what is owed, from its own state: the whole payment
/// (<c>Booking.ReturnsWholePayment</c> — a customer's free cancellation, an administrator's
/// cancellation) or the money above the deposit (<c>Booking.RefundableAboveDeposit</c> — any other
/// paid ending before pickup). The PAYMENT adds the processing-fee rule it froze and records each
/// refund once. This class only joins the two, so every way of ending a booking — the customer's
/// cancel, a non-delivery report, an administrator's cancel or no-show, the sweep's no-show — asks
/// the same question and records the same answer.
/// </para>
/// <para>
/// A call rather than an event, deliberately: this project dispatches domain events only after the
/// commit and has no outbox, so a refund raised from an event could be lost between the two — a
/// booking ended with the customer's money held and no row saying it is owed. It replaced
/// <c>DepositRefundSettlement</c>, which recorded only the free cancellation's refund, the same way.
/// Recording is the initiation; the payment sweep sends every requested refund to the provider under
/// the refund's own id, and keeps re-sending one the provider refused.
/// </para>
/// </remarks>
public static class BookingEndingRefunds
{
    /// <summary>
    /// Whether this ending owes the customer a refund, so a caller loads the payment only when it has
    /// to: it returned the whole payment, or there is money above the deposit to give back.
    /// </summary>
    /// <remarks>
    /// Exact, and decided by the booking alone. The processing fee goes back only WITH the money above
    /// the deposit, and a deposit-only payment never carries a fee (Khadra absorbs the deposit's
    /// processing cost, <c>ProcessingFeePolicy</c>), so a deposit-only booking that did not return its
    /// whole payment owes nothing here: its deposit follows the cancellation and dispute rules.
    /// </remarks>
    public static bool OwesRefund(Booking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);
        return booking.ReturnsWholePayment || !booking.RefundableAboveDeposit.IsZero;
    }

    /// <summary>
    /// Records the refund this booking's ending owes, idempotently. Returns it, or null when the
    /// ending owes nothing (a deposit-only booking that did not return its whole payment).
    /// </summary>
    /// <remarks>
    /// Throws rather than returning a failure: <c>DepositPaymentId</c> is only ever set in the
    /// transaction that applied that very payment, so a missing, foreign or unrefundable payment is a
    /// programming error, and letting the ending commit anyway would leave money held with nothing
    /// saying it is owed. In a request, the throw rolls the whole request back. The sweep checks the
    /// payment BEFORE it changes a booking (<see cref="CanRecord"/>), so a bad row there is skipped and
    /// logged, never half-applied.
    /// </remarks>
    public static Refund? Record(Booking booking, Payment? payment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(booking);
        if (!OwesRefund(booking))
            return null;
        if (!CanRecord(booking, payment))
            throw new DomainException($"Booking {booking.Id}'s payment could not be found to refund.");

        var result = booking.ReturnsWholePayment
            ? payment!.RefundWholePayment(WholePaymentReason(booking), now).Map(refund => (Refund?)refund)
            : payment!.RefundAboveDeposit(booking.RefundableAboveDeposit, now);
        if (result.IsFailure)
            throw new DomainException($"Payment {payment.Id} could not refund booking {booking.Id}: {result.Error.Code}.");

        return result.Value;
    }

    /// <summary>Loads the booking's payment when the ending may owe a refund, and records it.</summary>
    public static async Task<Refund?> RecordAsync(
        Booking booking,
        IPaymentRepository payments,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(payments);
        if (!OwesRefund(booking))
            return null;

        var payment = await payments.GetByIdAsync(booking.DepositPaymentId!.Value, cancellationToken);
        return Record(booking, payment, now);
    }

    /// <summary>
    /// Whether <paramref name="payment"/> is the one this booking's refund would be recorded on: its
    /// own payment, applied. Asked before a booking is changed, so a caller that cannot record the
    /// refund never records the ending either.
    /// </summary>
    public static bool CanRecord(Booking booking, Payment? payment)
    {
        ArgumentNullException.ThrowIfNull(booking);
        return payment is not null &&
               payment.Id == booking.DepositPaymentId &&
               payment.BookingId == booking.Id &&
               payment.Status == PaymentStatus.Applied;
    }

    /// <summary>
    /// What the customer's card would get back if they cancelled RIGHT NOW: the figure the cancel
    /// sheet states and <c>expectedRefund</c> is checked against. Null when cancelling would return
    /// nothing, including before anything was paid.
    /// </summary>
    public static Money? PreviewForCustomer(Booking booking, Payment? payment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(booking);
        if (payment is null || booking.DepositPaymentId is null || payment.Id != booking.DepositPaymentId)
            return null;

        return PreviewForCustomer(booking, payment.WholePaymentRefundAmount, payment.RefundableFee, now);
    }

    /// <summary>
    /// The same figure from the two facts the payment contributes, for a caller that holds its read
    /// model rather than the aggregate — the booking DTO's cancel sheet. One rule, two doors: the
    /// figure a screen promises and the one <c>expectedRefund</c> is checked against cannot differ.
    /// </summary>
    /// <param name="wholePaymentRefund">The payment's <c>WholePaymentRefundAmount</c>; null until captured.</param>
    /// <param name="refundableFee">The payment's <c>RefundableFee</c>.</param>
    public static Money? PreviewForCustomer(Booking booking, Money? wholePaymentRefund, Money refundableFee, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(refundableFee);
        if (booking.DepositPaymentId is null || wholePaymentRefund is null)
            return null;
        if (!booking.CanBeCancelled(now) || booking.PickedUpAt is not null)
            return null;

        var preview = booking.PreviewRefundOnCancellation(BookingParty.Customer, now);
        if (preview.IsNothing)
            return null;

        var amount = preview.WholePayment
            ? Money.Create(wholePaymentRefund.Amount, wholePaymentRefund.CurrencyCode)
            // The money above the deposit, and the fee that goes back with it.
            : Money.Create(preview.BookingPart.Amount, preview.BookingPart.CurrencyCode).Add(refundableFee);
        return amount.IsZero ? null : amount;
    }

    /// <summary>Who returned the whole payment decides the reason the refund is filed under.</summary>
    private static RefundReason WholePaymentReason(Booking booking) =>
        booking.CancelledBy == BookingParty.Admin ? RefundReason.PlatformCancellation : RefundReason.FreeCancellation;
}
