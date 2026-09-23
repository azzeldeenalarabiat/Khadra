using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Notifications;

namespace Khadra.Application.Notifications.Delivery;

/// <summary>The two lines a phone shows for a notification.</summary>
public sealed record PushText(string Title, string Body);

/// <summary>
/// Turns a notification into words for a person who is NOT looking at the app.
/// </summary>
/// <remarks>
/// The in-app list composes its own lines on the phone, from the kind, in whatever language the app is
/// in. A push cannot: the app may not be running at all when it arrives, so the server writes it, once
/// per phone, in the language that phone registered with. The two are kept saying the same thing by
/// hand, kind by kind; a kind this composer does not know gets a neutral line, never nothing.
/// </remarks>
public interface INotificationMessageComposer
{
    PushText ComposePush(Notification notification, Language language);

    /// <summary>
    /// The email for a kind delivered by email, in the recipient's chosen language — or in both, Arabic
    /// first, when they have never chosen, which is how every other email on the platform reads.
    /// </summary>
    EmailMessage ComposeEmail(Notification notification, User recipient);
}
