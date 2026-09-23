using System.ComponentModel.DataAnnotations;

namespace Khadra.Bff.Security;

internal sealed class BffSecuritySettings
{
    public const string SectionName = "BffSecurity";

    /// <summary>
    /// Which browser application this BFF fronts: <c>console</c> (the staff dashboard, the default and
    /// the only deployment that existed before the customer website) or <c>customer-web</c>. It selects
    /// <c>Deployments/{name}.json</c>, which carries that deployment's route table, roles, cookies and
    /// frontend. Read from configuration BEFORE the rest of it is bound — see Program.cs.
    /// </summary>
    [Required, RegularExpression("^[a-z][a-z0-9-]{1,31}$")]
    public string Deployment { get; init; } = BffRealm.ConsoleDeployment;

    [Required, Url]
    public string ApiBaseUrl { get; init; } = null!;

    [Required]
    public string RedisConnection { get; init; } = "localhost:6379";

    /// <summary>
    /// The roles that may hold a session here, comma-separated. A string, not an array, on purpose:
    /// configuration merges arrays BY INDEX, so a deployment overriding entry 0 of a three-entry
    /// default would silently keep entries 1 and 2.
    /// </summary>
    [Required, MinLength(1)]
    public string AllowedRoles { get; init; } = null!;

    /// <summary>
    /// Hard ceiling on a session, counted from sign-in and never moved. Staff do desk work and 8h covers
    /// a shift; a customer keeps a session for days, bounded by the API's own refresh-family ceiling.
    /// </summary>
    [Range(1, 24 * 30)]
    public int SessionAbsoluteHours { get; init; } = 8;

    /// <summary>Redis sliding expiry: the session ends after this much inactivity.</summary>
    [Range(5, 60 * 24 * 30)]
    public int SessionIdleMinutes { get; init; } = 30;

    /// <summary>The session cookie. Configurable only so two deployments on one development host
    /// (cookies ignore the port) cannot overwrite each other's session.</summary>
    [Required, RegularExpression("^__Host-[A-Za-z0-9.]+$")]
    public string SessionCookieName { get; init; } = "__Host-Khadra.Session";

    [Required, RegularExpression("^__Host-[A-Za-z0-9.]+$")]
    public string AntiforgeryCookieName { get; init; } = "__Host-Khadra.Antiforgery";

    [Required, RegularExpression("^[A-Za-z0-9-]+$")]
    public string XsrfCookieName { get; init; } = "XSRF-TOKEN";

    /// <summary>
    /// <c>Static</c>: serve the built SPA from wwwroot and fall back to index.html (the console).
    /// <c>Proxy</c>: every path no other endpoint claims goes to the <c>web</c> cluster, the server-side
    /// renderer of the customer website.
    /// </summary>
    [Required, RegularExpression("^(Static|Proxy)$")]
    public string Frontend { get; init; } = "Static";

    /// <summary>
    /// Sent to the renderer on every forwarded request as <c>X-Khadra-Edge</c>, so the renderer believes
    /// the client address this BFF names only when the request really came through this BFF. Required in
    /// Proxy mode outside Development; a secret, so it lives in the environment, never in a tracked file.
    /// </summary>
    public string? FrontendSharedSecret { get; init; }

    [Required]
    public string ContentSecurityPolicy { get; init; } =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; " +
        "font-src 'self' data:; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    public IReadOnlySet<string> AllowedRoleSet() =>
        AllowedRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
}
