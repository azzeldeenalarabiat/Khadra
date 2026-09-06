using Khadra.Domain.Common;

namespace Khadra.Domain.Notifications.Repositories;

/// <summary>
/// Read/write access to one person's notifications.
///
/// Every method is scoped by recipient. There is deliberately no "get by id" that does not also take
/// the user: an id alone is guessable, and a notification is the only place in this platform where
/// one member of staff could otherwise learn what another was told.
/// </summary>
public interface INotificationRepository
{
    Task<Notification?> GetAsync(Id notificationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Notification>> ListForAsync(
        Id recipientUserId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    Task<int> CountForAsync(Id recipientUserId, CancellationToken cancellationToken = default);

    Task<int> CountUnreadForAsync(Id recipientUserId, CancellationToken cancellationToken = default);

    /// <summary>Marks every unread row for this person read, and answers how many changed.</summary>
    Task<int> MarkAllReadAsync(
        Id recipientUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Write side, and the reason this is a port rather than a repository call in each handler.
///
/// <c>Raise</c> only STAGES the rows; the calling handler's <c>IUnitOfWork.SaveChangesAsync</c>
/// commits them in the SAME transaction as the action they describe. That is the whole point, and it
/// is the lesson <see cref="Auditing.Repositories.IAuditTrail"/> already records: domain events here
/// dispatch after commit with no outbox, so a notification raised from an event handler is
/// at-most-once — the booking gets approved and nobody is told, with nothing to show it went missing.
/// </summary>
public interface INotifier
{
    void Raise(Notification notification);

    void RaiseMany(IEnumerable<Notification> notifications);
}
