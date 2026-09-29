using Khadra.Domain.Common;

namespace Khadra.Domain.FinancialDocuments.Repositories;

/// <summary>
/// A delivery one dispatcher has taken, and the claim count its claim left: the proof it is still that dispatcher's
/// to work (<see cref="FinancialDocumentDelivery.Claims"/>).
/// </summary>
public sealed record ClaimedFinancialDocumentDelivery(Id DeliveryId, int Claims);

/// <summary>The emails owed for issued documents (payments Phase 7).</summary>
public interface IFinancialDocumentDeliveryRepository
{
    void Add(FinancialDocumentDelivery delivery);

    /// <summary>
    /// Takes up to <paramref name="batchSize"/> queued deliveries that are due, oldest due first, counts the claim on
    /// each and holds each until <paramref name="now"/> + <paramref name="lease"/>, so no other dispatcher takes it
    /// meanwhile — and a dispatcher that dies lets it go when the lease runs out. Returns each with the count its claim
    /// left, which the dispatcher checks before it works the row.
    /// </summary>
    Task<IReadOnlyList<ClaimedFinancialDocumentDelivery>> ClaimDueAsync(
        DateTimeOffset now,
        TimeSpan lease,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>One delivery with its attempts, tracked, or null.</summary>
    Task<FinancialDocumentDelivery?> GetAsync(Id deliveryId, CancellationToken cancellationToken = default);

    /// <summary>Whether an email of the document is queued and not sent yet: one at a time.</summary>
    Task<bool> HasQueuedAsync(Id documentId, CancellationToken cancellationToken = default);
}
