using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Khadra.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Khadra.Tests.Security;

/// <summary>
/// The customer-app version gate, through the real pipeline.
/// </summary>
/// <remarks>
/// <para>
/// **What it protects.** A release that changes a contract an installed build cannot read — Batch B
/// turned the car description and the gallery sections into <c>{ text, language }</c>, and a 1.0.0
/// phone casts them to strings and throws. That build has no version check of its own, so the API is
/// the only thing that can stop it, and it must stop it BEFORE the endpoint answers.
/// </para>
/// <para>
/// Most of these requests go to <c>/api/v1/auth/me</c>, which needs no database and answers 401 to
/// anybody without a token. That makes it a clean probe: 426 means the gate refused, 401 means the
/// gate let the request through to authentication — which is also the proof the gate runs first.
/// </para>
/// </remarks>
public sealed class MobileAppVersionGateTests : IDisposable
{
    private const string Minimum = "1.1.0";
    private const string UpdateUrl = "https://example.org/khadra.apk";
    private const string AllowedOrigin = "https://app.example.org";

    private static readonly Uri Probe = new("/api/v1/auth/me", UriKind.Relative);

    /// <summary>How a 1.0.0 build introduces itself: platform and OS version, no app version.</summary>
    private const string LegacyUserAgent = "Khadra (Android 16)";

    /// <summary>What the console's requests carry once the BFF has forwarded them.</summary>
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36";

    private readonly WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker> _factory = Api(Minimum);

    public void Dispose() => _factory.Dispose();

