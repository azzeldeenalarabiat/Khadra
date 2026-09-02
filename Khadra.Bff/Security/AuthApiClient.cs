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
        AddClientAddress(request);

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
        AddClientAddress(request);

        using var response = await httpClientFactory.CreateClient(HttpClientName)
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var tokens = await response.Content.ReadFromJsonAsync<ApiAuthTokens>(cancellationToken);
            return new AuthApiResult(tokens, response.StatusCode, null);
        }

        ApiProblem? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<ApiProblem>(cancellationToken);
        }
        catch (JsonException)
        {
            // Non-JSON error body (e.g. a proxy page); fall through with a generic problem.
        }

        return new AuthApiResult(null, response.StatusCode, problem);
    }

    // Lets the API's per-IP rate limiter see the real browser address (trusted only via KnownProxies).
    private void AddClientAddress(HttpRequestMessage request)
    {
        var address = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
        if (address is not null)
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", address.ToString());
    }

    [LoggerMessage(3001, LogLevel.Warning, "API logout returned {StatusCode}; the refresh-token family stays live until expiry.")]
    private static partial void LogLogoutFailed(ILogger logger, int statusCode);
}

internal sealed record AuthApiResult(ApiAuthTokens? Tokens, HttpStatusCode StatusCode, ApiProblem? Problem);

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
