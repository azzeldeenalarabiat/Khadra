using Khadra.Application.Auditing.ReadModels;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

internal sealed class AuditFeedReader(KhadraDbContext context) : IAuditFeedReader
{
    public async Task<IReadOnlyList<ActivityEntry>> RecentAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0)
            return [];

        // Smart enums are stored by name, and the feed wants exactly that name so the client can map
        // it to an icon and a verb. Projecting the enumeration object would materialise it only to
        // read .Name back off it.
        return await context.AuditEntries
            .OrderByDescending(entry => entry.OccurredAt)
            .Take(count)
            .Select(entry => new ActivityEntry(
                entry.Id,
                entry.OccurredAt,
                entry.ActorName,
                entry.Action.Name,
                entry.EntityType.Name,
                entry.SubjectLabel))
            .ToListAsync(cancellationToken);
    }
}
