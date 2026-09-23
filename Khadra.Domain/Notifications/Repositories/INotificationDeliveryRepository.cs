using Khadra.Domain.Common;

namespace Khadra.Domain.Notifications.Repositories;

/// <summary>The outbox the notification dispatcher works from.</summary>
public interface INotificationDeliveryRepository
{
    /// <summary>
    /// Claims up to <paramref name="batchSize"/> pending deliveries that are due, and returns them
    /// TRACKED, ready to record an outcome on.
    /// </summary>
    /// <remarks>
    /// Claiming is atomic and exclusive: each claimed row has its attempt count raised and its next
    /// attempt pushed <paramref name="lease"/> into the future in the same statement that selects it,
    /// with rows another process is claiming at that instant skipped rather than waited for. Two
    /// dispatchers — the old and new process during a zero-downtime deploy — therefore never take the
    /// same row, and a process that dies holding one releases it when the lease runs out.
    /// </remarks>
    Task<IReadOnlyList<NotificationDelivery>> ClaimDueAsync(
        DateTimeOffset now,
        TimeSpan lease,
        int batchSize,
        CancellationToken cancellationToken = default);
}
