namespace Khadra.Application.Common;

// Facts about the calling client captured by the API layer, so handlers never touch HttpContext.
//
// DeclaredVersion: the version a customer app build DECLARED on this request (Wave 4, W4-8; Wave 7), and null for
// anything else -- a browser through either BFF (both drop the version header), a legacy build that only names itself
// in its User-Agent, a version that does not parse.
public sealed record ClientInfo(string? IpAddress, string? UserAgent, AppVersion? DeclaredVersion = null)
{
    public static readonly ClientInfo Unknown = new(null, null);

    /// <summary>The request came from a customer app build that declared its version.</summary>
    public bool IsCustomerApp => DeclaredVersion is not null;

    /// <summary>
    /// Whether this is a customer app build OLDER than <paramref name="rule"/>: the one caller a rule that build cannot
    /// answer still spares, until <c>MobileApp:MinimumSupportedVersion</c> reaches the rule and refuses the build first.
    /// </summary>
    /// <remarks>
    /// A temporary backward-compatibility bridge, never a security boundary (owner, Wave 7): the version is whatever the
    /// caller declares. Everyone else -- the website, the console, an app at or past the rule, a caller that declares
    /// nothing -- is held to the rule. The rules and their removal: <see cref="MobileAppContract"/>.
    /// </remarks>
    public bool PredatesRule(AppVersion rule) => DeclaredVersion is { } declared && declared < rule;
}
