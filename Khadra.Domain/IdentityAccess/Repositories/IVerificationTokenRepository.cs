using Khadra.Domain.Common;

namespace Khadra.Domain.IdentityAccess.Repositories;

public interface IVerificationTokenRepository
{
    Task<VerificationToken?> GetByHashAsync(
        string tokenHash,
        VerificationPurpose purpose,
        CancellationToken cancellationToken = default);

    Task AddAsync(VerificationToken token, CancellationToken cancellationToken = default);

    /// <summary>Whether an unconsumed, unexpired link of this purpose is already out there.</summary>
    /// <remarks>
    /// Asked before re-sending one, so a restart does not invalidate a link the recipient is about
    /// to open.
    /// </remarks>
    Task<bool> HasActiveAsync(
        Id userId,
        VerificationPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    // Issuing a new link invalidates every older active link of the same purpose for that user.
    Task<int> InvalidateActiveAsync(
        Id userId,
        VerificationPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
