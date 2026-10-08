using Khadra.Bff.Security;
using Khadra.Tests.Support;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// Pre-launch item 222. The customer website's pages run two inline scripts Angular's event replay needs, and the
/// customer BFF's <c>script-src 'self'</c> refused both, so a tap made before hydration was lost. The BFF now mints a
/// nonce for each page it forwards to the renderer, names it in that response's <c>script-src</c> alone, and hands it
/// to the renderer in <c>X-Khadra-Csp-Nonce</c>. Never <c>'unsafe-inline'</c>, and nothing else in the policy moves.
/// </summary>
public sealed class BffContentSecurityPolicyNonceTests
{
    private static readonly string Policy = new BffSecuritySettings().ContentSecurityPolicy;

    [Fact]
    public void A_nonce_is_added_to_script_src_and_to_nothing_else()
    {
        var policy = ContentSecurityPolicyNonce.WithScriptNonce(Policy, "q3Vb8l0Vw6mZ1c2bQe9xkA==");

        Assert.Contains("script-src 'self' 'nonce-q3Vb8l0Vw6mZ1c2bQe9xkA=='", policy, StringComparison.Ordinal);
        // A nonce in style-src would switch off its 'unsafe-inline', which the website's styles rely on.
        Assert.Contains("style-src 'self' 'unsafe-inline';", policy, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(policy, "'nonce-"));
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", policy, StringComparison.Ordinal);

        // Every other directive reads exactly as configured.
        var configured = Policy.Split(';', StringSplitOptions.TrimEntries).Where(directive => !directive.StartsWith("script-src", StringComparison.Ordinal));
        var served = policy.Split(';', StringSplitOptions.TrimEntries).Where(directive => !directive.StartsWith("script-src", StringComparison.Ordinal));
        Assert.Equal(configured, served);
    }

    [Fact]
    public void Each_page_gets_its_own_unguessable_nonce()
    {
        var nonces = Enumerable.Range(0, 50).Select(_ => ContentSecurityPolicyNonce.NewNonce()).ToList();

        Assert.Equal(nonces.Count, nonces.Distinct(StringComparer.Ordinal).Count());
        foreach (var nonce in nonces)
            Assert.Equal(16, Convert.FromBase64String(nonce).Length);
    }

    /// <summary>
    /// Without its own script-src a policy would need a new directive, which replaces default-src for scripts and drops
    /// the bundles it allowed; a Proxy deployment refuses to start on one.
    /// </summary>
    [Theory]
    [InlineData("default-src 'self'; script-src 'self'", true)]
    [InlineData("default-src 'self'; SCRIPT-SRC 'self'", true)]
    [InlineData("default-src 'self'", false)]
    [InlineData("default-src 'self'; script-src-elem 'self'", false)]
    public void Only_a_policy_with_its_own_script_src_can_carry_the_nonce(string policy, bool carries)
    {
        Assert.Equal(carries, ContentSecurityPolicyNonce.HasScriptSource(policy));
        if (!carries)
            Assert.Throws<InvalidOperationException>(() => ContentSecurityPolicyNonce.WithScriptNonce(policy, "q3Vb8l0Vw6mZ1c2bQe9xkA=="));
    }

    [Fact]
    public void The_default_policy_carries_it()
    {
        Assert.True(ContentSecurityPolicyNonce.HasScriptSource(Policy));
    }

    /// <summary>The nonce is this BFF's to choose: a browser's copy is removed on every route before its own is set.</summary>
    [Fact]
    public void A_nonce_a_browser_sends_is_never_forwarded()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://khadra-web:4000/en/cars");
        request.Headers.Add(BffConstants.CspNonceHeaderName, "chosen-by-the-browser");

        ProxyRequestHeaders.RemoveBrowserSupplied(request.Headers);

        Assert.False(request.Headers.Contains(BffConstants.CspNonceHeaderName));
    }

    /// <summary>The renderer reads the header by its own spelling; the two must stay one name.</summary>
    [Fact]
    public void The_renderer_reads_the_header_the_bff_sends()
    {
        var server = File.ReadAllText(RepositoryRoot.File("Khadra.Web", "src", "server.ts"));

        Assert.Contains($"request.header('{BffConstants.CspNonceHeaderName.ToLowerInvariant()}')", server, StringComparison.Ordinal);
    }
}
