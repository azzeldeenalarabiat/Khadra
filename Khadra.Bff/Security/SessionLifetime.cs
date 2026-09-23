using System.Globalization;
using Microsoft.AspNetCore.Authentication;

namespace Khadra.Bff.Security;

/// <summary>
/// When a browser session ends: at the refresh token's own expiry, never later than a fixed ceiling
/// counted from sign-in.
/// </summary>
/// <remarks>
/// Until the customer website, this was computed once at sign-in and never moved, which was harmless
/// for staff (8h ceiling, 14-day refresh token: the ceiling always won). For a customer the refresh
/// token is the SHORTER of the two, and a session pinned to the first token's expiry would end while
/// the API was still happy to keep refreshing it. So every successful refresh re-applies this rule with
/// the new token's expiry. The ceiling is anchored on the sign-in time recorded in the ticket — not on
/// <see cref="AuthenticationProperties.IssuedUtc"/>, which is the cookie's concern — so it can never slide.
/// </remarks>
internal static class SessionLifetime
{
    private const string SignedInAtItem = "khadra:signed_in_at";

    public static void Start(AuthenticationProperties properties, DateTimeOffset now, DateTimeOffset refreshExpiresAt, int absoluteHours)
    {
        properties.Items[SignedInAtItem] = now.ToString("O", CultureInfo.InvariantCulture);
        properties.ExpiresUtc = ExpiryFor(now, refreshExpiresAt, absoluteHours);
    }

    public static void Extend(AuthenticationProperties properties, DateTimeOffset refreshExpiresAt, int absoluteHours)
    {
        // A ticket written before this existed has no sign-in time. Its current expiry already honours
        // the ceiling it was issued under, so it may shrink but never grow.
        if (!properties.Items.TryGetValue(SignedInAtItem, out var value) ||
            !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var signedInAt))
        {
            if (properties.ExpiresUtc is { } current && refreshExpiresAt < current)
                properties.ExpiresUtc = refreshExpiresAt;
            return;
        }

        properties.ExpiresUtc = ExpiryFor(signedInAt, refreshExpiresAt, absoluteHours);
    }

    internal static DateTimeOffset ExpiryFor(DateTimeOffset signedInAt, DateTimeOffset refreshExpiresAt, int absoluteHours)
    {
        var ceiling = signedInAt.AddHours(absoluteHours);
        return refreshExpiresAt < ceiling ? refreshExpiresAt : ceiling;
    }
}
