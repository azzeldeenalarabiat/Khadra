using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Khadra.Bff.Security;

/// <summary>
/// The idle timeout, kept where background polling cannot move it (pre-launch item 129).
/// </summary>
/// <remarks>
/// <para>
/// The session ticket lives in Redis with a SLIDING expiry, and a Redis read slides it: every authenticated request kept
/// the session alive, so a console polling in the background would never go idle. The console stopped polling after ten
/// quiet minutes, but that made a security property depend on the client being polite.
/// </para>
/// <para>
/// So the last time a PERSON used the session is kept here, under its own key, with an absolute expiry of that moment
/// plus <see cref="BffSecuritySettings.SessionIdleMinutes"/>. Reading it does not extend it; only activity writes it. A
/// request says how long ago its person last touched the page in <see cref="IdleHeaderName"/> (the console sends it on
/// every call), so a poll from an unattended console reports a growing idle time and moves nothing, while the same poll
/// with somebody scrolling the screen counts. A request without the header — the website, an older console build —
/// counts as activity now, exactly as every request did before. Once the key has expired, the next request ends the
/// session.
/// </para>
/// <para>
/// Not in the ticket, deliberately. Writing the ticket on activity would race the access-token refresh, which also
/// rewrites it: a stale copy written last would put back a refresh token the API has already rotated, and replaying that
/// revokes the whole session family. This key holds a time and nothing else.
/// </para>
/// </remarks>
internal sealed class SessionActivity(IDistributedCache cache, IOptions<BffSecuritySettings> settings)
{
    /// <summary>Seconds since the person last pressed a key, clicked or scrolled in the page that sent the request.</summary>
    public const string IdleHeaderName = "X-Khadra-Idle-Seconds";

    /// <summary>The ticket item naming this session's activity key. Written once, at sign-in, and never changed.</summary>
    internal const string IdItem = "khadra:activity_id";

    /// <summary>Activity is written at most this often: the idle timeout is measured in minutes, not requests.</summary>
    internal static readonly TimeSpan WriteGranularity = TimeSpan.FromMinutes(1);

    private TimeSpan IdleTimeout => TimeSpan.FromMinutes(settings.Value.SessionIdleMinutes);

    /// <summary>A session starts active.</summary>
    public Task StartAsync(AuthenticationProperties properties, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        properties.Items[IdItem] = id;
        return WriteAsync(id, now, now);
    }

    /// <summary>
    /// Whether the session may go on: false once it has been idle for the timeout. Records this request's activity
    /// when it is newer than what is stored.
    /// </summary>
    public async Task<bool> TouchAsync(AuthenticationProperties properties, HttpRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(request);

        // A ticket issued before this existed has no key. It keeps the rule it was issued under — the ticket's own
        // sliding expiry — for the rest of its life, which the absolute ceiling bounds.
        if (!properties.Items.TryGetValue(IdItem, out var id) || string.IsNullOrEmpty(id))
            return true;

        var stored = await cache.GetStringAsync(Key(id), request.HttpContext.RequestAborted);
        if (stored is null ||
            !DateTimeOffset.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastActive) ||
            now - lastActive >= IdleTimeout)
        {
            return false;
        }

        var activeAt = ActiveAt(request, now);
        if (activeAt - lastActive >= WriteGranularity)
            await WriteAsync(id, activeAt, now);
        return true;
    }

    /// <summary>When this request's person was last active: now, less the idle time the page reported.</summary>
    internal DateTimeOffset ActiveAt(HttpRequest request, DateTimeOffset now)
    {
        // Digits only: no sign, no fraction. Anything else is read as no report at all — what every client sent before
        // the header existed — and so as activity now. A client that lies gains nothing an ordinary request does not.
        if (!int.TryParse(request.Headers[IdleHeaderName].ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            return now;

        var idle = TimeSpan.FromSeconds(Math.Min(seconds, IdleTimeout.TotalSeconds));
        return now - idle;
    }

    private Task WriteAsync(string id, DateTimeOffset activeAt, DateTimeOffset now) =>
        cache.SetStringAsync(
            Key(id),
            activeAt.ToString("O", CultureInfo.InvariantCulture),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = activeAt + IdleTimeout - now });

    private static string Key(string id) => $"bff:activity:{id}";
}
