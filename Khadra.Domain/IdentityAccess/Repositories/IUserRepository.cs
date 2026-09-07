using Khadra.Domain.Common;

namespace Khadra.Domain.IdentityAccess.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Id id, CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(EmailAddress email, CancellationToken cancellationToken = default);

    Task<bool> ExistsByEmailAsync(EmailAddress email, CancellationToken cancellationToken = default);

    Task<bool> ExistsByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many administrators could still sign in, ignoring one of them.
    /// </summary>
    /// <remarks>
    /// The guard against locking the platform out of its own console: deactivating the last active
    /// administrator leaves nobody who can undo it. The configured bootstrap is not a way back —
    /// see <see cref="AnyAdminExistsAsync"/>, which counts suspended and deleted administrators too,
    /// precisely so that emptying this set cannot reopen it.
    /// </remarks>
    Task<int> CountActiveAdminsExceptAsync(Id excludedUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this platform has ever had an administrator, in any state.
    /// </summary>
    /// <remarks>
    /// The question the configured bootstrap asks itself, and deliberately a broader one than
    /// <see cref="CountActiveAdminsExceptAsync"/>. That counts who can sign in TODAY; this counts
    /// whether the seat was ever filled, so a suspended or soft-deleted administrator still answers
    /// yes. Narrowing it to active accounts would turn the bootstrap into a standing back door: an
    /// administrator suspended by their colleagues would be replaced by whoever controls the
    /// configuration, on the next restart.
    /// </remarks>
    Task<bool> AnyAdminExistsAsync(CancellationToken cancellationToken = default);

    Task AddAsync(User user, CancellationToken cancellationToken = default);
}
