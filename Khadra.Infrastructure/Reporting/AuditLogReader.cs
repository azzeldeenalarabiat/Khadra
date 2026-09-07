using Khadra.Application.Auditing.ReadModels;
using Khadra.Application.Common;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

internal sealed class AuditLogReader(KhadraDbContext context) : IAuditLogReader
{
    public async Task<PagedResult<AuditLogEntry>> ListAsync(
        AuditLogFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        var query = context.AuditEntries.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            var action = Enumeration.GetAll<AuditAction>().SingleOrDefault(candidate =>
                string.Equals(candidate.Name, filter.Action, StringComparison.OrdinalIgnoreCase));
            // An unrecognised action filters everything out rather than being ignored. Silently
            // returning the unfiltered log would look like the filter worked and matched everything,
            // which on this screen means an auditor believes they have seen all of something.
            if (action is null)
                return PagedResult.Empty<AuditLogEntry>(page.Page, page.PageSize);

            query = query.Where(entry => entry.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            var entityType = Enumeration.GetAll<AuditEntityType>().SingleOrDefault(candidate =>
                string.Equals(candidate.Name, filter.EntityType, StringComparison.OrdinalIgnoreCase));
            if (entityType is null)
                return PagedResult.Empty<AuditLogEntry>(page.Page, page.PageSize);

            query = query.Where(entry => entry.EntityType == entityType);
        }

        if (filter.ActorUserId is { } actorId)
            query = query.Where(entry => entry.ActorUserId == actorId);

        // "The System" is the absence of an actor, not an actor id, so it needs its own predicate.
        if (filter.SystemOnly == true)
            query = query.Where(entry => entry.ActorUserId == null);

        if (filter.EntityId is { } entityId)
            query = query.Where(entry => entry.EntityId == entityId);

        if (filter.OccurredFrom is { } from)
            query = query.Where(entry => entry.OccurredAt >= from);

        // Exclusive: the caller passes the start of the day AFTER the last one it wants.
        if (filter.OccurredBefore is { } before)
            query = query.Where(entry => entry.OccurredAt < before);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // The term is ESCAPED before it becomes a pattern. `%` and `_` are LIKE wildcards, so an
            // admin searching for a literal underscore was handed the entire log -- thirteen entries
            // reported as thirteen matches for their term, on the one screen where "this is all of
            // it" has to be true.
            //
            // Like + ToLower rather than ILike: ILike is Npgsql-only, which means the search branch
            // cannot be tested on the SQLite the persistence tests run on, and an untested branch on
            // this screen is the one nobody notices is wrong. This form translates on both, and is
            // just as trigram-indexable on Postgres if it ever needs to be.
            var term = filter.Search.Trim()
                .Replace(@"\", @"\\", StringComparison.Ordinal)
                .Replace("%", @"\%", StringComparison.Ordinal)
                .Replace("_", @"\_", StringComparison.Ordinal)
                .ToLowerInvariant();
            var pattern = $"%{term}%";

            // A sequential scan over two 200-character columns. Fine into the tens of thousands of
            // rows; if an unfiltered search ever exceeds ~100ms on production data, the answer is
            // pg_trgm with GIN indexes on lower(subject_label) and lower(actor_name) -- not before.
            // CA1304/CA1311 want a culture on ToLower. There is no culture here to give: this is an
            // expression tree, never executed by .NET — EF translates it to SQL LOWER(). Calling
            // ToLowerInvariant instead, which is what the analyzer would accept, is exactly the thing
            // that does NOT translate, and the query would fail at runtime.
#pragma warning disable CA1304, CA1311
            query = query.Where(entry =>
                EF.Functions.Like(entry.SubjectLabel.ToLower(), pattern, @"\") ||
                EF.Functions.Like(entry.ActorName.ToLower(), pattern, @"\"));
#pragma warning restore CA1304, CA1311
        }

        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return PagedResult.Empty<AuditLogEntry>(page.Page, page.PageSize);

        // Selected to an anonymous type, with the nullable Ids unwrapped afterwards. Calling
        // .Value.Value on a nullable value object behind a converter compiles and then throws at
        // runtime: EF cannot translate it. Everything expensive still happens in SQL.
        var rows = await query
            // Newest first, and TOTAL: see IAuditLogReader. Ties on occurred_at are broken by a
            // UUIDv7 id, which is unique and agrees with time order, so a page boundary cannot drop
            // an entry or repeat one.
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(entry => new
            {
                entry.Id,
                entry.OccurredAt,
                entry.ActorUserId,
                entry.ActorName,
                // Null for a background job. The client renders that as "System", not as a blank.
                ActorRole = entry.ActorRole == null ? null : entry.ActorRole.Name,
                Action = entry.Action.Name,
                EntityType = entry.EntityType.Name,
                entry.EntityId,
                entry.SubjectLabel,
                entry.PreviousValue,
                entry.NewValue,
                entry.Reason,
                entry.CorrelationId,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new AuditLogEntry(
                row.Id.Value,
                row.OccurredAt,
                row.ActorUserId?.Value,
                row.ActorName,
                row.ActorRole,
                row.Action,
                row.EntityType,
                row.EntityId?.Value,
                row.SubjectLabel,
                row.PreviousValue,
                row.NewValue,
                row.Reason,
                row.CorrelationId))
            .ToList();

        return new PagedResult<AuditLogEntry>(items, page.Page, page.PageSize, total);
    }
}
