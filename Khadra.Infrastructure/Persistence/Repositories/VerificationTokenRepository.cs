using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence.Repositories;

internal sealed class VerificationTokenRepository(KhadraDbContext context) : IVerificationTokenRepository
{
    public Task<VerificationToken?> GetByHashAsync(
        string tokenHash,
        VerificationPurpose purpose,
        CancellationToken cancellationToken = default) =>
        context.VerificationTokens.SingleOrDefaultAsync(
            token => token.TokenHash == tokenHash && token.Purpose == purpose,
            cancellationToken);

    public async Task AddAsync(VerificationToken token, CancellationToken cancellationToken = default) =>
        await context.VerificationTokens.AddAsync(token, cancellationToken);

    public Task<bool> HasActiveAsync(
        Id userId,
        VerificationPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        context.VerificationTokens.AnyAsync(
            token => token.UserId == userId
                && token.Purpose == purpose
                && token.ConsumedAt == null
                && token.ExpiresAt > now,
            cancellationToken);

    // Marks every unconsumed token of that purpose as consumed (expired ones included; harmless).
    public Task<int> InvalidateActiveAsync(
        Id userId,
        VerificationPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        context.VerificationTokens
            .Where(token => token.UserId == userId && token.Purpose == purpose && token.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.ConsumedAt, now), cancellationToken);
}
