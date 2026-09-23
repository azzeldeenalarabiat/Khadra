using Khadra.Domain.Common;

namespace Khadra.Domain.IdentityAccess.Repositories;

public interface IPushDeviceRepository
{
    /// <summary>The install with this token, revoked or not — a re-registration revives it.</summary>
    Task<PushDevice?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);

    Task AddAsync(PushDevice device, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every device a push to <paramref name="userId"/> may go to right now: not revoked, AND tied to a
    /// session that is still live.
    /// </summary>
    /// <remarks>
    /// The session test is the real guard. Sign-out, sign-out-everywhere, revoking a device, a password
    /// change, a suspension and plain session expiry all end the session without necessarily touching
    /// this table; checking the session here means none of them can leave a signed-out phone being
    /// woken. A device with no session id (registered on a token that predates the claim) is
    /// deliverable only while the user has ANY live session.
    /// </remarks>
    Task<IReadOnlyList<PushDevice>> ListDeliverableAsync(
        Id userId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes the devices tied to one session. A single UPDATE; returns rows affected.</summary>
    Task<int> RevokeForSessionAsync(
        Id userId,
        Guid sessionFamilyId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes one install by its token, when the push service reports it dead.</summary>
    Task<int> RevokeByTokenAsync(string token, DateTimeOffset now, CancellationToken cancellationToken = default);
}
