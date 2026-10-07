using Khadra.Domain.Common;

namespace Khadra.Domain.Notifications.Repositories;

/// <summary>
/// A delivery one dispatcher has claimed, and the claim count its claim left: the proof it is still that dispatcher's
/// to work (<see cref="NotificationDelivery.Attempts"/>; pre-launch item 204).
/// </summary>
public sealed record ClaimedNotificationDelivery(Id DeliveryId, int Claims);

/// <summary>The outbox the notification dispatcher works from.</summary>
public interface INotificationDeliveryRepository
{
    /// <summary>
    /// Claims up to <paramref name="batchSize"/> pending deliveries that are due, oldest first, and returns each with
    /// the claim count its claim left.
    /// </summary>
    /// <remarks>
    /// Claiming is atomic and exclusive: each claimed row has its attempt count raised and its next
    /// attempt pushed <paramref name="lease"/> into the future in the same statement that selects it,
    /// with rows another process is claiming at that instant skipped rather than waited for. Two
    /// dispatchers — the old and new process during a zero-downtime deploy — therefore never take the
    /// same row, and a process that dies holding one releases it when the lease runs out.
    /// </remarks>
    Task<IReadOnlyList<ClaimedNotificationDelivery>> ClaimDueAsync(
        DateTimeOffset now,
        TimeSpan lease,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews the lease on a claimed delivery to <paramref name="leaseUntil"/>, but only while it is still pending and
    /// its claim count is still <paramref name="claims"/>. False means another process has claimed it since, after
    /// this one outlived its lease, and the row is that process's to work. Applied at once, not staged.
    /// </summary>
    Task<bool> TryRenewClaimAsync(
        Id deliveryId,
        int claims,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken = default);

    /// <summary>The delivery, tracked, ready to record an outcome on.</summary>
    Task<NotificationDelivery?> GetAsync(Id deliveryId, CancellationToken cancellationToken = default);
}
