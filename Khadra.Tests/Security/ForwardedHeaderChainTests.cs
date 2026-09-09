using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Khadra.Tests.Security;

/// <summary>
/// What <c>ForwardedHeadersMiddleware</c> resolves for the chain production actually presents, and
/// why the trust list and the hop count have the values they do.
/// </summary>
/// <remarks>
/// Written from a captured production request rather than from reasoning about it. The admin console
/// answered 500 on <c>/bff/antiforgery</c> — the first call the sign-in page makes — because
/// antiforgery refuses to issue a <c>__Host-</c> cookie when <c>Request.IsHttps</c> is false, and it
/// was false because the headers that say otherwise were being discarded. The captured request:
///
/// <code>
///   Transport peer:   ::1
///   X-Forwarded-For:  176.29.3.177, 172.69.173.136, 10.24.207.134
///   X-Forwarded-Proto: https
///   Cf-Connecting-Ip: 176.29.3.177
/// </code>
///
/// which is visitor, Cloudflare edge, Render load balancer, and a platform router that connects to
/// the container over loopback. None of it was trusted, so all of it was ignored.
///
/// The two settings are tested together because neither is meaningful alone: <c>ForwardLimit</c> is
/// a CEILING, and the walk stops at the first address no configured range covers regardless of it.
/// Raising the limit without widening the trust list does nothing at all, which is the trap
/// <c>docs/deployment.md</c> used to send people into.
///
/// These run the middleware directly rather than through a host. <c>Khadra.Bff</c> connects to Redis
/// during startup and has no test host, and the behaviour under test belongs to the middleware and
/// the options, not to either application.
/// </remarks>
public sealed class ForwardedHeaderChainTests
{
    /// <summary>The visitor, the Cloudflare edge, and Render's load balancer, as captured.</summary>
    private const string ProductionChain = "176.29.3.177, 172.69.173.136, 10.24.207.134";
    private const string Visitor = "176.29.3.177";
    private const string RenderLoadBalancer = "10.24.207.134";

    /// <summary>Loopback, Render's private network, and the Cloudflare range holding the observed edge.</summary>
    private static readonly string[] FullTrust = ["::1", "127.0.0.0/8", "10.0.0.0/8", "172.64.0.0/13"];

    /// <summary>Only the hop that connects to the container, and nothing in front of it.</summary>
    private static readonly string[] LoopbackOnly = ["::1", "127.0.0.0/8"];

