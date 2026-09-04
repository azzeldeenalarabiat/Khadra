using CSharpFunctionalExtensions;
using Khadra.Application.AdminDashboard.Dtos;
using Khadra.Application.Common;
using Khadra.Application.Dealers.ReadModels;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.AdminDashboard.GetDealerCounts;

/// <summary>
/// The dealer KPI card: how the platform's rental offices are distributed across the licence check.
///
/// One reader, one aggregate, one card. This used to be a slice of a composite that also loaded the
/// booking trend, the work queue and the audit feed, so a screen wanting the dealer figures paid for
/// all of it; now nothing else is fetched to answer this.
/// </summary>
public sealed record GetDealerCountsQuery : IQuery<Result<DealerCountsDto, Error>>;

public sealed class GetDealerCountsHandler(IDealerDashboardReader dealers, IClock clock)
    : IRequestHandler<GetDealerCountsQuery, Result<DealerCountsDto, Error>>
{
    public async Task<Result<DealerCountsDto, Error>> Handle(
        GetDealerCountsQuery request,
        CancellationToken cancellationToken)
    {
        var counts = await dealers.CountsAsync(cancellationToken);

        return new DealerCountsDto(
            clock.UtcNow,
            counts.Total,
            counts.Trading,
            counts.PendingReview,
            counts.ClarificationNeeded,
            counts.Rejected,
            counts.Suspended);
    }
}
