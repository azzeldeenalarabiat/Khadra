using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications;
using Khadra.Domain.Notifications.Repositories;

namespace Khadra.Application.Notifications;

/// <summary>
/// Tells a dealership's people what just happened to their booking book.
///
/// Most of it is what one of THEM did: a dealership is a team, and when one member of staff answers a
/// request or hands a car over, that is news to the owner and to every colleague who shares the
/// booking book with them. Spec 4.2 asks for exactly this accountability, and until this existed it
/// lived only on the booking's own history where nobody was looking.
///
/// Since 2026-09-07 one thing arrives from outside the dealership as well — a customer asking for a
/// car — and that goes through NotifyTeamOfCustomerActionAsync, which names nobody. The remaining
/// customer-side notifications the design draws still have no producer: there is no
/// customer-cancellation endpoint and no scheduler for the time-based ones.
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
    /// Everyone at <paramref name="dealer"/>, told that a CUSTOMER did something.
    /// </summary>
    /// <remarks>
    /// Nobody is excluded, because the actor is not one of them.
    ///
    /// And nobody is named. Every other row here snapshots the actor's name so the line still reads
    /// correctly after that person is renamed or leaves; doing the same with a customer would copy
    /// their name into a table that is never deleted from, which is a promise about their data this
    /// platform has not made (spec 7). The dealership can see whose booking it is on the booking
    /// itself, where it belongs and where deleting an account removes it. So the row says "a
    /// customer" and carries no actor id at all.
    /// </remarks>
    public Task NotifyTeamOfCustomerActionAsync(
        Dealer dealer,
        NotificationKind kind,
        DateTimeOffset now,
        Id? subjectId = null,
        string? subjectReference = null)
    {
        ArgumentNullException.ThrowIfNull(dealer);
        ArgumentNullException.ThrowIfNull(kind);

        var recipients = Recipients(dealer, exceptUserId: Id.Empty);
        if (recipients.Count == 0)
            return Task.CompletedTask;

        notifier.RaiseMany(recipients.Select(recipient =>
            Notification.Raise(recipient, kind, CustomerActorName, now, subjectId, subjectReference)));

        return Task.CompletedTask;
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

    // Deliberately not a name. See NotifyTeamOfCustomerActionAsync.
    private const string CustomerActorName = "A customer";
}
