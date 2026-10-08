using System.Security.Cryptography;

namespace Khadra.Bff.Security;

/// <summary>
/// The two inline scripts the customer website must run, admitted by a nonce minted per request (pre-launch item 222).
/// </summary>
/// <remarks>
/// <para>
/// A page the renderer serves carries two inline scripts that Angular's event replay needs: the event-dispatch contract
/// the build inlines into <c>index.html</c>, and the <c>__jsaction_bootstrap(…)</c> call the server adds, whose list of
/// event types depends on the page. Under <c>script-src 'self'</c> both were refused, so a tap made before hydration
/// finished was lost. A hash cannot admit the second one — its text changes from page to page — and
/// <c>'unsafe-inline'</c> would admit everything. So this BFF mints a fresh nonce for every request it forwards to the
/// renderer, adds it to <c>script-src</c> on that response alone, and hands it to the renderer, which puts it on those
/// two scripts and nothing else (a file or a redirect from the renderer carries one it never uses).
/// </para>
/// <para>
/// Only <c>script-src</c> changes. A nonce in <c>style-src</c> would make browsers ignore its <c>'unsafe-inline'</c>,
/// which the website's styles rely on. Responses that do not come from the renderer — the API, the BFF's own
/// endpoints, the console — keep the configured policy exactly as it is.
/// </para>
/// </remarks>
internal static class ContentSecurityPolicyNonce
{
    /// <summary>Where the nonce minted for this request waits until the response's headers are written.</summary>
    public const string ItemKey = "Khadra.CspNonce";

    /// <summary>128 bits from the platform's CSPRNG, base64: the value goes into a header and an HTML attribute.</summary>
    public static string NewNonce() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    /// <summary>Whether the policy names its own <c>script-src</c>, which is the only directive a nonce is added to.</summary>
    public static bool HasScriptSource(string policy) => FindScriptSource(Directives(policy)) >= 0;

    /// <summary>
    /// The policy with <c>'nonce-…'</c> appended to its <c>script-src</c>, every other directive untouched. A policy
    /// without one is refused at startup in Proxy mode (<see cref="HasScriptSource"/>): adding the directive here would
    /// replace <c>default-src</c> for scripts and drop whatever it allowed.
    /// </summary>
    public static string WithScriptNonce(string policy, string nonce)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);

        var directives = Directives(policy);
        var index = FindScriptSource(directives);
        if (index < 0)
            throw new InvalidOperationException("The content security policy has no script-src directive to add a nonce to.");

        directives[index] = $"{directives[index]} 'nonce-{nonce}'";
        return string.Join("; ", directives);
    }

    private static List<string> Directives(string policy) =>
        [.. policy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static int FindScriptSource(List<string> directives) =>
        directives.FindIndex(directive =>
            directive.Equals("script-src", StringComparison.OrdinalIgnoreCase) ||
            directive.StartsWith("script-src ", StringComparison.OrdinalIgnoreCase));
}
