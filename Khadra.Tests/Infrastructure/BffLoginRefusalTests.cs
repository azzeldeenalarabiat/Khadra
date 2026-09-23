using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Khadra.Bff.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// What the BFF passes on when the API refuses a sign-in. The login limiter's Retry-After was dropped
/// here, so a browser refused by it could only say "wait a little" while the API knew the wait to the
/// second (found on 2026-09-24, testing the customer website). The installed app talks to the API
/// directly and was never affected.
/// </summary>
public sealed class BffLoginRefusalTests
{
    private const string RateLimitedBody =
        """{"title":"Too many requests.","status":429,"code":"rate_limit.exceeded"}""";

    [Fact]
    public async Task A_rate_limited_sign_in_keeps_the_wait_the_api_gave()
    {
        var client = ClientAnswering(HttpStatusCode.TooManyRequests, RateLimitedBody,
            headers => headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(540)));

        var result = await client.LoginAsync("someone@example.jo", "not-a-real-password", CancellationToken.None);

        Assert.Null(result.Tokens);
        Assert.Equal(HttpStatusCode.TooManyRequests, result.StatusCode);
        Assert.Equal("rate_limit.exceeded", result.Problem!.Code);
        Assert.Equal(TimeSpan.FromSeconds(540), result.RetryAfter);
    }

    [Fact]
    public async Task A_wait_given_as_a_date_is_read_as_the_time_left()
    {
        var client = ClientAnswering(HttpStatusCode.TooManyRequests, RateLimitedBody,
            headers => headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(5)));

        var result = await client.LoginAsync("someone@example.jo", "not-a-real-password", CancellationToken.None);

        Assert.NotNull(result.RetryAfter);
        Assert.InRange(result.RetryAfter!.Value, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task A_refusal_without_a_wait_carries_none()
    {
        var client = ClientAnswering(HttpStatusCode.Unauthorized,
            """{"title":"Invalid credentials.","status":401,"code":"auth.invalid_credentials"}""");

        var result = await client.LoginAsync("someone@example.jo", "not-a-real-password", CancellationToken.None);

        Assert.Equal("auth.invalid_credentials", result.Problem!.Code);
        Assert.Null(result.RetryAfter);
    }

    [Fact]
    public void The_wait_reaches_the_browser_in_whole_seconds_and_never_as_zero()
    {
        Assert.Equal("540", Relayed(TimeSpan.FromSeconds(539.2)));
        Assert.Equal("1", Relayed(TimeSpan.Zero));
        Assert.Null(Relayed(null));
    }

    private static string? Relayed(TimeSpan? wait)
    {
        var context = new DefaultHttpContext();
        new AuthApiResult(null, HttpStatusCode.TooManyRequests, null, wait).CopyRetryAfterTo(context.Response);
        var header = context.Response.Headers.RetryAfter;
        return header.Count == 0 ? null : header.ToString();
    }

    private static AuthApiClient ClientAnswering(
        HttpStatusCode status,
        string body,
        Action<HttpResponseHeaders>? headers = null) =>
        new(new StubFactory(new StubHandler(status, body, headers)), new HttpContextAccessor(), NullLogger<AuthApiClient>.Instance);

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.test/") };
    }

    private sealed class StubHandler(HttpStatusCode status, string body, Action<HttpResponseHeaders>? headers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/problem+json"),
            };
            headers?.Invoke(response.Headers);
            return Task.FromResult(response);
        }
    }
}
