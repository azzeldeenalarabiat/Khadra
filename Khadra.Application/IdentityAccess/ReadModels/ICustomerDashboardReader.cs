namespace Khadra.Application.IdentityAccess.ReadModels;

/// <summary>
/// Customer headcounts.
///
/// The three buckets partition the total, which the design's labels do not: email verification and
/// account status are independent flags, so counting "verified" and "suspended" separately would
/// report a suspended-but-verified customer twice. Verified therefore means verified AND active.
/// </summary>
public sealed record CustomerCounts(int Total, int Verified, int PendingVerification, int Suspended);

public interface ICustomerDashboardReader
{
    Task<CustomerCounts> CountsAsync(CancellationToken cancellationToken = default);
}
