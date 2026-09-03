using Khadra.Application.Dealers.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

// Correlated lookups by user id across the context boundary, never a navigation: the Employee row
// is the Dealers context's membership fact, the User row is Identity's. Subqueries rather than a
// left join, because the same shape already translates cleanly in BookingReader and a join over a
// converted Id with a null-conditional projection does not.
internal sealed class EmployeeReader(KhadraDbContext context) : IEmployeeReader
{
    public async Task<IReadOnlyList<EmployeeListItem>> ListAsync(Id dealerId, CancellationToken cancellationToken = default)
    {
        // Active first is the database's; the name order is applied in memory, because ordering by a
        // member of the projected record does not translate and a dealer's staff list is small.
        var items = await Project(
                context.Set<Employee>()
                    .Where(employee => employee.DealerId == dealerId)
                    .OrderByDescending(employee => employee.IsActive))
            .ToListAsync(cancellationToken);

        return items
            .OrderByDescending(item => item.IsActive)
            .ThenBy(item => item.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<EmployeeListItem?> GetAsync(Id dealerId, Id employeeId, CancellationToken cancellationToken = default) =>
        Project(context.Set<Employee>().Where(employee => employee.DealerId == dealerId && employee.Id == employeeId))
            .SingleOrDefaultAsync(cancellationToken);

    private IQueryable<EmployeeListItem> Project(IQueryable<Employee> employees) =>
        employees.Select(employee => new EmployeeListItem(
            employee.Id.Value,
            employee.UserId.Value,
            context.Users.Where(user => user.Id == employee.UserId).Select(user => user.Name.Value).FirstOrDefault() ?? string.Empty,
            context.Users.Where(user => user.Id == employee.UserId).Select(user => user.Email.Value).FirstOrDefault() ?? string.Empty,
            context.Users.Where(user => user.Id == employee.UserId).Select(user => user.Phone.Value).FirstOrDefault() ?? string.Empty,
            employee.CanViewReports,
            employee.IsActive,
            !employee.IsActive
                ? "Deactivated"
                : context.Users.Any(user => user.Id == employee.UserId && !user.IsEmailVerified)
                    ? "Invited"
                    : "Active",
            context.Users.Where(user => user.Id == employee.UserId).Select(user => user.LastLoginAt).FirstOrDefault(),
            employee.CreatedAt,
            employee.DeactivatedAt));
}
