using CSharpFunctionalExtensions;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;

namespace Khadra.Application.Bookings;

/// <summary>
/// The one seam through which Disputes touches a Booking.
///
/// Resolving a ticket has to read the deposit the booking holds and then close the booking out, and
/// both are the Booking context's business. Rather than let the Disputes handler reach into the
/// aggregate ad hoc, the two operations live here, owned by Bookings, so the boundary stays a named
/// door instead of a hole in the wall. Not a port and not a message: a class, because it is called
/// in the same transaction as the ticket and must succeed or fail with it. Static because it holds no
/// state and needs no collaborators; the seam is the named door, not an object.
/// </summary>
public static class BookingDisputeSettlement
{
    /// <summary>
    /// The deposit the platform holds for this booking -- as a FRESH Money, never the booking's own
    /// tracked instance, which EF would otherwise see in two aggregates at once.
    ///
    /// Read from the booking's frozen pricing, never from current settings. And a booking cancelled
    /// before its deposit was ever paid holds nothing: the disposition then has to be all zeros, and
    /// DepositDisposition.Create will insist on exactly that.
    ///
    /// Nor does a booking whose deposit goes back because the customer cancelled it inside the free
    /// window (owner, 2026-09-24): it stays disputable for the settlement window, and without this a
    /// resolution could split money already on its way back. Judged by the booking's own rule, never
    /// by the refund's status — a refused refund is still owed and still being re-sent, so reading
    /// "captured minus refunded" (which does not count a failed refund) would offer that deposit to a
    /// dispute a second time. See pre-launch item 78.
    /// </summary>
    /// <remarks>
    /// The DEPOSIT, however much was paid online. A customer who paid the full amount has put the
    /// office's rental revenue on the platform too, and that part is never at stake in a dispute
    /// (owner, 2026-09-24): it is the office's once the rental happens and the customer's if it does
    /// not. Only the deposit is ever split.
    /// </remarks>
    /// <param name="releasedOnCleanClose">
    /// Whether the deposit already went back because the dispute window closed with no claim on it
    /// (owner, 2026-09-26). Only a ticket opened in the very instant the window closed can meet one —
    /// the release waits for the window, and a ticket cannot be opened after it — and such a ticket
    /// holds nothing to split, rather than paying out money the customer already has.
    /// </param>
    public static Money DepositHeldFor(Booking booking, bool releasedOnCleanClose = false)
    {
        ArgumentNullException.ThrowIfNull(booking);

        var currency = booking.Pricing.CurrencyCode;
        // Nothing is held once the ending returned the whole payment — a customer's free cancellation
        // or an administrator's (owner, 2026-09-26) — or once the window released it.
        return booking.DepositPaymentId is null || booking.ReturnsWholePayment || releasedOnCleanClose
            ? Money.ZeroIn(currency)
            : Money.Create(booking.Pricing.DepositAmount.Amount, currency);
    }

    /// <summary>
    /// Closes the booking after its dispute is resolved. A no-op for a booking that already ended;
    /// a transition for one still waiting out its settlement window.
    /// </summary>
    public static UnitResult<Error> CloseAfterDispute(Booking booking, Id adminUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(booking);
        return booking.CloseAfterDisputeResolved(adminUserId, now);
    }
}
