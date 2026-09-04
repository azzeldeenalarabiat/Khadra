using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(KhadraDbContext context) : IUserRepository
{
    // Documents are part of the aggregate: attaching one replaces the previous of that type, which
    // cannot be decided without them loaded.
    public Task<User?> GetByIdAsync(Id id, CancellationToken cancellationToken = default) =>
        context.Users
            .Include(user => user.Documents)
            .SingleOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(EmailAddress email, CancellationToken cancellationToken = default) =>
        context.Users.SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

    // Uniqueness must hold across soft-deleted rows too, hence IgnoreQueryFilters here.
    public Task<bool> ExistsByEmailAsync(EmailAddress email, CancellationToken cancellationToken = default) =>
        context.Users.IgnoreQueryFilters().AnyAsync(user => user.Email == email, cancellationToken);

    public Task<bool> ExistsByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken = default) =>
        context.Users.IgnoreQueryFilters().AnyAsync(user => user.Phone == phone, cancellationToken);

    // The soft-delete filter applies here on purpose: a deleted administrator cannot sign in, so
    // they are not one of the accounts standing between the platform and a lockout.
    public Task<int> CountActiveAdminsExceptAsync(Id excludedUserId, CancellationToken cancellationToken = default)
    {
        var admin = UserRole.Admin;
        var active = UserStatus.Active;
        return context.Users.CountAsync(
            user => user.Role == admin && user.Status == active && user.Id != excludedUserId,
            cancellationToken);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default) =>
        await context.Users.AddAsync(user, cancellationToken);
}
