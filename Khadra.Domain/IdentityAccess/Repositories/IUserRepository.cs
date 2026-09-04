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
    /// administrator leaves nobody who can undo it, and there is no back door that creates one.
    /// </remarks>
    Task<int> CountActiveAdminsExceptAsync(Id excludedUserId, CancellationToken cancellationToken = default);

    Task AddAsync(User user, CancellationToken cancellationToken = default);
}
