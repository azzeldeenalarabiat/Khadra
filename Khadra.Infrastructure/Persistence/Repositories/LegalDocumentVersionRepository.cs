using Khadra.Domain.Common;
using Khadra.Domain.Legal;
using Khadra.Domain.Legal.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence.Repositories;

internal sealed class LegalDocumentVersionRepository(KhadraDbContext context) : ILegalDocumentVersionRepository
{
    public void Add(LegalDocumentVersion version) => context.LegalDocumentVersions.Add(version);

    public Task<LegalDocumentVersion?> GetByIdAsync(Id id, CancellationToken cancellationToken = default) =>
        context.LegalDocumentVersions.FirstOrDefaultAsync(version => version.Id == id, cancellationToken);

    public Task<DateTimeOffset?> LatestEffectiveFromAsync(LegalDocumentKind kind, CancellationToken cancellationToken = default) =>
        context.LegalDocumentVersions
            .Where(version => version.Kind == kind)
            .OrderByDescending(version => version.EffectiveFrom)
            .Select(version => (DateTimeOffset?)version.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> LabelTakenAsync(LegalDocumentKind kind, string versionLabel, CancellationToken cancellationToken = default) =>
        context.LegalDocumentVersions.AnyAsync(
            version => version.Kind == kind && version.VersionLabel == versionLabel,
            cancellationToken);

    public async Task<Id?> CurrentIdAsync(LegalDocumentKind kind, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        var ids = await context.LegalDocumentVersions
            .Where(version => version.Kind == kind && version.EffectiveFrom <= at)
            .OrderByDescending(version => version.EffectiveFrom)
            .Select(version => version.Id)
            .Take(1)
            .ToListAsync(cancellationToken);
        return ids.Count == 0 ? null : ids[0];
    }
}
