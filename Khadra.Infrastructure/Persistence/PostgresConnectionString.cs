using System.Globalization;
using Npgsql;

namespace Khadra.Infrastructure.Persistence;

/// <summary>
/// Accepts a Postgres connection string in either of the two forms a deployment might hand over.
/// </summary>
/// <remarks>
/// Npgsql understands only keyword form — <c>Host=…;Username=…;Password=…</c>. Most managed
/// platforms hand out a URL instead — <c>postgresql://user:pass@host:5432/db</c> — because that is
/// what libpq, Rails, Django and every Node driver accept. Render, Heroku, Fly and Railway all do.
///
/// Given a URL, Npgsql does not fail with anything that names the problem. It throws
/// <c>Format of the initialization string does not conform to specification starting at index 0</c>,
/// and only on first use — so the application starts perfectly, answers every request that avoids
/// the database, and returns 500 for every request that does not. That is a genuinely difficult
/// thing to read backwards, and it cost a deployment an evening.
///
/// Converting is a dozen lines and removes the class of mistake entirely, so the same configuration
/// value works whichever form the platform gives it in.
/// </remarks>
internal static class PostgresConnectionString
{
    /// <summary>Returns keyword form, converting from URL form when that is what was given.</summary>
    public static string Normalise(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var trimmed = connectionString.Trim();
        if (!LooksLikeUrl(trimmed)) return trimmed;

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                "The Postgres connection string begins like a URL but could not be parsed as one. " +
                "Expected postgresql://user:password@host:port/database, or Npgsql keyword form.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            // A URL that names no port means the default, the same as omitting it in keyword form.
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            // The path is "/database"; an empty one is a malformed URL rather than a default.
            Database = uri.AbsolutePath.TrimStart('/'),
        };

        // Credentials are percent-encoded in a URL — a password containing @ or / has to be, or the
        // URL would not parse — so they are decoded before Npgsql is given them.
        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length > 0 && userInfo[0].Length > 0)
            builder.Username = Uri.UnescapeDataString(userInfo[0]);
        if (userInfo.Length > 1)
            builder.Password = Uri.UnescapeDataString(userInfo[1]);

        // Anything after ? — sslmode being the one that matters — carried across rather than dropped.
        var sslModeCameFromTheUrl = false;
        foreach (var (key, value) in ParseQuery(uri.Query))
        {
            builder[key] = value;
            if (string.Equals(key, "SSL Mode", StringComparison.OrdinalIgnoreCase))
                sslModeCameFromTheUrl = true;
        }

        // Managed Postgres is reached over the public internet or a shared network and requires TLS;
        // libpq-style URLs usually leave it implicit, and Npgsql's default of Prefer would silently
        // accept an unencrypted connection. Applied only when the URL did not say, so an explicit
        // sslmode still wins.
        //
        // Tracked with a flag rather than asking the builder whether it has the key: a
        // NpgsqlConnectionStringBuilder reports every key it knows about as present, default or not,
        // so ContainsKey is always true here and the default would never have been applied.
        if (!sslModeCameFromTheUrl)
        {
            builder.SslMode = SslMode.Require;
        }

        return builder.ConnectionString;
    }

    private static bool LooksLikeUrl(string value) =>
        value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<(string Key, string Value)> ParseQuery(string query)
    {
        if (string.IsNullOrEmpty(query)) yield break;

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2) continue;

            var key = Uri.UnescapeDataString(parts[0]);
            var value = Uri.UnescapeDataString(parts[1]);

            // libpq spells it sslmode=require; Npgsql's builder wants SSL Mode=Require. The builder
            // accepts either spelling of the key, but not every libpq value, so the one that differs
            // is mapped and the rest passed through.
            if (string.Equals(key, "sslmode", StringComparison.OrdinalIgnoreCase))
            {
                yield return ("SSL Mode", MapSslMode(value));
                continue;
            }

            yield return (key, value);
        }
    }

    private static string MapSslMode(string value) =>
        value.ToLower(CultureInfo.InvariantCulture) switch
        {
            // libpq's "prefer" and "allow" have no exact Npgsql equivalent; both mean "use TLS if the
            // server offers it", which is Prefer.
            "prefer" or "allow" => nameof(SslMode.Prefer),
            "disable" => nameof(SslMode.Disable),
            "require" => nameof(SslMode.Require),
            "verify-ca" => nameof(SslMode.VerifyCA),
            "verify-full" => nameof(SslMode.VerifyFull),
            _ => value,
        };
}
