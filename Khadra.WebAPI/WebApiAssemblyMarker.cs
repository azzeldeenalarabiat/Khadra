namespace Khadra.WebAPI;

public sealed class WebApiAssemblyMarker;

public static class SecurityPolicies
{
    public const string Admin = "Admin";
    public const string DealerStaff = "DealerStaff";
    public const string DealerOwner = "DealerOwner";
    public const string Customer = "Customer";
    public const string VerifiedEmail = "VerifiedEmail";

    // Spec 3.1: a dealer owner whose application is still PENDING_REVIEW (or was rejected, or is
    // suspended) may sign in and watch their application, but may not operate the business.
    public const string ApprovedDealer = "ApprovedDealer";

    // The same trading gate, open to employees as well as the owner. Spec 4.2 gives employees exactly
    // one job -- acting on booking requests -- and that job lives behind this policy. Fleet, delivery,
    // profile and staff management stay on ApprovedDealer, which is owner-only.
    public const string ApprovedDealerStaff = "ApprovedDealerStaff";
}

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Login = "login";
    public const string Refresh = "refresh";
}
