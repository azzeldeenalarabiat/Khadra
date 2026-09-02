namespace Khadra.WebAPI;

public sealed class WebApiAssemblyMarker;

public static class SecurityPolicies
{
    public const string Admin = "Admin";
    public const string DealerStaff = "DealerStaff";
    public const string DealerOwner = "DealerOwner";
    public const string Customer = "Customer";
    public const string VerifiedEmail = "VerifiedEmail";
}

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Login = "login";
    public const string Refresh = "refresh";
}
