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
    /// </summary>
    public static Money DepositHeldFor(Booking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);

        var currency = booking.Pricing.CurrencyCode;
        return booking.DepositPaymentId is null
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
