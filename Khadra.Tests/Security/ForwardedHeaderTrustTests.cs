using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Khadra.Tests.Security;

/// <summary>
/// The rate limiter partitions on <c>Connection.RemoteIpAddress</c>, and <c>UseForwardedHeaders</c>
/// rewrites that from <c>X-Forwarded-For</c>. The header is therefore a security input: whoever may
/// set it picks the bucket their requests are counted in, and can leave every limit behind by
/// sending a fresh value each time — including the ten-per-fifteen-minutes cap that is the only
/// brute-force protection on password sign-in.
///
/// It was exactly that, and the cause was easy to miss: ForwardedHeadersMiddleware only checks the
/// sender when it has something to check against (<c>KnownNetworks.Count > 0 ||
/// KnownProxies.Count > 0</c>), so BOTH lists empty does not mean "trust nobody", it means "trust
/// anybody". Program.cs cleared both and then filled KnownProxies from configuration that was <c>[]</c>.
///
/// These tests set a real client address on the connection before the pipeline runs, because
/// WebApplicationFactory leaves <c>RemoteIpAddress</c> null and a null address gives the middleware
/// nothing to compare — which is not the situation any deployment is ever in.
/// </summary>
public sealed class ForwardedHeaderTrustTests
{
    private const string TheBff = "10.255.255.1";
    private const string SomeoneElse = "203.0.113.7";

    /// <summary>Puts a real address on the connection, ahead of everything else in the pipeline.</summary>
    private sealed class ClientAddressFilter(string address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(address);
                await following();
            });
            next(app);
        };
    }

    private static WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker> Api(
        string callerAddress,
        string environment = "Testing",
        params string[] knownProxies) =>
        new WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=khadra_tests;Username=x;Password=y");
                builder.UseSetting("Authentication:Jwt:SigningKey", new string('k', 48));
                builder.UseSetting("Database:AutoMigrate", "false");
                builder.UseSetting("Email:Provider", "Logging");
                for (var i = 0; i < knownProxies.Length; i++)
                    builder.UseSetting($"KnownProxies:{i}", knownProxies[i]);
                builder.ConfigureTestServices(services =>
                    services.AddSingleton<IStartupFilter>(new ClientAddressFilter(callerAddress)));
            });

    /// <summary>Twelve calls, each claiming to be a different client. Returns the statuses seen.</summary>
    private static async Task<List<HttpStatusCode>> RotateForwardedFor(HttpClient client, bool sendHeader)
    {
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 12; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/auth/verify-email", UriKind.Relative))
            {
                Content = JsonContent.Create(new { }),
            };
            if (sendHeader)
                request.Headers.TryAddWithoutValidation("X-Forwarded-For", $"198.51.100.{attempt}");
            using var response = await client.SendAsync(request);
            statuses.Add(response.StatusCode);
        }

        return statuses;
    }

    /// <summary>
    /// The regression. A caller the API does not trust rotates the header on every request; if it
    /// were honoured each request would land in its own partition and none would ever be refused.
    /// </summary>
    [Fact]
    public async Task Rotating_x_forwarded_for_from_an_untrusted_caller_cannot_escape_the_rate_limit()
    {
        using var factory = Api(callerAddress: SomeoneElse, knownProxies: TheBff);
        using var client = factory.CreateClient();

        var statuses = await RotateForwardedFor(client, sendHeader: true);

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    /// <summary>
    /// The other half: the BFF must still be able to say who its caller is, or every client behind
    /// it shares one bucket and ten failed sign-ins lock the platform out. Same twelve requests,
    /// same rotation, but arriving from the trusted address — none may be refused.
    /// </summary>
    [Fact]
    public async Task The_trusted_proxy_can_still_partition_its_clients()
    {
        using var factory = Api(callerAddress: TheBff, knownProxies: TheBff);
        using var client = factory.CreateClient();

        var statuses = await RotateForwardedFor(client, sendHeader: true);

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses);
    }

    /// <summary>
    /// Shows the assertions above are about the header rather than the endpoint refusing everything
    /// anyway: from the trusted proxy but with no header, all twelve share one bucket and the limit
    /// bites.
    /// </summary>
    [Fact]
    public async Task Without_the_header_everything_shares_one_bucket()
    {
        using var factory = Api(callerAddress: TheBff, knownProxies: TheBff);
        using var client = factory.CreateClient();

        var statuses = await RotateForwardedFor(client, sendHeader: false);

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    /// <summary>
    /// A deployment that has not named its proxy has no safe behaviour available — trust everyone,
    /// or count every client in one bucket — so it must not start.
    /// </summary>
    [Fact]
    public async Task An_environment_other_than_development_refuses_to_start_without_a_known_proxy()
    {
        using var factory = Api(callerAddress: SomeoneElse, environment: "Staging");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        });

        Assert.Contains("KnownProxies", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// One variable may carry the whole list.
    ///
    /// Behind a managed edge the correct list is roughly twenty-five ranges — Cloudflare publishes
    /// twenty-two — and twenty-five <c>KnownProxies__N</c> variables typed into a dashboard is a
    /// configuration nobody re-reads, where a single omission silently stops the walk one hop short
    /// and collapses every visitor behind that edge into one rate-limit bucket.
    ///
    /// Proven by consequence rather than by inspection: the entry below is not a parseable address,
    /// so without splitting it the API refuses to start, and if it were split but not trusted the
    /// rotation would be refused. Neither happens. The ragged spacing is deliberate.
    /// </summary>
    [Fact]
    public async Task One_known_proxies_entry_may_carry_a_comma_separated_list()
    {
        using var factory = Api(
            callerAddress: TheBff,
            knownProxies: $"192.0.2.0/24, {TheBff} ,198.51.100.4");
        using var client = factory.CreateClient();

        var statuses = await RotateForwardedFor(client, sendHeader: true);

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses);
    }

    /// <summary>Development still boots with nothing configured, so nobody is blocked locally.</summary>
    [Fact]
    public async Task Development_still_starts_without_a_known_proxy()
    {
        using var factory = Api(callerAddress: SomeoneElse, environment: "Development");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
