using Khadra.Domain.Common;

namespace Khadra.Domain.IdentityAccess.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>The replacement a rotated token points at, so a retry can be told from a replay.</summary>
    Task<RefreshToken?> GetByIdAsync(Id id, CancellationToken cancellationToken = default);

    Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default);

    // Bulk revocations run as a single UPDATE; they do not load aggregates. Returns rows affected.
    Task<int> RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<int> RevokeAllForUserAsync(Id userId, DateTimeOffset now, CancellationToken cancellationToken = default);
}
