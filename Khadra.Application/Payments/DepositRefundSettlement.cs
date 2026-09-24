using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;

namespace Khadra.Application.Payments;

/// <summary>
/// The one seam through which Bookings touches a Payment: returning the deposit of a booking the
/// customer cancelled inside its free-cancellation window (owner, 2026-09-24).
/// </summary>
/// <remarks>
/// <para>
/// The mirror of <c>BookingDepositSettlement</c> (Payments touching a Booking) and owned, like it, by
/// the context whose aggregate it touches. A call rather than an event for the same reason: this
/// project has no outbox and dispatches domain events only after the commit, so a refund raised from
/// <c>BookingCancelled</c> could be lost between the two — a cancelled booking with the customer's
/// money held and no row saying it is owed. Here the refund is recorded in the SAME save as the
/// cancellation, or neither happens.
/// </para>
/// <para>
/// Recording is the initiation. The payment sweep sends every requested refund to the provider on
/// its next tick, under the refund's own id as the idempotency key, and keeps re-sending one the
/// provider refused. Nothing here calls the provider: a customer's cancellation must not wait on,
/// or fail because of, a processor.
/// </para>
/// </remarks>
public static class DepositRefundSettlement
{
    /// <summary>
    /// Records the full refund a free cancellation owes, idempotently.
    /// </summary>
    /// <remarks>
    /// Throws rather than returning a failure, deliberately. <c>DepositPaymentId</c> is only ever set in
    /// the transaction that applied that very payment, so a booking that returns its deposit and a
    /// payment that is missing, belongs elsewhere or cannot be refunded is a programming error — and
    /// letting the cancellation commit anyway would leave money held with nothing saying it is owed.
    /// The throw rolls the whole request back.
    /// </remarks>
    public static Refund RefundForFreeCancellation(Booking booking, Payment? payment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(booking);

        if (!booking.ReturnsDepositOnCancellation)
            throw new DomainException($"Booking {booking.Id} does not return its deposit on cancellation.");
        if (payment is null || payment.Id != booking.DepositPaymentId || payment.BookingId != booking.Id)
            throw new DomainException($"Booking {booking.Id}'s deposit payment could not be found to refund.");

        var refund = payment.RefundForFreeCancellation(now);
        if (refund.IsFailure)
            throw new DomainException($"Payment {payment.Id} could not refund booking {booking.Id}'s deposit: {refund.Error.Code}.");

        return refund.Value;
    }
}
