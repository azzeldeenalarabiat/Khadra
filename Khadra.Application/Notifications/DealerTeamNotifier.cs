using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications;
using Khadra.Domain.Notifications.Repositories;

namespace Khadra.Application.Notifications;

/// <summary>
/// Tells a dealership's people what one of them just did.
///
/// This is the producer that actually exists today. The design imagines notifications arriving from
/// the customer's side — a new request, a cancellation — and none of those can be raised here: the
/// customer flow is the Flutter app, which is not in this repository, and there is no scheduler for
/// the time-based ones. What IS true is that a dealership is a team: when one member of staff answers
/// a request or hands a car over, that is news to the owner and to every colleague who shares the
/// booking book with them. Spec 4.2 asks for exactly this accountability, and until now it existed
/// only on the booking's own history where nobody was looking.
///
/// The actor is never notified of their own action. They were there.
///
/// Nothing is saved here: rows are STAGED on the caller's DbContext and committed by the caller's own
/// SaveChangesAsync, so the notification and the action it describes land in one transaction or not
/// at all. That is why this takes an <see cref="INotifier"/> and not a repository.
/// </summary>
public sealed class DealerTeamNotifier(INotifier notifier, IUserRepository users)
{
    /// <summary>
    /// Everyone at <paramref name="dealer"/> except the actor, told that something happened.
    ///
    /// The actor's NAME is snapshotted onto each row, following <c>AuditEntry</c>: "Ahmad approved
    /// KR-1042" has to still read correctly after Ahmad is renamed or leaves. Only the actor's name
    /// is stored — never the customer's, because this table is not deleted from.
    /// </summary>
    public async Task NotifyTeamAsync(
        Dealer dealer,
        Id actorUserId,
        NotificationKind kind,
        DateTimeOffset now,
        Id? subjectId = null,
        string? subjectReference = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dealer);
        ArgumentNullException.ThrowIfNull(kind);

        var recipients = Recipients(dealer, exceptUserId: actorUserId);
        if (recipients.Count == 0)
            return;

        var actorName = await NameOfAsync(actorUserId, cancellationToken);
        notifier.RaiseMany(recipients.Select(recipient =>
            Notification.Raise(recipient, kind, actorName, now, subjectId, subjectReference, actorUserId)));
    }

    /// <summary>
    /// One named person, told about something that happened TO them — a grant, a deactivation.
    ///
    /// Separate from the team feed because the recipient is the subject, not a bystander, and because
    /// the actor here is their owner or an administrator rather than a colleague.
    /// </summary>
    public async Task NotifyPersonAsync(
        Id recipientUserId,
        Id? actorUserId,
        NotificationKind kind,
        DateTimeOffset now,
        Id? subjectId = null,
        string? subjectReference = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kind);

        if (recipientUserId.IsEmpty || recipientUserId == actorUserId)
            return;

        var actorName = actorUserId is { } actor
            ? await NameOfAsync(actor, cancellationToken)
            : PlatformActorName;

        notifier.Raise(Notification.Raise(
            recipientUserId, kind, actorName, now, subjectId, subjectReference, actorUserId));
    }

    /// <summary>The owner and every ACTIVE employee. A deactivated one has no standing (spec 4.2).</summary>
    private static List<Id> Recipients(Dealer dealer, Id exceptUserId)
    {
        var recipients = new List<Id>();
        if (dealer.OwnerUserId != exceptUserId)
            recipients.Add(dealer.OwnerUserId);

        recipients.AddRange(dealer.Employees
            .Where(employee => employee.IsActive && employee.UserId != exceptUserId)
            .Select(employee => employee.UserId));

        return recipients;
    }

    /// <summary>
    /// The actor's name at the moment they acted.
    ///
    /// A missing user is possible — soft-deleted, or a race with deactivation — and is not worth
    /// failing an approved booking over, so it degrades to a label rather than throwing.
    /// </summary>
    private async Task<string> NameOfAsync(Id userId, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user?.Name.Value ?? UnknownActorName;
    }

    private const string PlatformActorName = "Khadra";
    private const string UnknownActorName = "A colleague";
}