    private static WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker> Api(string minimum, string? updateUrl = UpdateUrl) =>
        new WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.IsolateFromDeveloperDatabase();
                builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=khadra_tests;Username=x;Password=y");
                builder.UseSetting("Authentication:Jwt:SigningKey", new string('k', 48));
                builder.UseSetting("Database:AutoMigrate", "false");
                builder.UseSetting("Email:Provider", "Logging");
                builder.UseSetting("KnownProxies:0", "10.255.255.1");
                // Appended rather than UseSetting: Program.cs re-adds the developer's untracked
                // appsettings.Local.json after the host's settings, and an appended source is the one
                // thing that still outranks it. The test states the minimum instead of negotiating.
                builder.ConfigureAppConfiguration(configuration =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["MobileApp:MinimumSupportedVersion"] = minimum,
                        ["MobileApp:UpdateUrl"] = updateUrl,
                        ["App:AllowedOrigins:0"] = AllowedOrigin,
                    }));
            });

    private async Task<HttpResponseMessage> SendAsync(
        Uri path,
        string? version = null,
        string? userAgent = null,
        string? acceptLanguage = null,
        string? origin = null,
        string? bearer = null,
        WebApplicationFactory<Khadra.WebAPI.WebApiAssemblyMarker>? factory = null)
    {
        using var client = (factory ?? _factory).CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (version is not null) request.Headers.TryAddWithoutValidation("X-Khadra-App-Version", version);
        if (userAgent is not null) request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        if (acceptLanguage is not null) request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        if (origin is not null) request.Headers.TryAddWithoutValidation("Origin", origin);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ProblemOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // ── Who is refused ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_build_that_predates_the_version_header_is_refused_with_426()
    {
        // The installed 1.0.0 APK: its User-Agent, and nothing else. It has no gate of its own, so
        // this refusal is the only thing standing between it and a contract it crashes on.
        using var response = await SendAsync(Probe, userAgent: LegacyUserAgent);

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore, "A refusal about the caller's build must never be cached.");

        var problem = await ProblemOf(response);
        Assert.Equal(426, problem.GetProperty("status").GetInt32());
        Assert.Equal("https://httpstatuses.com/426", problem.GetProperty("type").GetString());
        Assert.Equal("/api/v1/auth/me", problem.GetProperty("instance").GetString());
        Assert.Equal("app.update_required", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal(Minimum, problem.GetProperty("minimumSupportedVersion").GetString());
        Assert.Equal(UpdateUrl, problem.GetProperty("updateUrl").GetString());

        // That build prints the title verbatim and sends no language, so the title carries both:
        // Arabic first, then English.
        var title = problem.GetProperty("title").GetString()!;
        Assert.Equal("حدّث تطبيق خضرا للمتابعة · Update the Khadra app to continue", title);
        Assert.Contains("لم يعد مدعومًا", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Contains("no longer supported", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.0.0+1")]
    [InlineData("1.0.9")]
    [InlineData("0.9.0")]
    // A release candidate of the minimum is still BELOW it: Semantic Versioning, not a guess.
    [InlineData("1.1.0-rc.1")]
    public async Task A_declared_version_below_the_minimum_is_refused(string version)
    {
        using var response = await SendAsync(Probe, version: version, userAgent: LegacyUserAgent);

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1.1")]
    [InlineData("v1.1.0")]
    [InlineData("1.1.0.0")]
    public async Task A_version_nothing_can_read_is_refused(string version)
    {
        // A build that declares a version nobody can read is a build nobody can vouch for. Letting it
        // through would put a contract it may not read in front of it.
        using var response = await SendAsync(Probe, version: version, userAgent: LegacyUserAgent);

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task The_header_alone_identifies_the_app_whatever_the_user_agent_says()
    {
        // A web build cannot set a User-Agent, but it does send the header: an old one is refused
        // like any other old build.
        using var response = await SendAsync(Probe, version: "1.0.0", userAgent: BrowserUserAgent);

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    // ── Who is let through ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.1.0")]
    [InlineData("1.1.0+2")]
    [InlineData("1.1.1")]
    // Numeric, not textual: as a string "1.10.0" sorts below "1.9.0".
    [InlineData("1.10.0")]
    [InlineData("2.0.0")]
    public async Task The_minimum_itself_and_every_newer_build_are_let_through(string version)
    {
        // 401: through the gate and on to authentication, which is where an anonymous probe stops.
        using var response = await SendAsync(Probe, version: version, userAgent: LegacyUserAgent);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_console_through_the_BFF_is_never_gated()
    {
        // What the console's calls look like when they reach the API: the browser's User-Agent,
        // forwarded by YARP, a bearer the BFF attached, and no app version at all.
        using var response = await SendAsync(Probe, userAgent: BrowserUserAgent, bearer: "not-a-real-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Server_to_server_traffic_is_never_gated()
    {
        // .NET's HttpClient sends no User-Agent unless told to — the BFF's own calls, a provider's
        // webhook, a monitoring probe. None of it is the customer app.
        using var response = await SendAsync(Probe);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_user_agent_that_merely_mentions_Khadra_is_not_the_app()
    {
        // The prefix is exact and case-sensitive: `Khadra (`. Anything looser starts catching things
        // that are not the app.
        using var response = await SendAsync(Probe, userAgent: "KhadraMonitor/1.0 (health)");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── What is always open ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_app_configuration_stays_open_to_every_build()
    {
        // Where a build that knows about the minimum reads it. Refused here, that build would fail
        // call by call instead of putting up its update screen.
        using var response = await SendAsync(new Uri("/api/v1/app-config", UriKind.Relative), userAgent: LegacyUserAgent);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var mobileApp = (await ProblemOf(response)).GetProperty("mobileApp");
        Assert.Equal(Minimum, mobileApp.GetProperty("minimumSupportedVersion").GetString());
        Assert.Equal(UpdateUrl, mobileApp.GetProperty("updateUrl").GetString());
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_checks_are_never_gated(string path)
    {
        using var response = await SendAsync(new Uri(path, UriKind.Relative), userAgent: LegacyUserAgent);

        Assert.NotEqual(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    // ── How the refusal reads ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ar", "حدّث تطبيق خضرا للمتابعة")]
    [InlineData("ar-JO", "حدّث تطبيق خضرا للمتابعة")]
    [InlineData("en", "Update the Khadra app to continue")]
    [InlineData("en-GB,en;q=0.9", "Update the Khadra app to continue")]
    public async Task A_build_that_asks_in_a_language_is_answered_in_it(string acceptLanguage, string title)
    {
        using var response = await SendAsync(Probe, version: "1.0.0", acceptLanguage: acceptLanguage);

        Assert.Equal(title, (await ProblemOf(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task A_browser_build_can_read_the_refusal()
    {
        // The proof the gate sits AFTER CORS: without the allow-origin header the browser would
        // report a CORS failure and the app would never see the code.
        using var response = await SendAsync(Probe, version: "1.0.0", userAgent: BrowserUserAgent, origin: AllowedOrigin);

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowed));
        Assert.Equal(AllowedOrigin, Assert.Single(allowed));
    }

    [Fact]
    public async Task The_refusal_comes_before_authentication_even_with_a_token()
    {
        // A signed-in old build is still told to update — not handed a 401 that would send it off to
        // refresh a token against a contract it cannot read.
        using var response = await SendAsync(Probe, userAgent: LegacyUserAgent, bearer: "not-a-real-token");

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task With_no_minimum_set_no_build_is_refused()
    {
        using var factory = Api(minimum: "", updateUrl: null);

        using var response = await SendAsync(Probe, userAgent: LegacyUserAgent, factory: factory);
        using var config = await SendAsync(new Uri("/api/v1/app-config", UriKind.Relative), factory: factory);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var mobileApp = (await ProblemOf(config)).GetProperty("mobileApp");
        Assert.Equal(JsonValueKind.Null, mobileApp.GetProperty("minimumSupportedVersion").ValueKind);
        Assert.Equal(JsonValueKind.Null, mobileApp.GetProperty("updateUrl").ValueKind);
    }

    // ── Configuration that must not start ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.1")]
    [InlineData("v1.1.0")]
    [InlineData("1.1.0-rc.1")]
    [InlineData("latest")]
    public async Task A_minimum_that_is_not_a_release_version_stops_the_api_starting(string minimum)
    {
        // A typo in the one setting that keeps old builds off a contract they cannot read must not
        // quietly switch the gate off, which is what "could not parse, so no minimum" would do.
        using var factory = Api(minimum);

        var failure = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        });

        Assert.Contains("MobileApp", failure.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("example.org/khadra.apk")]
    [InlineData("ftp://example.org/khadra.apk")]
    public async Task An_update_link_a_phone_cannot_open_stops_the_api_starting(string updateUrl)
    {
        using var factory = Api(Minimum, updateUrl);

        var failure = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        });

        Assert.Contains("MobileApp", failure.ToString(), StringComparison.Ordinal);
    }
}
