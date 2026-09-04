using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

internal sealed class AdminUserReader(KhadraDbContext context) : IAdminUserReader
{
    public async Task<IReadOnlyList<AdminUserListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var admin = UserRole.Admin;

        // Not paged: there are a handful of administrators and there always will be. A pager over
        // three rows is furniture.
        return await context.Users
            .Where(user => user.Role == admin)
            .OrderBy(user => user.CreatedAt)
            .ThenBy(user => user.Id)
            .Select(user => new AdminUserListItem(
                user.Id.Value,
                user.Name.Value,
                user.Email.Value,
                user.Phone.Value,
                user.Status.Name,
                user.IsEmailVerified,
                user.LastLoginAt,
                user.CreatedAt,
                user.SuspensionReason,
                // Correlated rather than joined: Auditing is another bounded context. It answers the
                // one question worth asking before deactivating somebody — whether the account has
                // ever done anything the platform is accountable for.
                context.AuditEntries.Count(entry => entry.ActorUserId == user.Id)))
            .ToListAsync(cancellationToken);
    }
}
