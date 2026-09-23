using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence.Repositories;

internal sealed class PushDeviceRepository(KhadraDbContext context) : IPushDeviceRepository
{
    public Task<PushDevice?> GetByTokenAsync(string token, CancellationToken cancellationToken = default) =>
        context.PushDevices.SingleOrDefaultAsync(device => device.Token == token, cancellationToken);

    public async Task AddAsync(PushDevice device, CancellationToken cancellationToken = default) =>
        await context.PushDevices.AddAsync(device, cancellationToken);

    public async Task<IReadOnlyList<PushDevice>> ListDeliverableAsync(
        Id userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // The session join is the guard, not a courtesy: see IPushDeviceRepository. A family is live
        // while ANY of its tokens is unrevoked and unexpired — rotation revokes the old row and adds
        // the new one, so exactly one is live in a healthy session.
        var liveSessions = context.RefreshTokens
            .Where(token => token.UserId == userId
                            && token.RevokedAt == null
                            && token.ExpiresAt > now
                            && token.FamilyExpiresAt > now);

        return await context.PushDevices
            .Where(device => device.UserId == userId && device.RevokedAt == null)
            .Where(device => device.SessionFamilyId == null
                ? liveSessions.Any()
                : liveSessions.Any(token => token.FamilyId == device.SessionFamilyId))
            .OrderBy(device => device.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<int> RevokeForSessionAsync(
        Id userId,
        Guid sessionFamilyId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        context.PushDevices
            .Where(device => device.UserId == userId
                             && device.SessionFamilyId == sessionFamilyId
                             && device.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(device => device.RevokedAt, now), cancellationToken);

    public Task<int> RevokeByTokenAsync(string token, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        context.PushDevices
            .Where(device => device.Token == token && device.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(device => device.RevokedAt, now), cancellationToken);
}
