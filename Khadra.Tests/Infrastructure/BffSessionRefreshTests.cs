using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Khadra.Bff.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// When a BFF ends a browser's session over a failed token refresh, and what the browser is told (pre-launch item 246).
/// </summary>
/// <remarks>
/// <para>
/// Only a definite refusal ends it: a 401 or 403 that is the API's own answer, its ProblemDetails with a code. A 429, a
/// 5xx, the 503 of a refresh race, a timeout, a dropped connection, an answer that is not the token pair, or a 401/403
/// page from an edge in front of the API says nothing about the session, so it is kept and the browser is told 503 to
/// try again. Until 2026-10-10 every one of those signed the person out; on Staging a website session ended when the
/// refresh reached a sleeping API and Render answered 429. The customer app has kept this rule since 1.1.0.
/// </para>
/// <para>
/// The token is got in the proxy pipeline, not in YARP's request transform, where an exception becomes a bodiless 502
/// that the exception handler never sees. The pipeline tests at the end run the real YARP forwarder to prove the
/// browser receives the 503 and its <c>Retry-After</c>, and a refusal's 401.
/// </para>
/// </remarks>
public sealed class BffSessionRefreshTests
{
    private const string Json = "application/json";

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"status":401,"code":"auth.invalid_refresh_token"}""")]
    [InlineData(HttpStatusCode.Forbidden, """{"status":403,"code":"auth.account_suspended"}""")]
    public async Task The_apis_own_refusal_ends_the_session(HttpStatusCode status, string body)
    {
        var (service, session, _) = Arrange(Answer(status, body, Json));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAccessTokenAsync(session.Context, CancellationToken.None));

        Assert.Equal(1, session.SignOuts);
        Assert.Equal(0, session.SignIns);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, """{"status":429,"code":"rate_limited"}""", Json)]
    [InlineData(HttpStatusCode.TooManyRequests, "Too Many Requests", "text/plain")]
    [InlineData(HttpStatusCode.InternalServerError, """{"status":500,"code":"server.error"}""", Json)]
    [InlineData(HttpStatusCode.BadGateway, "<html>Bad gateway</html>", "text/html")]
    [InlineData(HttpStatusCode.ServiceUnavailable, """{"status":503,"code":"auth.refresh_conflict"}""", Json)]
    [InlineData(HttpStatusCode.GatewayTimeout, "", null)]
    // A 403 page from a firewall in front of the API, not the API's refusal: read as one, it would end every session.
    [InlineData(HttpStatusCode.Forbidden, "<html>Access denied (1020)</html>", "text/html")]
    [InlineData(HttpStatusCode.Unauthorized, "", null)]
    public async Task A_passing_failure_keeps_the_session(HttpStatusCode status, string body, string? contentType)
    {
        var (service, session, _) = Arrange(Answer(status, body, contentType));

        var failure = await Assert.ThrowsAsync<BffSessionRefreshUnavailableException>(
            () => service.GetAccessTokenAsync(session.Context, CancellationToken.None));

        Assert.Equal(0, session.SignOuts);
        Assert.Equal(0, session.SignIns);
        // The stored refresh token is exactly what it was, ready for the next attempt.
        Assert.Equal(session.RefreshToken, session.Properties.GetTokenValue("refresh_token"));
        Assert.Equal(status, failure.Status);
    }

    [Theory]
    [InlineData("<html>A captive portal</html>", "text/html")]
    [InlineData("""{"unexpected":true}""", Json)]
    [InlineData("", null)]
    public async Task A_success_that_is_not_the_token_pair_keeps_the_session(string body, string? contentType)
    {
        var (service, session, _) = Arrange(Answer(HttpStatusCode.OK, body, contentType));

        await Assert.ThrowsAsync<BffSessionRefreshUnavailableException>(() => service.GetAccessTokenAsync(session.Context, CancellationToken.None));

        Assert.Equal(0, session.SignOuts);
    }

    [Fact]
    public async Task The_wait_and_the_code_the_api_gave_travel_with_the_failure()
    {
        var (service, session, _) = Arrange(Answer(HttpStatusCode.ServiceUnavailable, """{"status":503,"code":"auth.refresh_conflict"}""", Json,
            headers => headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30))));

        var failure = await Assert.ThrowsAsync<BffSessionRefreshUnavailableException>(
            () => service.GetAccessTokenAsync(session.Context, CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(30), failure.RetryAfter);
        Assert.Equal("auth.refresh_conflict", failure.Code);
    }

    [Fact]
    public async Task A_dropped_connection_keeps_the_session()
    {
        var (service, session, _) = Arrange(_ => throw new HttpRequestException("Connection refused."));

        var failure = await Assert.ThrowsAsync<BffSessionRefreshUnavailableException>(
            () => service.GetAccessTokenAsync(session.Context, CancellationToken.None));

        Assert.Equal(0, session.SignOuts);
        Assert.Null(failure.Status);
        Assert.IsType<HttpRequestException>(failure.InnerException);
    }

    [Fact]
    public async Task A_timeout_keeps_the_session()
    {
        var (service, session, _) = Arrange(_ => throw new TaskCanceledException("The request timed out."));

        await Assert.ThrowsAsync<BffSessionRefreshUnavailableException>(() => service.GetAccessTokenAsync(session.Context, CancellationToken.None));

        Assert.Equal(0, session.SignOuts);
    }

    [Fact]
    public async Task The_next_request_after_a_passing_failure_asks_again_and_succeeds()
    {
        // The single flight remembers an answer for a minute. A failure that is not a verdict must not be remembered,
        // or every request in that minute would be told "unavailable" without the API being asked.
        var (service, session, calls) = Arrange(
            Answer(HttpStatusCode.ServiceUnavailable, """{"status":503,"code":"auth.refresh_conflict"}""", Json),
            Answer(HttpStatusCode.OK, Tokens("access-2", "refresh-2"), Json));

        await Assert.ThrowsAsync<BffSessionRefreshUnavailableException>(() => service.GetAccessTokenAsync(session.Context, CancellationToken.None));
        var token = await service.GetAccessTokenAsync(session.Context, CancellationToken.None);

        Assert.Equal("access-2", token);
        Assert.Equal(2, calls.Count);
        Assert.Equal(1, session.SignIns);
        Assert.Equal(0, session.SignOuts);
        Assert.Equal("refresh-2", session.Properties.GetTokenValue("refresh_token"));
    }

    [Fact]
    public async Task Requests_waiting_on_one_failed_attempt_share_it_and_the_next_one_asks_again()
    {
        // Two requests cross the refresh threshold together: ONE call to the API, and both are told to retry. The
        // request after them is not given the remembered failure: it makes a second call.
        var gate = new TaskCompletionSource();
        var calls = new List<HttpRequestMessage>();
        var session = new FakeSession($"refresh-{Guid.NewGuid():N}");
        var client = new AuthApiClient(
            new StubFactory(new GatedHandler(gate.Task, calls, Answer(HttpStatusCode.ServiceUnavailable, """{"status":503,"code":"auth.refresh_conflict"}""", Json))),
            new HttpContextAccessor(),
            NullLogger<AuthApiClient>.Instance);
        var service = new BffAccessTokenService(client, Options.Create(new BffSecuritySettings()));

        var first = service.GetAccessTokenAsync(session.Context, CancellationToken.None);
        var second = service.GetAccessTokenAsync(session.Context, CancellationToken.None);
        gate.SetResult();
        await Assert.ThrowsAsync<BffSessionRefreshUnavailableException>(() => first);
        await Assert.ThrowsAsync<BffSessionRefreshUnavailableException>(() => second);
        Assert.Single(calls);

        await Assert.ThrowsAsync<BffSessionRefreshUnavailableException>(() => service.GetAccessTokenAsync(session.Context, CancellationToken.None));
        Assert.Equal(2, calls.Count);
        Assert.Equal(0, session.SignOuts);
    }

    // ── Through the proxy pipeline, with the real YARP forwarder ─────────────────────────────────────────────────

    [Fact]
    public async Task Through_the_proxy_a_passing_failure_is_a_503_with_the_wait_never_a_502_or_a_401()
    {
        await using var proxy = await Proxy.StartAsync(
            refresh: Answer(HttpStatusCode.ServiceUnavailable, """{"status":503,"code":"auth.refresh_conflict"}""", Json,
                headers => headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30))));

        var response = await proxy.Client.GetAsync(new Uri("/api/v1/bookings", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(30), response.Headers.RetryAfter?.Delta);
        Assert.Contains("bff.session_refresh_unavailable", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, proxy.Session.SignOuts);
        Assert.Empty(proxy.Forwarded);
    }

    [Fact]
    public async Task Through_the_proxy_a_refusal_is_a_401_and_ends_the_session()
    {
        await using var proxy = await Proxy.StartAsync(
            refresh: Answer(HttpStatusCode.Unauthorized, """{"status":401,"code":"auth.invalid_refresh_token"}""", Json));

        var response = await proxy.Client.GetAsync(new Uri("/api/v1/bookings", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("bff.session_expired", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, proxy.Session.SignOuts);
        Assert.Empty(proxy.Forwarded);
    }

    [Fact]
    public async Task Through_the_proxy_a_refreshed_token_is_the_bearer_the_api_receives()
    {
        await using var proxy = await Proxy.StartAsync(refresh: Answer(HttpStatusCode.OK, Tokens("access-2", "refresh-2"), Json));

        var response = await proxy.Client.GetAsync(new Uri("/api/v1/bookings", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forwarded = Assert.Single(proxy.Forwarded);
        Assert.Equal("Bearer access-2", forwarded);
    }

    // ── Harness ─────────────────────────────────────────────────────────────────────────────────────────────────

    private static (BffAccessTokenService Service, FakeSession Session, List<HttpRequestMessage> Calls) Arrange(
        params Func<HttpRequestMessage, HttpResponseMessage>[] answers)
    {
        var calls = new List<HttpRequestMessage>();
        var service = Service(new SequenceHandler(answers, calls));
        // A refresh token of its own per test: the single flight is process-wide.
        return (service, new FakeSession($"refresh-{Guid.NewGuid():N}"), calls);
    }

    private static BffAccessTokenService Service(HttpMessageHandler apiHandler) =>
        new(
            new AuthApiClient(new StubFactory(apiHandler), new HttpContextAccessor(), NullLogger<AuthApiClient>.Instance),
            Options.Create(new BffSecuritySettings()));

    private static Func<HttpRequestMessage, HttpResponseMessage> Answer(
        HttpStatusCode status,
        string body,
        string? contentType,
        Action<HttpResponseHeaders>? headers = null) => _ =>
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        if (contentType is not null)
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType) { CharSet = "utf-8" };
        var response = new HttpResponseMessage(status) { Content = content };
        headers?.Invoke(response.Headers);
        return response;
    };

    private static string Tokens(string access, string refresh)
    {
        var now = DateTimeOffset.UtcNow;
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            accessToken = access,
            accessTokenExpiresAt = now.AddMinutes(15),
            refreshToken = refresh,
            refreshTokenExpiresAt = now.AddDays(14),
            user = new
            {
                id = Guid.NewGuid(),
                email = "someone@example.jo",
                fullName = "Layla Odeh",
                phone = "+962790000000",
                role = "Customer",
                isEmailVerified = true,
                mustChangePassword = false,
                createdAt = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            },
        });
    }

    /// <summary>A signed-in cookie whose access token has expired, so the next call must refresh.</summary>
    private sealed class FakeSession : IAuthenticationService
    {
        public FakeSession(string refreshToken)
        {
            RefreshToken = refreshToken;
            Properties.StoreTokens([
                new AuthenticationToken { Name = "access_token", Value = "access-1" },
                new AuthenticationToken { Name = "refresh_token", Value = refreshToken },
                new AuthenticationToken { Name = "expires_at", Value = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O", System.Globalization.CultureInfo.InvariantCulture) },
                new AuthenticationToken { Name = "refresh_expires_at", Value = DateTimeOffset.UtcNow.AddDays(10).ToString("O", System.Globalization.CultureInfo.InvariantCulture) },
            ]);
            Context = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(this).BuildServiceProvider(),
            };
        }

        public string RefreshToken { get; }
        public AuthenticationProperties Properties { get; } = new();
        public HttpContext Context { get; }
        public int SignIns { get; private set; }
        public int SignOuts { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1")], "test")), Properties, scheme ?? "test")));

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            SignIns++;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignOuts++;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// The BFF's proxy as Program.cs wires it, reduced to what item 246 is about: one session route to the API, the
    /// exception handler, the pipeline step that gets the token, and the transform that attaches it. The API behind
    /// it is a stub; its refresh endpoint answers as the test says, and every forwarded call answers 200.
    /// </summary>
    private sealed class Proxy : IAsyncDisposable
    {
        private readonly WebApplication app;

        private Proxy(WebApplication app, FakeSession session, List<string?> forwarded)
        {
            this.app = app;
            Session = session;
            Forwarded = forwarded;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }
        public FakeSession Session { get; }
        public List<string?> Forwarded { get; }

        public static async Task<Proxy> StartAsync(Func<HttpRequestMessage, HttpResponseMessage> refresh)
        {
            var session = new FakeSession($"refresh-{Guid.NewGuid():N}");
            var forwarded = new List<string?>();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<IAuthenticationService>(session);
            builder.Services.AddSingleton(Service(new SequenceHandler([refresh], [])));
            builder.Services.AddProblemDetails();
            builder.Services.AddExceptionHandler<BffExceptionHandler>();
            builder.Services.AddSingleton<IForwarderHttpClientFactory>(new StubForwarderFactory(forwarded));
            builder.Services.AddReverseProxy()
                .LoadFromMemory(
                    [new RouteConfig { RouteId = "api", ClusterId = "api", Match = new RouteMatch { Path = "/api/{**rest}" } }],
                    [new ClusterConfig
                    {
                        ClusterId = "api",
                        Destinations = new Dictionary<string, DestinationConfig> { ["api"] = new() { Address = "https://api.test/" } },
                    }])
                .AddTransforms(transforms => transforms.AddRequestTransform(transform =>
                {
                    BffProxyAccessToken.AttachTo(transform.HttpContext, transform.ProxyRequest);
                    return ValueTask.CompletedTask;
                }));

            var app = builder.Build();
            app.UseExceptionHandler();
            app.MapReverseProxy(pipeline => pipeline.Use(BffProxyAccessToken.AcquireAsync));
            await app.StartAsync();
            return new Proxy(app, session, forwarded);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class StubForwarderFactory(List<string?> forwarded) : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(new RecordingApi(forwarded));
    }

    private sealed class RecordingApi(List<string?> forwarded) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            forwarded.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, Json) });
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.test/") };
    }

    private sealed class SequenceHandler(Func<HttpRequestMessage, HttpResponseMessage>[] answers, List<HttpRequestMessage> calls)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            calls.Add(request);
            var answer = answers[Math.Min(calls.Count - 1, answers.Length - 1)];
            return Task.FromResult(answer(request));
        }
    }

    /// <summary>Answers only once the test opens the gate, so several requests can be waiting on one attempt.</summary>
    private sealed class GatedHandler(Task gate, List<HttpRequestMessage> calls, Func<HttpRequestMessage, HttpResponseMessage> answer)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (calls)
                calls.Add(request);
            await gate;
            return answer(request);
        }
    }
}