    private static async Task<(string Client, bool IsHttps)> Resolve(
        string[] trusted,
        int forwardLimit,
        string forwardedFor,
        IPAddress peer,
        string forwardedProto = "https")
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = forwardLimit,
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var entry in trusted)
        {
            if (entry.Contains('/', StringComparison.Ordinal))
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(entry));
            else
                options.KnownProxies.Add(IPAddress.Parse(entry));
        }

        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = peer;
        context.Request.Scheme = "http";
        if (forwardedFor.Length > 0) context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        if (forwardedProto.Length > 0) context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;

        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));
        await middleware.Invoke(context);

        return (context.Connection.RemoteIpAddress?.ToString() ?? "(none)", context.Request.IsHttps);
    }

    /// <summary>
    /// The fix, against the exact request that was failing: the visitor is resolved, and the request
    /// is seen as HTTPS so the <c>__Host-</c> antiforgery cookie can be issued at all.
    /// </summary>
    [Fact]
    public async Task The_captured_production_chain_resolves_the_visitor_over_https()
    {
        var (client, isHttps) = await Resolve(FullTrust, 3, ProductionChain, IPAddress.IPv6Loopback);

        Assert.Equal(Visitor, client);
        Assert.True(isHttps);
    }

    /// <summary>
    /// Trusting the loopback hop alone is enough to fix the 500 — the scheme survives — but it
    /// resolves Render's load balancer as the client, which is the SAME address for every visitor
    /// on earth.
    ///
    /// That is not a spoofing hole; it is worse than the "everyone shares one bucket" it looks like.
    /// <c>CredentialSubject.PartitionKey</c> keys a sign-in on <c>{address}|{account}</c>, so with
    /// the address constant the cap becomes a per-account bucket shared between attacker and
    /// victim: ten requests a quarter hour aimed at a named administrator holds that account shut,
    /// and the victim cannot move out of the way. This test exists so nobody "simplifies" the trust
    /// list back down to loopback having seen the sign-in page start working.
    /// </summary>
    [Fact]
    public async Task Trusting_only_loopback_fixes_the_scheme_but_resolves_the_platform_not_the_visitor()
    {
        var (client, isHttps) = await Resolve(LoopbackOnly, 3, ProductionChain, IPAddress.IPv6Loopback);

        Assert.True(isHttps);
        Assert.Equal(RenderLoadBalancer, client);
        Assert.NotEqual(Visitor, client);
    }

    /// <summary>
    /// Why raising the hop count on its own is not a fix. The middleware re-checks trust at each hop
    /// against the address it just consumed, so with only loopback trusted the walk stops after one
    /// hop whatever the ceiling says — 1, 2 and 3 are the same configuration.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Raising_the_hop_count_without_widening_the_trust_list_changes_nothing(int forwardLimit)
    {
        var (client, _) = await Resolve(LoopbackOnly, forwardLimit, ProductionChain, IPAddress.IPv6Loopback);

        Assert.Equal(RenderLoadBalancer, client);
    }

    /// <summary>
    /// Cloudflare APPENDS the address it accepted the connection from to whatever the caller sent,
    /// rather than replacing it, so anything a caller invents lands to the LEFT of their true
    /// address. Walking from the right therefore reaches the truth first and stops there, because
    /// an ordinary visitor's address is in no trusted range.
    /// </summary>
    [Fact]
    public async Task A_caller_who_prepends_their_own_entry_is_still_resolved_correctly()
    {
        var (client, _) = await Resolve(
            FullTrust, 3, "1.2.3.4, " + ProductionChain, IPAddress.IPv6Loopback);

        Assert.Equal(Visitor, client);
    }

    /// <summary>
    /// The narrow case that decides the hop count exactly, rather than "3 or more".
    ///
    /// When the visitor's OWN address falls inside a trusted range — a Cloudflare Worker fetching
    /// this origin — the walk does not stop at them, and one hop too many consumes a value they
    /// supplied. At three the Worker is resolved; at four the caller picks their own rate-limit
    /// partition. The limit is part of the guarantee, not a margin to be padded.
    /// </summary>
    [Theory]
    [InlineData(3, "172.64.5.5")]
    [InlineData(4, "1.2.3.4")]
    public async Task When_the_visitor_is_itself_in_a_trusted_range_an_extra_hop_believes_the_caller(
        int forwardLimit, string expectedClient)
    {
        var (client, _) = await Resolve(
            FullTrust, forwardLimit, "1.2.3.4, 172.64.5.5, 172.69.173.136, 10.24.207.134",
            IPAddress.IPv6Loopback);

        Assert.Equal(expectedClient, client);
    }

    /// <summary>
    /// The guarantee is verified from the transport peer outward, so nothing an untrusted caller
    /// sends is believed — not the address, and not the scheme either.
    /// </summary>
    [Fact]
    public async Task Nothing_an_untrusted_caller_sends_is_believed()
    {
        var attacker = IPAddress.Parse("203.0.113.99");

        var (client, isHttps) = await Resolve(FullTrust, 3, ProductionChain, attacker);

        Assert.Equal(attacker.ToString(), client);
        Assert.False(isHttps);
    }

    /// <summary>
    /// The platform's health probe: loopback, no forwarding headers at all. It must resolve to
    /// itself and claim nothing — this is the request that consumed an earlier one-shot diagnostic
    /// and very nearly argued for trusting a health check.
    /// </summary>
    [Fact]
    public async Task A_health_probe_carrying_no_headers_resolves_to_itself()
    {
        var (client, isHttps) = await Resolve(
            FullTrust, 3, forwardedFor: "", IPAddress.IPv6Loopback, forwardedProto: "");

        Assert.Equal("::1", client);
        Assert.False(isHttps);
    }

    /// <summary>
    /// The other way into the API: the BFF proxying over the private network, where the peer is the
    /// BFF's own address and it has written a single entry naming the real client. One configuration
    /// covers both shapes, because the walk stops at the first untrusted address either way.
    /// </summary>
    [Fact]
    public async Task The_private_hop_from_the_bff_resolves_the_client_the_bff_named()
    {
        var (client, isHttps) = await Resolve(
            FullTrust, 3, Visitor, IPAddress.Parse("10.201.4.9"));

        Assert.Equal(Visitor, client);
        Assert.True(isHttps);
    }
}
