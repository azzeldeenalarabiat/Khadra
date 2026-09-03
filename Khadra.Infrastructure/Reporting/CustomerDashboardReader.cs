using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

internal sealed class CustomerDashboardReader(KhadraDbContext context) : ICustomerDashboardReader
{
    public async Task<CustomerCounts> CountsAsync(CancellationToken cancellationToken = default)
    {
        var customer = UserRole.Customer;
        var suspended = UserStatus.Suspended;

        // The soft-delete filter on users applies, so deleted accounts are already out.
        var counts = await context.Users
            .Where(user => user.Role == customer)
            .GroupBy(_ => 1)
            .Select(group => new CustomerCounts(
                group.Count(),
                // Verified means verified AND active: email verification and account status are
                // independent flags, so counting them separately would report a suspended-but-verified
                // customer in two buckets and the three would not add up to the total.
                group.Count(user => user.IsEmailVerified && user.Status != suspended),
                group.Count(user => !user.IsEmailVerified && user.Status != suspended),
                group.Count(user => user.Status == suspended)))
            .SingleOrDefaultAsync(cancellationToken);

        return counts ?? new CustomerCounts(0, 0, 0, 0);
    }
}
