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

        // Through the projection the audit log reads too (AuditRows), so the two cannot disagree about
        // which booking an entry is about. Smart enums come back by their stored name, which is exactly
        // what the client maps to an icon and a sentence.
        var rows = await context.AuditEntries
            // The audit log's TOTAL order (see IAuditLogReader). A handler writes its action and its
            // audit line off one clock read, so two entries can share an instant, and ordering on
            // the instant alone let the strip's last row change between two refreshes.
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Take(count)
            .SelectRows(context)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new ActivityEntry(
            row.Id,
            row.OccurredAt,
            row.ActorName,
            row.Action,
            row.EntityType,
            row.SubjectLabel,
            row.EntityId,
            row.Booking?.Value))];
    }
}
