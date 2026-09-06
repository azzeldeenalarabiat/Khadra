using Khadra.Domain.Common;
using Khadra.Domain.Notifications;
using Khadra.Domain.Notifications.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence.Repositories;

internal sealed class NotificationRepository(KhadraDbContext context) : INotificationRepository
{
    public Task<Notification?> GetAsync(Id notificationId, CancellationToken cancellationToken = default) =>
        context.Notifications.FirstOrDefaultAsync(
            notification => notification.Id == notificationId,
            cancellationToken);

    // (occurred_at DESC, id DESC): occurred_at alone is not a total order, and a non-total order
    // lets a page boundary drop a row — the same defect the audit log documents.
    public async Task<IReadOnlyList<Notification>> ListForAsync(
        Id recipientUserId,
        int skip,
        int take,
        CancellationToken cancellationToken = default) =>
        await context.Notifications
            .Where(notification => notification.RecipientUserId == recipientUserId)
            .OrderByDescending(notification => notification.OccurredAt)
            .ThenByDescending(notification => notification.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountForAsync(Id recipientUserId, CancellationToken cancellationToken = default) =>
        context.Notifications
            .CountAsync(notification => notification.RecipientUserId == recipientUserId, cancellationToken);

    public Task<int> CountUnreadForAsync(Id recipientUserId, CancellationToken cancellationToken = default) =>
        context.Notifications.CountAsync(
            notification => notification.RecipientUserId == recipientUserId && notification.ReadAt == null,
            cancellationToken);

    /// <summary>
    /// Loads only the unread rows and marks them through the aggregate.
    ///
    /// Deliberately NOT an ExecuteUpdate: the domain decides what "read" means (idempotent, first
    /// moment wins), and a bulk SQL update would go round the aggregate and round the change tracker,
    /// so the shadow updated_at and any future invariant would both be skipped. The set is bounded by
    /// what one person has not yet read, which is small by construction.
    /// </summary>
    public async Task<int> MarkAllReadAsync(
        Id recipientUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var unread = await context.Notifications
            .Where(notification =>
                notification.RecipientUserId == recipientUserId && notification.ReadAt == null)
            .ToListAsync(cancellationToken);

        foreach (var notification in unread)
            notification.MarkRead(now);

        return unread.Count;
    }
}

// Stages rows on the same DbContext the caller is about to save.
//
// No SaveChanges here, for the reason AuditTrail gives: the notification and the action it describes
// commit together or not at all. Raising from a domain event instead would be at-most-once, because
// UnitOfWork dispatches events after the commit has already happened.
internal sealed class Notifier(KhadraDbContext context) : INotifier
{
    public void Raise(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        context.Notifications.Add(notification);
    }

    public void RaiseMany(IEnumerable<Notification> notifications)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        context.Notifications.AddRange(notifications);
    }
}
