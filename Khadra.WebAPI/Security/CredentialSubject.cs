using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Khadra.WebAPI.Security;

/// <summary>
/// Surfaces the account an anonymous auth request is ABOUT, so the rate limiter can partition by it.
/// </summary>
/// <remarks>
/// Every limit on this API used to be keyed on the client address alone. On a home connection that
/// is one household; in Jordan it is a carrier. Zain, Orange and Umniah put thousands of subscribers
/// behind a single IPv4 address, so ten sign-in attempts per quarter hour was ten attempts shared by
/// an entire network — the eleventh customer to open the app was locked out by ten strangers.
///
/// Keying on (address, account) fixes that without weakening anything: brute-forcing one account
/// still hits a tight limit, and one carrier's customers no longer collide because they are signing
/// in to different accounts.
///
/// The address stays in the key deliberately. Dropping it would let one attacker spread attempts on
/// one account across many addresses; keeping it means the pair has to repeat before anything is
/// refused.
///
/// This runs BEFORE the rate limiter, which is before model binding, so the body is read by hand and
/// rewound. Only the SHA-256 prefix is kept: the limiter holds partition keys in memory for the
/// length of a window, and an email address is not something to leave lying in them.
/// </remarks>
public static class CredentialSubject
{
    private const string ItemKey = "khadra.credential-subject";

    /// <summary>Bodies larger than this are not credentials, and are not read.</summary>
    private const int MaximumBodyBytes = 8 * 1024;

    /// <summary>
    /// Reads the subject out of an auth request body and stashes it for the limiter.
    /// </summary>
    public static IApplicationBuilder UseCredentialSubject(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            if (ShouldInspect(context))
                context.Items[ItemKey] = await ReadSubjectAsync(context);

            await next(context);
        });
    }

    /// <summary>
    /// The partition key for an auth endpoint: the caller's address and, when the request names one,
    /// the account it concerns.
    /// </summary>
    public static string PartitionKey(HttpContext context, string address)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(ItemKey, out var subject) && subject is string named
            ? $"{address}|{named}"
            : address;
    }

    private static bool ShouldInspect(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
            return false;
        if (!context.Request.Path.StartsWithSegments("/api/v1/auth", StringComparison.OrdinalIgnoreCase))
            return false;

        var contentType = context.Request.ContentType;
        return contentType is not null
            && contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            && context.Request.ContentLength is > 0 and <= MaximumBodyBytes;
    }

    private static async Task<string?> ReadSubjectAsync(HttpContext context)
    {
        // Buffering makes the stream seekable so model binding can read it again afterwards.
        context.Request.EnableBuffering();

        var length = (int)(context.Request.ContentLength ?? 0);
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var read = await context.Request.Body.ReadAtLeastAsync(
                buffer.AsMemory(0, length), length, throwOnEndOfStream: false);
            context.Request.Body.Position = 0;

            return Fingerprint(EmailIn(buffer.AsSpan(0, read)));
        }
        catch (JsonException)
        {
            // Malformed JSON is the model binder's problem to report, not this middleware's.
            context.Request.Body.Position = 0;
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string? EmailIn(ReadOnlySpan<byte> body)
    {
        var reader = new Utf8JsonReader(body);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return null;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var isEmail = reader.ValueTextEquals("email") || reader.ValueTextEquals("Email");
            if (!reader.Read())
                return null;
            if (isEmail && reader.TokenType == JsonTokenType.String)
                return reader.GetString();
            // Skip whatever this property's value was, nested objects included.
            reader.TrySkip();
        }

        return null;
    }

    private static string? Fingerprint(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        // Normalised the same way the account lookup normalises it, so "A@x.com " and "a@x.com"
        // share a bucket rather than granting an attacker two.
        var normalised = email.Trim().ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalised));
        return Convert.ToHexString(hash.AsSpan(0, 8));
    }
}
