using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Khadra.Bff.Security;

// Server-to-server calls to the WebAPI auth endpoints. The BFF is the only holder of API tokens.
internal sealed partial class AuthApiClient(
    IHttpClientFactory httpClientFactory,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AuthApiClient> logger)
{
    public const string HttpClientName = "khadra-api";

    public Task<AuthApiResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
        SendAsync("api/v1/auth/login", new { email, password }, accessToken: null, cancellationToken);

    public Task<AuthApiResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        SendAsync("api/v1/auth/refresh", new { refreshToken }, accessToken: null, cancellationToken);

    public Task<AuthApiResult> ChangePasswordAsync(
        string accessToken,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken) =>
        SendAsync("api/v1/auth/change-password", new { currentPassword, newPassword }, accessToken, cancellationToken);

    public async Task LogoutAsync(string accessToken, string refreshToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/logout")
        {
            Content = JsonContent.Create(new { refreshToken, allDevices = false })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        AddClientContext(request);

        using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            LogLogoutFailed(logger, (int)response.StatusCode);
    }

    private async Task<AuthApiResult> SendAsync(
        string path,
        object payload,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
        if (!string.IsNullOrEmpty(accessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        AddClientContext(request);

        using var response = await httpClientFactory.CreateClient(HttpClientName)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            ApiAuthTokens? tokens = null;
            try
            {
                tokens = await response.Content.ReadFromJsonAsync<ApiAuthTokens>(cancellationToken);
            }
            catch (Exception error) when (error is JsonException or NotSupportedException)
            {
                // Not the token pair: a proxy's page, an empty or truncated body. NotSupportedException is what
                // ReadFromJsonAsync throws for a body that is not JSON at all (text/html, no content type).
            }

            // A success that carried no tokens is a broken hop, not an answer: reported as a bad gateway so no caller
            // mistakes it for a refusal (pre-launch item 246). JSON without the two tokens is not the pair either: it
            // deserialises into one whose tokens are null, which would otherwise be installed as the session's.
            return tokens is null || string.IsNullOrEmpty(tokens.AccessToken) || string.IsNullOrEmpty(tokens.RefreshToken)
                ? new AuthApiResult(null, HttpStatusCode.BadGateway, null)
                : new AuthApiResult(tokens, response.StatusCode, null);
        }

        ApiProblem? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<ApiProblem>(cancellationToken);
        }
        catch (Exception error) when (error is JsonException or NotSupportedException)
        {
            // Not the API's ProblemDetails (a proxy page, a plain-text 429 from the edge): no problem, and callers
            // treat its absence as "not the API's own verdict".
        }

        return new AuthApiResult(null, response.StatusCode, problem, RetryAfterOf(response));
    }

    /// <summary>
    /// How long the API asked the caller to wait, whichever form it used. Kept so the BFF can pass it on:
    /// without it a browser refused by the login limiter can only say "wait a little", when the API
    /// knows the answer to the second.
    /// </summary>
    internal static TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
            return delta;
        if (header?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
        return null;
    }

    /// <summary>
    /// Passes on who the browser is: its address, so the API's rate limiter can tell one client from
    /// another, and its user agent, so a session can be recognised on the security screen.
    /// </summary>
    /// <remarks>
    /// Both are read from the connection, and the API accepts them only from an address named in its
    /// KnownProxies — this is a claim about the caller, not something the caller may assert.
    ///
    /// The user agent was missing, and it mattered: YARP copies it on the routes it proxies, but the
    /// calls that actually mint a refresh-token family are these, made by the BFF itself. So every
    /// browser session recorded a null agent and /security listed all of them as "Device not
    /// recorded" — a screen whose whole job is to let someone spot a session they do not recognise.
    /// TryAddWithoutValidation because real agent strings routinely fail strict header parsing.
    /// </remarks>
    private void AddClientContext(HttpRequestMessage request)
    {
        var context = httpContextAccessor.HttpContext;
        var address = context?.Connection.RemoteIpAddress;
        if (address is not null)
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", address.ToString());

        var userAgent = context?.Request.Headers.UserAgent.ToString();
        if (!string.IsNullOrWhiteSpace(userAgent))
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
    }

    [LoggerMessage(3001, LogLevel.Warning, "API logout returned {StatusCode}; the refresh-token family stays live until expiry.")]
    private static partial void LogLogoutFailed(ILogger logger, int statusCode);
}

internal sealed record AuthApiResult(
    ApiAuthTokens? Tokens,
    HttpStatusCode StatusCode,
    ApiProblem? Problem,
    TimeSpan? RetryAfter = null)
{
    /// <summary>
    /// Relays the API's Retry-After onto the BFF's own answer, in whole seconds and never below one,
    /// the form the API itself sends.
    /// </summary>
    public void CopyRetryAfterTo(HttpResponse response) => WriteRetryAfter(response, RetryAfter);

    /// <summary>Writes <paramref name="wait"/> as a Retry-After in whole seconds, never below one; nothing when null.</summary>
    internal static void WriteRetryAfter(HttpResponse response, TimeSpan? wait)
    {
        if (wait is not { } value)
            return;
        var seconds = Math.Max(1, (int)Math.Ceiling(value.TotalSeconds));
        response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

internal sealed record ApiAuthTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    ApiUser User);

internal sealed record ApiUser(
    Guid Id,
    string Email,
    string FullName,
    string Phone,
    string Role,
    bool IsEmailVerified,
    bool MustChangePassword,
    DateTimeOffset CreatedAt);

internal sealed record ApiProblem(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("status")] int? Status,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("errors")] Dictionary<string, string[]>? Errors);
