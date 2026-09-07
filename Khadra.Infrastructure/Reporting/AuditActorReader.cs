using Khadra.Application.Auditing.ReadAuditLog;
using Khadra.Application.Auditing.ReadModels;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

internal sealed class AuditActorReader(KhadraDbContext context) : IAuditActorReader
{
    public async Task<IReadOnlyList<AuditActorDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        // Grouped over the ENTRIES, so someone who has left the platform is still offered as a filter
        // and their decisions stay findable. Grouping on the id AND the snapshotted name together
        // means a person renamed between two actions appears once per name they acted under -- which
        // is the honest reading of a log that snapshots names deliberately.
        // Projected to an anonymous type first, and the Id? unwrapped afterwards. Building the DTO
        // inside the query means calling .Value.Value on a nullable value object behind a converter,
        // which EF cannot translate -- it compiles, then throws at runtime on the one screen this
        // reader exists for. The grouping, counting and ordering all still happen in SQL; only the
        // unwrap is in memory, over one row per distinct actor.
        var actors = await context.AuditEntries
            .GroupBy(entry => new { entry.ActorUserId, entry.ActorName })
            .Select(group => new
            {
                group.Key.ActorUserId,
                group.Key.ActorName,
                EntryCount = group.Count(),
            })
            // Busiest first: an auditor looking for "who did most of this" starts at the top, and the
            // list is short enough that alphabetical would bury the interesting names.
            .OrderByDescending(actor => actor.EntryCount)
            .ThenBy(actor => actor.ActorName)
            .ToListAsync(cancellationToken);

        return [.. actors.Select(actor =>
            new AuditActorDto(actor.ActorUserId?.Value, actor.ActorName, actor.EntryCount))];
    }
}
