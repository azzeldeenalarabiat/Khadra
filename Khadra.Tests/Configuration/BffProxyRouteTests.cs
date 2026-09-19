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

    private static JsonElement Routes()
    {
        // Anchored on the file itself, the way ShippedConfigurationTests is.
        var path = Path.Combine("Khadra.Bff", "appsettings.json");
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
            }).RootElement.Clone().GetProperty("ReverseProxy").GetProperty("Routes");
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
}
