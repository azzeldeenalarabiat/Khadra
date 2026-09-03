using Khadra.Application.Dealers.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

// Read-side only: counts and labels, never aggregates. IDealerRepository loads whole dealers with
// their employees and documents because a command is about to change one; a dashboard that used it
// would pull the entire table into memory to produce six integers.
internal sealed class DealerDashboardReader(KhadraDbContext context) : IDealerDashboardReader
{
    public async Task<DealerCounts> CountsAsync(CancellationToken cancellationToken = default)
    {
        var pendingReview = DealerVerificationStatus.PendingReview;
        var approved = DealerVerificationStatus.Approved;
        var rejected = DealerVerificationStatus.Rejected;
        var clarification = DealerVerificationStatus.ClarificationNeeded;

        // One round trip: conditional counts become COUNT(*) FILTER (WHERE ...) on PostgreSQL.
        var counts = await context.Dealers
            .GroupBy(_ => 1)
            .Select(group => new DealerCounts(
                group.Count(),
                group.Count(dealer => dealer.VerificationStatus == approved && !dealer.IsSuspended),
                group.Count(dealer => dealer.VerificationStatus == pendingReview),
                group.Count(dealer => dealer.VerificationStatus == clarification),
                group.Count(dealer => dealer.VerificationStatus == rejected),
                group.Count(dealer => dealer.IsSuspended)))
            .SingleOrDefaultAsync(cancellationToken);

        // No dealers at all: GroupBy yields no rows rather than a row of zeros.
        return counts ?? new DealerCounts(0, 0, 0, 0, 0, 0);
    }

    public async Task<IReadOnlyList<PendingDealerApplication>> PendingApplicationsAsync(
        CancellationToken cancellationToken = default)
    {
        var pendingReview = DealerVerificationStatus.PendingReview;

        return await context.Dealers
            .Where(dealer => dealer.VerificationStatus == pendingReview)
            .OrderBy(dealer => dealer.ReviewDueAt)
            .Select(dealer => new PendingDealerApplication(
                dealer.Id,
                dealer.BusinessName.Value,
                dealer.SubmittedAt,
                dealer.ReviewDueAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DealerName>> NamesAsync(
        IReadOnlyCollection<Id> dealerIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dealerIds);
        if (dealerIds.Count == 0)
            return [];

        var ids = dealerIds.ToList();
        return await context.Dealers
            .Where(dealer => ids.Contains(dealer.Id))
            .Select(dealer => new DealerName(dealer.Id, dealer.BusinessName.Value))
            .ToListAsync(cancellationToken);
    }
}
