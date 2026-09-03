using Khadra.Domain.Common;

namespace Khadra.Application.Dealers.ReadModels;

/// <summary>
/// One member of staff as the owner sees them (spec 4.2). Joined to the Identity context by user
/// id for the name, address and last sign-in; the Dealers context holds only the membership.
/// </summary>
public sealed record EmployeeListItem(
    Guid EmployeeId,
    Guid UserId,
    string FullName,
    string Email,
    string Phone,
    bool CanViewReports,
    bool IsActive,
    // Invited (never accepted), Active, or Deactivated. Derived here so the console never has to
    // combine three flags to say one word.
    string Status,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeactivatedAt);

public interface IEmployeeReader
{
    Task<IReadOnlyList<EmployeeListItem>> ListAsync(Id dealerId, CancellationToken cancellationToken = default);

    Task<EmployeeListItem?> GetAsync(Id dealerId, Id employeeId, CancellationToken cancellationToken = default);
}
