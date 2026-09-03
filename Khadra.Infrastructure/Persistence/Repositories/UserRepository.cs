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

    public async Task AddAsync(User user, CancellationToken cancellationToken = default) =>
        await context.Users.AddAsync(user, cancellationToken);
}
