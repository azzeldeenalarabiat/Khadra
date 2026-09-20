using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.Common.Ports;

/// <summary>
/// The messages the platform sends a customer about a booking, in Arabic and English together.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="IAuthEmailComposer"/> because the two have almost nothing in common
/// beyond being email. An auth message is one sentence and a link to a single-use token; a booking
/// message carries a car, a reference, a sum of money and a deadline, and it has to be readable by
/// somebody standing in a street deciding whether to go and pay.
/// </para>
/// <para>
/// It takes the <see cref="BookingDto"/> the handler already has rather than the aggregate, so the
/// email states the same reference, the same deposit and the same deadline the customer's own screen
/// states. Two renderings of one booking that disagree is a support call nobody can answer.
/// </para>
/// </remarks>
public interface IBookingEmailComposer
{
    /// <summary>
    /// A gallery has said yes, and the deposit is now owed by a deadline that will not wait.
    /// </summary>
    /// <param name="note">The gallery's own words, if they left any. Untranslated and untrusted.</param>
    EmailMessage BookingApproved(User customer, BookingDto booking, BookingContext context, string? note);
}
