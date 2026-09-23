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
//
// The same goes for delivering it: every channel a kind is delivered on (push, email) gets its outbox
// row HERE, in the same SaveChanges, so the dispatcher can never be owed a notification that does not
// exist or miss one that does. Handlers stay exactly as they were; what a kind wakes is decided by the
// kind (NotificationKind.DeliveredOn), in one place.
internal sealed class Notifier(KhadraDbContext context) : INotifier
{
    public void Raise(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        context.Notifications.Add(notification);
        OweDeliveries(notification);
    }

    public void RaiseMany(IEnumerable<Notification> notifications)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        foreach (var notification in notifications)
            Raise(notification);
    }

    private void OweDeliveries(Notification notification)
    {
        // Due from the moment it happened: a reminder raised a little late is sent at once, not later.
        foreach (var channel in notification.Kind.DeliveredOn())
            context.NotificationDeliveries.Add(
                NotificationDelivery.Owe(notification.Id, channel, notification.OccurredAt));
    }
}

internal sealed class NotificationDeliveryRepository(KhadraDbContext context) : INotificationDeliveryRepository
{
    public async Task<IReadOnlyList<NotificationDelivery>> ClaimDueAsync(
        DateTimeOffset now,
        TimeSpan lease,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var leaseUntil = now.Add(lease);
        List<Guid> claimed;

        if (context.Database.IsNpgsql())
        {
            // One statement: choose, lock, lease and count. SKIP LOCKED is what makes two dispatchers
            // safe — a row another transaction is claiming is passed over, never waited on and never
            // taken twice. Ordered oldest-due first so a backlog drains in the order it built up.
            claimed = await context.Database
                .SqlQuery<Guid>($"""
                    UPDATE notification_deliveries AS d
                       SET attempts = d.attempts + 1,
                           next_attempt_at = {leaseUntil},
                           updated_at = {now}
                     WHERE d.id IN (
                           SELECT id FROM notification_deliveries
                            WHERE state = 'Pending' AND next_attempt_at <= {now}
                            ORDER BY next_attempt_at, id
                            LIMIT {batchSize}
                            FOR UPDATE SKIP LOCKED)
                    RETURNING d.id AS "Value"
                    """)
                .ToListAsync(cancellationToken);
        }
        else
        {
            // SQLite (the persistence tests) has one writer at a time and no row locks, so the plain
            // select-then-update is already exclusive there. Same effect, same columns.
            var due = await context.NotificationDeliveries
                .Where(delivery => delivery.State == DeliveryState.Pending && delivery.NextAttemptAt <= now)
                .OrderBy(delivery => delivery.NextAttemptAt)
                .Take(batchSize)
                .Select(delivery => delivery.Id)
                .ToListAsync(cancellationToken);
            var ids = due.ToList();
            await context.NotificationDeliveries
                .Where(delivery => ids.Contains(delivery.Id))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(delivery => delivery.Attempts, delivery => delivery.Attempts + 1)
                    .SetProperty(delivery => delivery.NextAttemptAt, leaseUntil), cancellationToken);
            claimed = ids.Select(id => id.Value).ToList();
        }

        if (claimed.Count == 0)
            return [];

        var keys = claimed.Select(Id.From).ToList();
        return await context.NotificationDeliveries
            .Where(delivery => keys.Contains(delivery.Id))
            .OrderBy(delivery => delivery.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
