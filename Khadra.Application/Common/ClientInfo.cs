namespace Khadra.Application.Common;

// Facts about the calling client captured by the API layer, so handlers never touch HttpContext.
//
// IsCustomerApp: the request came from a customer app build that DECLARED its version (Wave 4, W4-8; the advisor's
// review) -- the one caller the legal-consent rules spare until a build asks for consent itself (1.4.0). A browser
// through either BFF never is: both drop the version header.
public sealed record ClientInfo(string? IpAddress, string? UserAgent, bool IsCustomerApp = false)
{
    public static readonly ClientInfo Unknown = new(null, null);
}
