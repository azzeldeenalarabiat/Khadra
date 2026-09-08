using CSharpFunctionalExtensions;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;

namespace Khadra.Application.Bookings;

/// <summary>
/// The one seam through which Payments touches a Booking.
/// </summary>
/// <remarks>
/// <para>
/// The twin of <see cref="BookingDisputeSettlement"/>, and it exists for the same reason: taking a
/// deposit has to read what the booking says is owed and then confirm it, and both are the Booking
/// context's business. A class rather than a port or a message, because it runs in the SAME
/// transaction as the payment and must succeed or fail with it — this project has no outbox, and
/// <c>UnitOfWork</c> dispatches domain events only after the commit, so a cross-context event lost
/// between the two would mean money captured, booking unconfirmed, and the car released at its
/// deadline. That is the worst failure the system can have, and it is why the seam is a call.
/// </para>
/// <para>
/// <b><see cref="Confirm"/> is the only production caller of <c>Booking.ConfirmDepositPaid</c>.</b>
/// The aggregate accepts any non-empty payment id, deliberately — it cannot know what a real one
/// looks like — so nothing in it would stop a future handler passing <c>Id.New()</c> and marking a
/// booking paid against a payment that never existed. This class is where that is prevented: every
/// id that reaches the aggregate through here belongs to a <c>Payment</c> row whose capture the
/// provider signed for.
/// </para>
/// </remarks>
public static class BookingDepositSettlement
{
    /// <summary>
    /// What the customer must pay to confirm this booking, or why they may not pay it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the ONE place the payment deadline is enforced. Once a provider has captured, the
    /// clock is no longer the question — the money has moved, and refusing it at that point would
    /// mean keeping it. So the deadline gates the DOOR, not the confirmation:
    /// <c>ConfirmDepositPaid</c> is deliberately deadline-blind, and pre-launch item 62 is the
    /// reasoning.
    /// </para>
    /// <para>
    /// The amount is a FRESH <see cref="Money"/> rather than the booking's own tracked instance,
    /// which EF would otherwise see owned by two aggregates at once — the same rule
    /// <see cref="BookingDisputeSettlement.DepositHeldFor"/> follows.
    /// </para>
    /// </remarks>
    public static Result<Money, Error> DepositDue(Booking booking, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(booking);

        if (booking.DepositPaymentId is not null)
            return BookingErrors.NotAwaitingPayment;
        if (!booking.IsAwaitingPayment(now))
            return BookingErrors.NotAwaitingPayment;

        return Money.Create(booking.Pricing.DepositAmount.Amount, booking.Pricing.CurrencyCode);
    }

    /// <summary>
    /// The deposit cleared. Confirms the booking, or says why the capture has nowhere to go.
    /// </summary>
    /// <remarks>
    /// Idempotent through the aggregate: a payment id already on the booking is a success, because a
    /// provider retrying its own webhook must not be told no and keep retrying.
    /// </remarks>
    public static UnitResult<Error> Confirm(Booking booking, Id paymentId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(booking);
        if (paymentId.IsEmpty)
            throw new DomainException("Confirming a deposit requires the payment that paid it.");

        return booking.ConfirmDepositPaid(paymentId, now);
    }

    /// <summary>
    /// Why a capture could not be applied, as a code an admin screen can group on.
    /// </summary>
    /// <remarks>
    /// Derived from the booking rather than from the failed confirmation's error, so the record says
    /// what was true of the rental — expired, cancelled, already paid — rather than which guard
    /// happened to fire first.
    /// </remarks>
    public static string OrphanReasonFor(Booking booking, Id paymentId)
    {
        ArgumentNullException.ThrowIfNull(booking);

        if (booking.DepositPaymentId is { } paid && paid != paymentId)
            return "AlreadyPaidByAnotherAttempt";
        if (booking.Status == BookingStatus.Expired)
            return "BookingExpired";
        if (booking.Status == BookingStatus.Cancelled)
            return "BookingCancelled";
        if (booking.Status == BookingStatus.Rejected)
            return "BookingRejected";
        if (booking.Status == BookingStatus.NoShow)
            return "BookingNoShow";
        return "BookingNotAwaitingPayment";
    }
}
