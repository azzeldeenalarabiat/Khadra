using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Khadra.Bff.Security;

// Returns a valid API access token for the current browser session, refreshing it server-side when it
// is about to expire. Refreshes are single-flight per refresh token: the API's replay detection would
// otherwise revoke the whole family when several proxied requests cross the threshold together.
//
// Only a definite refusal ends the session (pre-launch item 246): a 401 or 403 that is the API's own answer, its
// ProblemDetails with a code. Anything else -- a 429, a 5xx, the 503 of a refresh race, a timeout, a dropped
// connection, an answer that is not the token pair, a 401/403 page from an edge in front of the API -- says nothing
// about whether the session is valid, so the cookie and its stored refresh token are kept and the caller is told to
// try again (BffSessionRefreshUnavailableException, a 503). The edge case matters: this refresh is server to server
// through the same edge as everyone's, so a firewall's 403 page read as a refusal would sign every session out.
// The customer app has kept that rule since 1.1.0; until 2026-10-10 the BFFs ended a session on any failure.
internal sealed class BffAccessTokenService(AuthApiClient authApiClient, IOptions<BffSecuritySettings> settings)
{
    private static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RefreshResultGrace = TimeSpan.FromSeconds(60);
    private static readonly ConcurrentDictionary<string, (DateTimeOffset CachedAt, Lazy<Task<RefreshOutcome>> Refresh)> RefreshesByToken = new();

    public async Task<string> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var authentication = await context.AuthenticateAsync(BffConstants.CookieScheme);
        if (!authentication.Succeeded || authentication.Properties is null || authentication.Principal is null)
            throw new UnauthorizedAccessException("The session is unavailable.");

        var accessToken = authentication.Properties.GetTokenValue(BffConstants.AccessTokenName);
        var expiresAtValue = authentication.Properties.GetTokenValue(BffConstants.AccessExpiresAtName);
        if (!string.IsNullOrEmpty(accessToken) &&
            DateTimeOffset.TryParse(expiresAtValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt) &&
            expiresAt > DateTimeOffset.UtcNow.Add(RefreshBeforeExpiry))
        {
            return accessToken;
        }

        var refreshToken = authentication.Properties.GetTokenValue(BffConstants.RefreshTokenName);
        if (string.IsNullOrEmpty(refreshToken))
        {
            await context.SignOutAsync(BffConstants.CookieScheme);
            throw new UnauthorizedAccessException("The session cannot be refreshed.");
        }

        var outcome = await GetOrStartRefreshAsync(refreshToken);
        if (outcome.Refused)
        {
            // Dead session: clear the cookie so the SPA stops seeing /bff/user as authenticated.
            await context.SignOutAsync(BffConstants.CookieScheme);
            throw new UnauthorizedAccessException("The session refresh was rejected.");
        }

        if (outcome.Tokens is not { } refreshed)
        {
            // Not a verdict. The session stays exactly as it was; the next request refreshes again.
            throw new BffSessionRefreshUnavailableException(outcome.RetryAfter, outcome.Status, outcome.Code, outcome.Error);
        }

