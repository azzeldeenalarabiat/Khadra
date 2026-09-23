using System.Text.Json;

namespace Khadra.Tests.Configuration;

/// <summary>
/// Which requests the BFF forwards without a session, as its shipped configuration says.
/// </summary>
/// <remarks>
/// <para>
/// The BFF cannot be started in this suite — it connects to Redis before it serves anything — so the
/// route table is read where it physically lives. That is not a lesser test for this particular
/// question: the policy is a STRING in configuration (<c>"Anonymous"</c>), matched by name in
/// <c>Program.cs</c>, so a typo or a deleted line is exactly the failure this can see and a mocked
/// host could not.
/// </para>
/// <para>
/// It exists because of a real defect. The console reads <c>GET /api/v1/app-config</c> once, at
/// startup, to learn the currency's scale and the reporting zone. Everything under <c>/api</c> needed
/// a session, so a console opened at the sign-in page — which is every normal sign-in — got a 401,
/// swallowed it by design, and then ran the whole session on fallbacks: dinars printed without their
/// three decimals, and times in the browser's zone rather than the platform's. Signing in did not fix
/// it, because sign-in navigates within the application and the configuration is never asked for
/// again.
/// </para>
/// </remarks>
public sealed class BffProxyRouteTests
{
    /// <summary>
    /// Every route the BFF will forward to an anonymous caller.
    ///
    /// Written out rather than counted, so ADDING one is a deliberate edit here: each entry widens the
    /// surface a caller can reach without a session, and the browser holds no token to lose.
    /// </summary>
    private static readonly string[] AnonymousPaths =
    [
        "/api/v1/app-config",
        "/api/v1/auth/accept-invitation",
        "/api/v1/auth/forgot-password",
        "/api/v1/auth/register",
        "/api/v1/auth/register-dealer-owner",
        "/api/v1/auth/resend-verification",
        "/api/v1/auth/reset-password",
        "/api/v1/auth/verify-email",
    ];

    /// <summary>
    /// The customer website: public catalogue reads and the customer half of account recovery. No
    /// dealer registration and no staff invitation -- this host has no page for either -- and the page
    /// renderer, which never receives a session.
    /// </summary>
    private static readonly string[] CustomerWebAnonymousPaths =
    [
        "/api/v1/app-config",
        "/api/v1/auth/forgot-password",
        "/api/v1/auth/register",
        "/api/v1/auth/resend-verification",
        "/api/v1/auth/reset-password",
        "/api/v1/auth/verify-email",
        "/api/v1/car-types",
        "/api/v1/cities",
        "/api/v1/dealer-images/{**path}",
        "/api/v1/galleries",
        "/api/v1/galleries/{dealerId:guid}",
        "/api/v1/galleries/{dealerId:guid}/reviews",
        "/api/v1/vehicle-images/{**path}",
        "/api/v1/vehicles",
        "/api/v1/vehicles/facets",
        "/api/v1/vehicles/{vehicleId:guid}",
        "/api/v1/vehicles/{vehicleId:guid}/quote",
        "/{**catch-all}",
    ];

    private static readonly string[] Reads = ["GET", "HEAD"];
    private static readonly string[] PostOnly = ["POST"];

    private static JsonElement Routes(string deployment = "console") =>
        Deployment(deployment).GetProperty("ReverseProxy").GetProperty("Routes");

