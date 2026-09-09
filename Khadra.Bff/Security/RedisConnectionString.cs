using System.Globalization;
using StackExchange.Redis;

namespace Khadra.Bff.Security;

/// <summary>
/// Accepts a Redis connection string in either of the two forms a deployment might hand over.
/// </summary>
/// <remarks>
/// The same mismatch the database had, one service along. StackExchange.Redis reads its own
/// comma-separated form — <c>host:port,password=…,ssl=True</c> — while managed platforms hand out a
/// URL: <c>redis://:password@host:6379</c>, or <c>rediss://</c> when TLS is required. Render, Heroku,
/// Fly and Upstash all do.
///
/// It matters more here than it did for Postgres, because <c>ConnectionMultiplexer.ConnectAsync</c>
/// is awaited during startup and a failure is fatal by design: Redis holds the session tickets and
/// the Data Protection key ring, so a BFF that cannot reach it cannot sign anybody in. A URL would
/// therefore not produce a subtle 500 — it would crash-loop the console before it ever served a page.
///
/// Fixed before it could happen rather than after, which is the only reason this file reads as
/// unremarkable.
/// </remarks>
internal static class RedisConnectionString
{
    /// <summary>Returns StackExchange.Redis form, converting from URL form when that is what was given.</summary>
    public static string Normalise(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var trimmed = connectionString.Trim();
        if (!LooksLikeUrl(trimmed)) return trimmed;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                "The Redis connection string begins like a URL but could not be parsed as one. " +
                "Expected redis://[user]:password@host:port, or StackExchange.Redis form.");
        }

        var options = new ConfigurationOptions
        {
            // rediss:// is the TLS scheme, exactly as https:// is to http://. A managed instance
            // reached over anything but a private network will use it, and getting this wrong fails
            // as a timeout rather than as anything that names TLS.
            Ssl = uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase),
        };

        options.EndPoints.Add(uri.Host, uri.IsDefaultPort ? 6379 : uri.Port);

        // Credentials are percent-encoded in a URL, so they are decoded before use. Redis before
        // version 6 had no usernames and the URL carries only a password — "redis://:secret@host" —
        // which is why an empty user is normal rather than a parse failure.
        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length > 0 && userInfo[0].Length > 0)
            options.User = Uri.UnescapeDataString(userInfo[0]);
        if (userInfo.Length > 1 && userInfo[1].Length > 0)
            options.Password = Uri.UnescapeDataString(userInfo[1]);

        // The path names a database number: redis://host:6379/0. Absent means 0, the default.
        var path = uri.AbsolutePath.Trim('/');
        if (path.Length > 0 && int.TryParse(path, NumberStyles.Integer, CultureInfo.InvariantCulture, out var db))
            options.DefaultDatabase = db;

        return options.ToString();
    }

    private static bool LooksLikeUrl(string value) =>
        value.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase);
}
