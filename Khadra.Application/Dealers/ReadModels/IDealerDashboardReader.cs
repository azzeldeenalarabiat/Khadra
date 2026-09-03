using Khadra.Domain.Common;

namespace Khadra.Application.Dealers.ReadModels;

// The Dealers context publishes its own read contract; the admin dashboard consumes it. The interface
// lives here rather than in Common/Ports so that extracting this context later means replacing one
// implementation with an HTTP client, not untangling a shared file.

/// <summary>
/// Dealer headcounts for the dashboard. The buckets partition the total, which the design's labels do
/// not: verification and suspension are orthogonal in the domain (an approved dealer stays approved
/// while suspended), so "Approved" and "Suspended" would double-count the same rows.
/// </summary>
/// <param name="Total">Every dealer that has not been soft-deleted.</param>
/// <param name="Trading">Approved and not suspended: the ones who can actually take bookings.</param>
/// <param name="PendingReview">Awaiting a first admin decision.</param>
/// <param name="ClarificationNeeded">Sent back to the dealer with a note.</param>
/// <param name="Rejected">Turned down.</param>
/// <param name="Suspended">Sanctioned, whatever their verification status.</param>
public sealed record DealerCounts(
    int Total,
    int Trading,
    int PendingReview,
    int ClarificationNeeded,
    int Rejected,
    int Suspended);

/// <summary>An application an admin still owes a decision on.</summary>
public sealed record PendingDealerApplication(
    Id DealerId,
    string BusinessName,
    DateTimeOffset SubmittedAt,
    DateTimeOffset ReviewDueAt);

public sealed record DealerName(Id DealerId, string BusinessName);

public interface IDealerDashboardReader
{
    Task<DealerCounts> CountsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingDealerApplication>> PendingApplicationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Display names for dealers referenced by another context. Cross-context references are by id,
    /// so the dashboard resolves labels through this rather than joining across context boundaries.
    /// </summary>
    Task<IReadOnlyList<DealerName>> NamesAsync(
        IReadOnlyCollection<Id> dealerIds,
        CancellationToken cancellationToken = default);
}
