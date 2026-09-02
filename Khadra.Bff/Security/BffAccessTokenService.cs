using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.Authentication;

namespace Khadra.Bff.Security;

// Returns a valid API access token for the current browser session, refreshing it server-side when it
// is about to expire. Refreshes are single-flight per refresh token: the API's replay detection would
// otherwise revoke the whole family when several proxied requests cross the threshold together.
internal sealed class BffAccessTokenService(AuthApiClient authApiClient)
{
    private static readonly TimeSpan RefreshBeforeExpiry = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RefreshResultGrace = TimeSpan.FromSeconds(60);
    private static readonly ConcurrentDictionary<string, (DateTimeOffset CachedAt, Lazy<Task<ApiAuthTokens?>> Refresh)> RefreshesByToken = new();

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

        var refreshed = await GetOrStartRefreshAsync(refreshToken);
        if (refreshed is null)
        {
            // Dead session: clear the cookie so the SPA stops seeing /bff/user as authenticated.
            await context.SignOutAsync(BffConstants.CookieScheme);
            throw new UnauthorizedAccessException("The session refresh was rejected.");
        }

        StoreTokens(authentication.Properties, refreshed);
        await context.SignInAsync(BffConstants.CookieScheme, authentication.Principal, authentication.Properties);
        return refreshed.AccessToken;
    }

    private Task<ApiAuthTokens?> GetOrStartRefreshAsync(string refreshToken)
    {
        PruneExpiredRefreshes();
        var entry = RefreshesByToken.GetOrAdd(refreshToken, static (key, client) => (
            DateTimeOffset.UtcNow,
            new Lazy<Task<ApiAuthTokens?>>(
                () => RefreshCoreAsync(client, key),
                LazyThreadSafetyMode.ExecutionAndPublication)),
            authApiClient);
        return entry.Refresh.Value;
    }

    private static async Task<ApiAuthTokens?> RefreshCoreAsync(AuthApiClient client, string refreshToken)
    {
        // Deliberately ignores per-request cancellation: the shared result must complete for stragglers.
        var result = await client.RefreshAsync(refreshToken, CancellationToken.None);
        return result.Tokens;
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
}
