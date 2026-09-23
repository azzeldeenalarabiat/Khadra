using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Khadra.Infrastructure.Notifications.Push;

/// <summary>
/// Firebase Cloud Messaging, HTTP v1: one request per phone, authorised as the project's service account.
/// </summary>
/// <remarks>
/// <para>
/// <b>No Firebase SDK.</b> What v1 needs is an OAuth access token minted from the service account —
/// a signed JWT exchanged at Google's token endpoint — and a JSON POST. The JWT library this API
/// already uses for its own tokens signs the assertion, so the whole integration is this file and no
/// new dependency to keep patched.
/// </para>
/// <para>
/// <b>What the phone shows.</b> Every message carries a <c>notification</c> block (so Android shows it
/// itself when the app is in the background or not running) on the <c>booking_updates</c> channel the
/// app creates, with the default sound and the notification id as its tag, plus a <c>data</c> block
/// the app reads to open the right screen when it is tapped.
/// </para>
/// <para>
/// <b>Errors.</b> FCM's <c>UNREGISTERED</c> (404) and an invalid token (400 <c>INVALID_ARGUMENT</c>)
/// mean the install is gone: the device is revoked and never tried again. A token from ANOTHER project
/// is reported as <c>SENDER_ID_MISMATCH</c> (403) and treated the same, which is what keeps staging and
/// production apart even if a token strayed. Everything else — 429, 5xx, a timeout, a failed token
/// exchange — is transient and retried by the outbox.
/// </para>
/// </remarks>
internal sealed partial class FcmPushSender(
    IHttpClientFactory httpClients,
    IOptions<PushOptions> options,
    IClock clock,
    ILogger<FcmPushSender> logger) : IPushSender, IDisposable
{
    public const string HttpClientName = "fcm";
    public const string AndroidChannelId = "booking_updates";
    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public bool IsConfigured => options.Value.IsFcm && options.Value.FcmIsComplete;

    public async Task<PushSendResult> SendAsync(PushMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        string accessToken;
        try
        {
            accessToken = await AccessTokenAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException or CryptographicException)
        {
            LogTokenExchangeFailed(logger, exception.GetType().Name);
            return PushSendResult.Transient("Could not obtain an FCM access token.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1/projects/{Uri.EscapeDataString(options.Value.Fcm.ProjectId)}/messages:send")
        {
            Content = JsonContent.Create(Envelope(message), options: Json),
        };
        request.Headers.Authorization = new("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return PushSendResult.Transient($"FCM unreachable ({exception.GetType().Name}).");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
                return PushSendResult.Delivered;

            var status = await ErrorStatusAsync(response, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                _accessToken = null; // Minted again on the next send.

            return IsDeadToken(response.StatusCode, status)
                ? PushSendResult.DeadToken($"FCM {(int)response.StatusCode} {status}")
                : PushSendResult.Transient($"FCM {(int)response.StatusCode} {status}");
        }
    }

    internal static bool IsDeadToken(HttpStatusCode code, string? status) =>
        status is "UNREGISTERED" or "SENDER_ID_MISMATCH"
        || (code == HttpStatusCode.NotFound)
        || (code == HttpStatusCode.BadRequest && status == "INVALID_ARGUMENT");

    internal static object Envelope(PushMessage message) => new
    {
        message = new
        {
            token = message.Token,
            notification = new { title = message.Title, body = message.Body },
            data = message.Data,
            android = new
            {
                priority = "HIGH",
                notification = new
                {
                    channel_id = AndroidChannelId,
                    sound = "default",
                    tag = message.Tag,
                },
            },
            apns = new
            {
                headers = new Dictionary<string, string> { ["apns-collapse-id"] = message.Tag },
                payload = new { aps = new { sound = "default" } },
            },
        },
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static async Task<string?> ErrorStatusAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var error = document.RootElement.GetProperty("error");
            // v1 puts the FCM-specific code in details[].errorCode when there is one; prefer it.
            if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in details.EnumerateArray())
                {
                    if (detail.TryGetProperty("errorCode", out var code) && code.GetString() is { Length: > 0 } fcmCode)
                        return fcmCode;
                }
            }

            return error.TryGetProperty("status", out var status) ? status.GetString() : null;
        }
#pragma warning disable CA1031 // An error body we cannot read is still an error; its status code decides.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>A cached OAuth access token for the service account, minted again five minutes before it expires.</summary>
    private async Task<string> AccessTokenAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        if (_accessToken is { } cached && now < _accessTokenExpiresAt)
            return cached;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            now = clock.UtcNow;
            if (_accessToken is { } again && now < _accessTokenExpiresAt)
                return again;

            if (!FcmServiceAccount.TryParse(options.Value.Fcm.ServiceAccountJson, out var account))
                throw new InvalidOperationException("Push:Fcm:ServiceAccountJson is not a service-account key.");

            using var rsa = RSA.Create();
            rsa.ImportFromPem(account.PrivateKeyPem);
            var assertion = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = account.ClientEmail,
                Audience = account.TokenUri,
                IssuedAt = now.UtcDateTime,
                NotBefore = now.UtcDateTime,
                Expires = now.AddMinutes(55).UtcDateTime,
                Claims = new Dictionary<string, object> { ["scope"] = Scope },
                SigningCredentials = new SigningCredentials(
                    new RsaSecurityKey(rsa.ExportParameters(true)), SecurityAlgorithms.RsaSha256),
            });

            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion,
            });
            using var response = await httpClients.CreateClient(HttpClientName)
                .PostAsync(new Uri(account.TokenUri), form, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Token endpoint answered {(int)response.StatusCode}.");

            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                       ?? throw new HttpRequestException("Token endpoint answered with no body.");

            _accessToken = body.AccessToken;
            _accessTokenExpiresAt = now.AddSeconds(Math.Max(60, body.ExpiresIn - 300));
            return body.AccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    public void Dispose() => _tokenLock.Dispose();

    [LoggerMessage(2310, LogLevel.Warning, "Could not obtain an FCM access token ({Reason}). Pushes will be retried.")]
    private static partial void LogTokenExchangeFailed(ILogger logger, string reason);
}

/// <summary>No push provider: every push is skipped, and the startup log says so.</summary>
internal sealed class UnconfiguredPushSender : IPushSender
{
    public bool IsConfigured => false;

    public Task<PushSendResult> SendAsync(PushMessage message, CancellationToken cancellationToken = default) =>
        Task.FromResult(PushSendResult.Transient("No push provider is configured."));
}
