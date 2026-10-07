using Khadra.Application.Legal.ReadModels;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

internal sealed class LegalConsentReader(KhadraDbContext context) : ILegalConsentReader
{
    public async Task<IReadOnlyList<PendingLegalVersion>> PendingAsync(
        Id userId,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        var versions = context.LegalDocumentVersions.AsNoTracking();

        // ONE statement, because the consent gate asks it on every request it judges: the version of each kind in force
        // at `at` (none of its kind newer and already in force) that this person has no acceptance of. The unique index on
        // (kind, effective_from) answers the first half and the consents' (user_id, document_version_id) index the second.
        var rows = await versions
            .Where(version => version.EffectiveFrom <= at
                && !versions.Any(newer =>
                    newer.Kind == version.Kind && newer.EffectiveFrom <= at && newer.EffectiveFrom > version.EffectiveFrom)
                && !context.LegalConsents.Any(consent =>
                    consent.UserId == userId && consent.DocumentVersionId == version.Id))
            .Select(version => new { version.Kind, version.Id, version.VersionLabel, version.EffectiveFrom })
            .ToListAsync(cancellationToken);

        return [.. rows
            .OrderBy(row => row.Kind.Id)
            .Select(row => new PendingLegalVersion(row.Kind, row.Id, row.VersionLabel, row.EffectiveFrom))];
    }

    public async Task<IReadOnlyList<AcceptedLegalVersion>> AcceptedAsync(Id userId, CancellationToken cancellationToken = default)
    {
        var rows = await context.LegalConsents
            .AsNoTracking()
            .Where(consent => consent.UserId == userId)
            .Join(
                context.LegalDocumentVersions.AsNoTracking(),
                consent => consent.DocumentVersionId,
                version => version.Id,
                (consent, version) => new
                {
                    consent.Id,
                    version.Kind,
                    VersionId = version.Id,
                    version.VersionLabel,
                    consent.OccurredAt,
                    consent.Channel,
                    consent.Language,
                })
            .ToListAsync(cancellationToken);

        // Ordered here rather than in SQL: the record is one person's handful of rows, and a total order — newest first,
        // the id breaking a tie — must not depend on how a provider sorts an instant.
        return [.. rows
            .OrderByDescending(row => row.OccurredAt)
            .ThenByDescending(row => row.Id.Value)
            .Select(row => new AcceptedLegalVersion(
                row.Kind, row.VersionId, row.VersionLabel, row.OccurredAt, row.Channel, row.Language))];
    }

    public async Task<IReadOnlySet<Id>> AlreadyAcceptedAsync(
        Id userId,
        IReadOnlyCollection<Id> versionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(versionIds);
        if (versionIds.Count == 0)
            return new HashSet<Id>();

        var wanted = versionIds.Distinct().ToList();
        var accepted = await context.LegalConsents
            .AsNoTracking()
            .Where(consent => consent.UserId == userId && wanted.Contains(consent.DocumentVersionId))
            .Select(consent => consent.DocumentVersionId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return accepted.ToHashSet();
    }
}
