using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

internal sealed class SessionReader(KhadraDbContext context, IClock clock) : ISessionReader
{
    public async Task<IReadOnlyList<SessionSummary>> ListForUserAsync(
        Id userId,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        // Grouped by family, because a family IS the session: one row per sign-in, not one per
        // refresh. The device and address come from the newest token in the family, which is where
        // it was last used from; the first token says where it started.
        var rows = await context.RefreshTokens
            .Where(token => token.UserId == userId)
            .GroupBy(token => token.FamilyId)
            .Select(family => new
            {
                FamilyId = family.Key,
                SignedInAt = family.Min(token => token.CreatedAt),
                LastUsedAt = family.Max(token => token.CreatedAt),
                ExpiresAt = family.Max(token => token.FamilyExpiresAt),
                IsActive = family.Any(token =>
                    token.RevokedAt == null && token.ExpiresAt > now && token.FamilyExpiresAt > now),
            })
            .ToListAsync(cancellationToken);

        // The newest token per family carries the device and address it was last refreshed from.
        var families = rows.Select(row => row.FamilyId).ToList();
        var latest = await context.RefreshTokens
            .Where(token => families.Contains(token.FamilyId))
            .GroupBy(token => token.FamilyId)
            .Select(family => family
                .OrderByDescending(token => token.CreatedAt)
                .Select(token => new { token.FamilyId, token.CreatedByIp, token.UserAgent })
                .First())
            .ToListAsync(cancellationToken);

        var byFamily = latest.ToDictionary(row => row.FamilyId);

        return rows
            // Live sessions first, then most recently used. Both keys can tie, so the family id
            // settles it and the list cannot reorder itself between reads.
            .OrderByDescending(row => row.IsActive)
            .ThenByDescending(row => row.LastUsedAt)
            .ThenBy(row => row.FamilyId)
            .Select(row => new SessionSummary(
                row.FamilyId,
                row.SignedInAt,
                row.LastUsedAt,
                row.ExpiresAt,
                byFamily.TryGetValue(row.FamilyId, out var device) ? device.CreatedByIp : null,
                byFamily.TryGetValue(row.FamilyId, out var agent) ? agent.UserAgent : null,
                row.IsActive))
            .ToList();
    }

    public Task<bool> BelongsToUserAsync(Id userId, Guid familyId, CancellationToken cancellationToken = default) =>
        context.RefreshTokens.AnyAsync(
            token => token.UserId == userId && token.FamilyId == familyId,
            cancellationToken);
}