        StoreTokens(authentication.Properties, refreshed);
        SessionLifetime.Extend(authentication.Properties, refreshed.RefreshTokenExpiresAt, settings.Value.SessionAbsoluteHours);
        await context.SignInAsync(BffConstants.CookieScheme, authentication.Principal, authentication.Properties);
        return refreshed.AccessToken;
    }

    private async Task<RefreshOutcome> GetOrStartRefreshAsync(string refreshToken)
    {
        PruneExpiredRefreshes();
        var entry = RefreshesByToken.GetOrAdd(refreshToken, static (key, client) => (
            DateTimeOffset.UtcNow,
            new Lazy<Task<RefreshOutcome>>(
                () => RefreshCoreAsync(client, key),
                LazyThreadSafetyMode.ExecutionAndPublication)),
            authApiClient);

        var remembered = new KeyValuePair<string, (DateTimeOffset, Lazy<Task<RefreshOutcome>>)>(refreshToken, entry);
        RefreshOutcome outcome;
        try
        {
            outcome = await entry.Refresh.Value;
        }
        catch
        {
            // RefreshCoreAsync catches everything it can; should anything still escape, it is not remembered either.
            RefreshesByToken.TryRemove(remembered);
            throw;
        }

        // A failure that is not a verdict is not remembered. Every request already waiting on this attempt shares its
        // answer, but the next one asks the API again rather than being told "unavailable" for the rest of the grace.
        // Removed only if it is still this attempt's entry, so a newer attempt started meanwhile is left alone.
        if (outcome.IsUnavailable)
            RefreshesByToken.TryRemove(remembered);

        return outcome;
    }

    private static async Task<RefreshOutcome> RefreshCoreAsync(AuthApiClient client, string refreshToken)
    {
        AuthApiResult result;
        try
        {
            // Deliberately ignores per-request cancellation: the shared result must complete for stragglers.
            // So a cancellation here is the HTTP client's own timeout, not a browser that went away.
            result = await client.RefreshAsync(refreshToken, CancellationToken.None);
        }
        catch (Exception error)
        {
            // Nothing an HTTP call throws is a verdict on the session: a dropped connection, the client's own timeout,
            // a body that could not be read. Its type travels on the exception for the log.
            return RefreshOutcome.Unavailable(null, null, null, error);
        }

        if (result.Tokens is { } tokens)
            return RefreshOutcome.Issued(tokens);

        // A refusal is a 401 or 403 that the API itself sent: it always answers this endpoint with its ProblemDetails
        // and a code (auth.invalid_refresh_token, auth.account_suspended, ...). Without one the answer came from
        // something in between, and says nothing about this session.
        var refusedByTheApi = result.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            && !string.IsNullOrEmpty(result.Problem?.Code);
        return refusedByTheApi
            ? RefreshOutcome.RefusedOutcome
            : RefreshOutcome.Unavailable(result.RetryAfter, result.StatusCode, result.Problem?.Code, null);
    }

    private static void PruneExpiredRefreshes()
    {
        var cutoff = DateTimeOffset.UtcNow - RefreshResultGrace;
        foreach (var pair in RefreshesByToken)
        {
            if (pair.Value.CachedAt < cutoff)
                RefreshesByToken.TryRemove(pair.Key, out _);
        }
    }

    internal static void StoreTokens(AuthenticationProperties properties, ApiAuthTokens tokens)
    {
        properties.StoreTokens([
            new AuthenticationToken { Name = BffConstants.AccessTokenName, Value = tokens.AccessToken },
            new AuthenticationToken { Name = BffConstants.RefreshTokenName, Value = tokens.RefreshToken },
            new AuthenticationToken
            {
                Name = BffConstants.AccessExpiresAtName,
                Value = tokens.AccessTokenExpiresAt.ToString("O", CultureInfo.InvariantCulture)
            },
            new AuthenticationToken
            {
                Name = BffConstants.RefreshExpiresAtName,
                Value = tokens.RefreshTokenExpiresAt.ToString("O", CultureInfo.InvariantCulture)
            }
        ]);
    }

    /// <summary>What one refresh attempt came to: new tokens, a definite refusal, or nothing either way.</summary>
    private sealed record RefreshOutcome(
        ApiAuthTokens? Tokens,
        bool Refused,
        TimeSpan? RetryAfter,
        HttpStatusCode? Status,
        string? Code,
        Exception? Error)
    {
        public static readonly RefreshOutcome RefusedOutcome = new(null, true, null, null, null, null);

        public bool IsUnavailable => Tokens is null && !Refused;

        public static RefreshOutcome Issued(ApiAuthTokens tokens) => new(tokens, false, null, null, null, null);

        public static RefreshOutcome Unavailable(TimeSpan? retryAfter, HttpStatusCode? status, string? code, Exception? error) =>
            new(null, false, retryAfter, status, code, error);
    }
}
