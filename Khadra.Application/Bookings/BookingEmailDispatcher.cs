using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess.Repositories;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.Bookings;

/// <summary>
/// Tells a customer, by email, that a gallery has approved their booking.
/// </summary>
/// <remarks>
/// <para>
/// <b>It cannot fail the approval, and it is not allowed to try.</b> The decision is committed
/// before this runs; a gallery that answered a request has answered it whatever a mail server does
/// next, and returning an error to the console would invite a retry that answers
/// <c>not_awaiting_decision</c> on a booking that is already approved. Every exception is caught and
/// logged, which is the third of the shapes <see cref="IdentityAccess.AuthEmailDispatcher"/> sets
/// out: a notice with nothing for the caller to do differently.
/// </para>
/// <para>
/// <b>Why email at all, when there is already an in-app notification.</b> There is no push channel
/// (pre-launch item 73) and the customer has two hours to pay. The in-app notification reaches
/// somebody who opens the app; this reaches somebody who does not. Item 90 is the record of how much
/// rides on it — an approval nobody reads is a lost rental, a wasted decision by the gallery, and a
/// car held for nothing.
/// </para>
/// <para>
/// <b>Not a domain event handler.</b> <c>DomainEventDispatcher</c> catches nothing, so a throwing
/// handler would turn a committed approval into a 500 for the gallery. Nothing on this platform
/// sends mail from a domain event, and this is why.
/// </para>
/// </remarks>
public sealed partial class BookingEmailDispatcher(
    IUserRepository users,
    IBookingEmailComposer composer,
    IEmailSender sender,
    ILogger<BookingEmailDispatcher> logger)
{
    /// <returns><c>true</c> when the mail server accepted the message.</returns>
    public async Task<bool> SendApprovalAsync(
        BookingDto booking,
        BookingContext context,
        string? note,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(booking);

        try
        {
            var customer = await users.GetByIdAsync(Id.From(booking.CustomerId), cancellationToken);
            if (customer is null)
            {
                LogNoRecipient(logger, booking.Reference, "the customer record is gone");
                return false;
            }

            // An unverified address is one nobody has proved they can read, and the platform has
            // already declined to send anything but a verification link to it.
            if (!customer.IsEmailVerified)
            {
                LogNoRecipient(logger, booking.Reference, "the address is not verified");
                return false;
            }

            // CancellationToken.None on purpose. The request's token is cancelled the moment the
            // gallery's browser drops the connection, which happens AFTER the commit — and this is
            // the one message on the platform where a send abandoned half-way is probably a booking
            // that expires unread. `Email:TimeoutSeconds` already bounds how long it can take.
            await sender.SendAsync(composer.BookingApproved(customer, booking, context, note), CancellationToken.None);
            return true;
        }
#pragma warning disable CA1031 // A delivery failure is reported to the log, never thrown at a gallery.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogDeliveryFailed(logger, booking.Reference, exception);
            return false;
        }
    }

    [LoggerMessage(1200, LogLevel.Error,
        "Failed to send the approval email for booking {Reference}. The approval stands.")]
    private static partial void LogDeliveryFailed(ILogger logger, string reference, Exception exception);

    [LoggerMessage(1201, LogLevel.Warning,
        "No approval email sent for booking {Reference}: {Reason}.")]
    private static partial void LogNoRecipient(ILogger logger, string reference, string reason);
}
