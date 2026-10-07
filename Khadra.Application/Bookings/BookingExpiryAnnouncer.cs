using Khadra.Application.Notifications;
using Khadra.Domain.Bookings;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Notifications;

namespace Khadra.Application.Bookings;

/// <summary>
/// Tells the customer that a booking expired, and the office when an approval it gave lapsed unpaid — the ONE place
/// an expiry is announced (Wave 4, checklist 234).
/// </summary>
/// <remarks>
/// <para>
/// It stages the notifications on the tracker of whoever settled the lapse, and saves nothing: they persist exactly
/// when that unit of work saves the expiry, and never when it does not. So a read that settles a booking on load and
/// writes nothing announces nothing, and a command that settles it and saves for its own reason announces it once,
/// with the expiry.
/// </para>
/// <para>
/// An expiry used to be announced only by the two paths that expired a booking themselves — the settlement sweep and
/// a new request clearing a stale hold. A lapse settled on load and saved by any other command was told to nobody:
/// most plainly a capture landing seconds after the payment deadline, which expires the booking as it is loaded,
/// orphans the money, and commits both.
/// </para>
/// <para>
/// Fed per lapse (Fix &amp; Polish Wave 3, C7): the office hears that an approval lapsed unpaid, as Khadra, and never
/// that a request it let lapse did.
/// </para>
/// </remarks>
public sealed class BookingExpiryAnnouncer(DealerTeamNotifier team, IDealerRepository dealers)
{
    /// <param name="dealer">The booking's office, when the caller already holds it; read otherwise.</param>
    public async Task AnnounceAsync(
        Booking booking,
        BookingLapseKind lapse,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        Dealer? dealer = null)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(lapse);

        // An office that has since left the platform does not cost the customer the news.
        dealer ??= await dealers.GetByIdAsync(booking.DealerId, cancellationToken);

        await team.NotifyCustomerAsync(
            booking.CustomerId,
            dealer?.BusinessName.Value ?? string.Empty,
            NotificationKind.YourBookingExpired,
            now,
            booking.Id,
            booking.Reference.Value);

        if (dealer is not null && lapse == BookingLapseKind.Unpaid)
            await team.NotifyTeamFromPlatformAsync(dealer, NotificationKind.BookingExpiredUnpaid, now, booking.Id, booking.Reference.Value);
    }
}
