using CSharpFunctionalExtensions;
using Khadra.Application.AdminDashboard.Dtos;
using Khadra.Application.Auditing.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.AdminDashboard.GetRecentActivity;

/// <summary>
/// The dashboard's activity strip: the last few privileged actions, newest first.
///
/// Deliberately NOT the audit log. This is a fixed-size glance sized by
/// AdminDashboard:ActivityFeedSize, with no filters and no paging; the audit-log screen is a
/// searchable, paged read over the same append-only table and gets its own reader when it is built.
/// Two consumers of one table, with different questions.
/// </summary>
public sealed record GetRecentActivityQuery : IQuery<Result<ActivityFeedDto, Error>>;

public sealed class GetRecentActivityHandler(
    IAuditFeedReader auditFeed,
    IAdminDashboardSettings settings,
    IClock clock)
    : IRequestHandler<GetRecentActivityQuery, Result<ActivityFeedDto, Error>>
{
    public async Task<Result<ActivityFeedDto, Error>> Handle(
        GetRecentActivityQuery request,
        CancellationToken cancellationToken)
    {
        var entries = await auditFeed.RecentAsync(settings.ActivityFeedSize, cancellationToken);

        return new ActivityFeedDto(
            clock.UtcNow,
            [.. entries.Select(entry => new ActivityEntryDto(
                entry.Id.Value,
                entry.OccurredAt,
                entry.ActorName,
                entry.Action,
                entry.EntityType,
                entry.SubjectLabel))]);
    }
}