    internal static JsonElement Deployment(string deployment)
    {
        // Anchored on the file itself, the way ShippedConfigurationTests is.
        var path = Path.Combine("Khadra.Bff", "Deployments", deployment + ".json");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, path)))
            root = root.Parent;

        Assert.NotNull(root);

        return JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root.FullName, path)),
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }).RootElement.Clone();
    }

    private static bool IsAnonymous(JsonElement route) =>
        route.TryGetProperty("AuthorizationPolicy", out var policy) &&
        string.Equals(policy.GetString(), "Anonymous", StringComparison.Ordinal);

    private static string PathOf(JsonElement route) =>
        route.GetProperty("Match").GetProperty("Path").GetString()!;

    [Fact]
    public void The_console_can_read_the_platform_configuration_before_anyone_signs_in()
    {
        var route = Routes()
            .EnumerateObject()
            .Select(entry => entry.Value)
            .Single(candidate => PathOf(candidate) == "/api/v1/app-config");

        Assert.True(IsAnonymous(route), "the console asks for this before it has a session");
        // Reading the platform's own facts, and nothing else: no other verb reaches the API this way.
        Assert.Equal(
            ["GET"],
            route.GetProperty("Match").GetProperty("Methods").EnumerateArray().Select(m => m.GetString()));
    }

    [Fact]
    public void Everything_else_under_api_still_needs_a_session()
    {
        var catchAll = Routes()
            .EnumerateObject()
            .Select(entry => entry.Value)
            .Single(candidate => PathOf(candidate) == "/api/{**catch-all}");

        Assert.False(IsAnonymous(catchAll));
    }

    [Fact]
    public void The_anonymous_surface_is_exactly_these_routes()
    {
        var anonymous = Routes()
            .EnumerateObject()
            .Select(entry => entry.Value)
            .Where(IsAnonymous)
            .Select(PathOf)
            .OrderBy(path => path, StringComparer.Ordinal);

        Assert.Equal(AnonymousPaths, anonymous);
    }

    [Fact]
    public void The_customer_website_exposes_exactly_its_own_anonymous_surface()
    {
        var anonymous = Routes("customer-web")
            .EnumerateObject()
            .Select(entry => entry.Value)
            .Where(IsAnonymous)
            .Select(PathOf)
            .OrderBy(path => path, StringComparer.Ordinal);

        Assert.Equal(CustomerWebAnonymousPaths.OrderBy(path => path, StringComparer.Ordinal), anonymous);
    }

    [Fact]
    public void The_customer_website_only_reads_anonymously_apart_from_account_recovery()
    {
        foreach (var route in Routes("customer-web").EnumerateObject().Select(entry => entry.Value).Where(IsAnonymous))
        {
            var methods = route.GetProperty("Match").GetProperty("Methods").EnumerateArray().Select(m => m.GetString()!).ToArray();
            var path = PathOf(route);
            if (path.StartsWith("/api/v1/auth/", StringComparison.Ordinal))
                Assert.Equal(PostOnly, methods);
            else
                Assert.All(methods, method => Assert.Contains(method, Reads));
        }
    }

    [Fact]
    public void The_customer_website_keeps_the_rest_of_the_api_behind_a_session()
    {
        var catchAll = Routes("customer-web")
            .EnumerateObject()
            .Select(entry => entry.Value)
            .Single(candidate => PathOf(candidate) == "/api/{**catch-all}");

        Assert.False(IsAnonymous(catchAll));
    }

    [Fact]
    public void Pages_go_to_the_renderer_only_after_every_api_route_has_had_its_chance()
    {
        var pages = Routes("customer-web")
            .EnumerateObject()
            .Select(entry => entry.Value)
            .Single(candidate => PathOf(candidate) == "/{**catch-all}");

        Assert.Equal("web", pages.GetProperty("ClusterId").GetString());
        Assert.True(pages.GetProperty("Order").GetInt32() > 0, "a catch-all at the default order would compete with /api");
    }

    [Theory]
    [InlineData("console", "Admin,DealerOwner,DealerEmployee")]
    [InlineData("customer-web", "Customer")]
    public void Each_deployment_names_the_roles_it_serves(string deployment, string roles)
    {
        Assert.Equal(roles, Deployment(deployment).GetProperty("BffSecurity").GetProperty("AllowedRoles").GetString());
    }

    [Fact]
    public void The_two_deployments_never_share_a_cookie_name()
    {
        var console = Deployment("console").GetProperty("BffSecurity");
        var web = Deployment("customer-web").GetProperty("BffSecurity");

        // The console relies on the defaults, which the customer website must not reuse: cookies ignore
        // the port, so on one development host the two would overwrite each other.
        Assert.False(console.TryGetProperty("SessionCookieName", out _));
        Assert.NotEqual("__Host-Khadra.Session", web.GetProperty("SessionCookieName").GetString());
        Assert.NotEqual("__Host-Khadra.Antiforgery", web.GetProperty("AntiforgeryCookieName").GetString());
        Assert.NotEqual("XSRF-TOKEN", web.GetProperty("XsrfCookieName").GetString());
    }
}
