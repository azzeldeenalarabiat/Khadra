using Khadra.Application.Common;
using Khadra.Application.Legal.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Legal;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Khadra.Infrastructure.Reporting;

internal sealed partial class LegalDocumentReader(KhadraDbContext context, ILogger<LegalDocumentReader> logger) : ILegalDocumentReader
{
    public async Task<PagedResult<LegalVersionRow>> ListAsync(
        LegalDocumentKind? kind,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        var query = context.LegalDocumentVersions.AsNoTracking();
        if (kind is not null)
            query = query.Where(version => version.Kind == kind);

        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return PagedResult.Empty<LegalVersionRow>(page.Page, page.PageSize);

        // A total order, so a page boundary never drops a version.
        var items = await query
            .OrderByDescending(version => version.EffectiveFrom)
            .ThenByDescending(version => version.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(version => new LegalVersionRow(
                version.Id,
                version.Kind,
                version.VersionLabel,
                version.EffectiveFrom,
                version.PublishedAt,
                version.PublishedByAdminId,
                context.Users
                    .Where(user => user.Id == version.PublishedByAdminId)
                    .Select(user => user.Name.Value)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
        return new PagedResult<LegalVersionRow>(items, page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<CurrentLegalVersion>> CurrentAsync(DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        // One small query per kind, in the kind's order: there are two kinds, and each is answered by the unique index.
        var current = new List<CurrentLegalVersion>();
        foreach (var kind in Enumeration.GetAll<LegalDocumentKind>())
        {
            var version = await context.LegalDocumentVersions
                .AsNoTracking()
                .Where(row => row.Kind == kind && row.EffectiveFrom <= at)
                .OrderByDescending(row => row.EffectiveFrom)
                .Select(row => new { row.Id, row.VersionLabel, row.EffectiveFrom })
                .FirstOrDefaultAsync(cancellationToken);
            if (version is not null)
                current.Add(new CurrentLegalVersion(kind, version.Id, version.VersionLabel, version.EffectiveFrom));
        }

        return current;
    }

    public async Task<IReadOnlyList<CurrentLegalVersion>?> CurrentIfReadableAsync(
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await CurrentAsync(at, cancellationToken);
        }
        catch (Exception unreadable) when (DatabaseUnreadable.IsCauseOf(unreadable))
        {
            LogUnreadable(logger, DatabaseUnreadable.Reason(unreadable));
            return null;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The legal texts in force could not be read, so /app-config says it does not know them: {Reason}")]
    private static partial void LogUnreadable(ILogger logger, string reason);

    public Task<string?> AdminNameAsync(Id adminId, CancellationToken cancellationToken = default) =>
        context.Users
            .Where(user => user.Id == adminId)
            .Select(user => user.Name.Value)
            .FirstOrDefaultAsync(cancellationToken);
}
