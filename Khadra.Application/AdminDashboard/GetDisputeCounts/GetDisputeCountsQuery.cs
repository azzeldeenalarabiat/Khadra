using CSharpFunctionalExtensions;
using Khadra.Application.AdminDashboard.Dtos;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Disputes.ReadModels;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.AdminDashboard.GetDisputeCounts;

/// <summary>The dispute KPI card: what is open, what has run over, and what was settled recently.</summary>
public sealed record GetDisputeCountsQuery : IQuery<Result<DisputeCountsDto, Error>>;

public sealed class GetDisputeCountsHandler(
    IDisputeDashboardReader disputes,
    IAdminDashboardSettings settings,
    IClock clock)
    : IRequestHandler<GetDisputeCountsQuery, Result<DisputeCountsDto, Error>>
{
    public async Task<Result<DisputeCountsDto, Error>> Handle(
        GetDisputeCountsQuery request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var resolvedSince = now.AddDays(-settings.ResolvedDisputeWindowDays);
        var counts = await disputes.CountsAsync(resolvedSince, now, cancellationToken);

        // The window travels with the figure. "4 resolved" means nothing without "in 30 days", and
        // the console must never be the thing that remembers which 30.
        return new DisputeCountsDto(
            now,
            counts.Open,
            counts.UnderReview,
            counts.Overdue,
            counts.ResolvedInWindow,
            settings.ResolvedDisputeWindowDays);
    }
}
