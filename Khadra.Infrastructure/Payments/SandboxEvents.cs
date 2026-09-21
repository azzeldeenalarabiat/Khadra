using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Khadra.Domain.Common;

namespace Khadra.Infrastructure.Payments;

/// <summary>
/// The wire format a sandbox delivery travels in, and the one place it is written or signed.
/// </summary>
/// <remarks>
/// <para>
/// Separated from <see cref="SandboxPaymentProvider"/> — which stays <c>internal</c>, like every
/// other infrastructure implementation — because two things need this format and only one of them is
/// the adapter. The other is the checkout console in <c>Khadra.WebAPI</c>, which signs the body the
/// tester's browser then posts to the real webhook. The ADAPTER is infrastructure; the FORMAT is a
/// contract between two assemblies, so it is public and the adapter is not.
/// </para>
/// <para>
/// <b>One implementation of the signature, and one of the casing.</b> The provider both writes these
/// bodies and verifies them, so the two halves can drift apart while each looks correct alone — and
/// they did: camelCase out, case-sensitive PascalCase in, every body refused with a signature that
/// verified perfectly. Both halves now go through this class, so there is nothing left to drift.
/// </para>
/// </remarks>
public static class SandboxEvents
{
    /// <summary>The header a sandbox delivery carries its signature in.</summary>
    public const string SignatureHeader = "x-khadra-sandbox-signature";

    /// <summary>Where the sandbox checkout console is served, relative to the API's own root.</summary>
    /// <remarks>
    /// Shared between the page and the link to it, so the URL a customer is handed and the route that
    /// answers it cannot drift apart.
    /// </remarks>
    public const string ConsolePath = "/sandbox-checkout";

    /// <summary>
    /// Minor units to the dinar. JOD has three — fils — which is one of the things this fake guesses.
    /// </summary>
    /// <remarks>
    /// A real adapter reads the exponent from the provider's own currency table rather than assuming
    /// the platform's currency. Hard-coded here because the sandbox only ever quotes back an amount
    /// this platform sent it, and because the day a real adapter lands, all of this is deleted.
    /// </remarks>
    public const decimal MinorUnitsPerDinar = 1000m;

    /// <summary>
    /// ONE serialiser setting, shared by the two halves. Not two that happen to agree.
    /// </summary>
    /// <remarks>
    /// Default <c>JsonSerializer</c> options are case-SENSITIVE, so a camelCase body binds to nothing
    /// against PascalCase properties: every field arrives null, and a delivery is refused with a 401
    /// that says nothing about casing. Web defaults, because that is what the rest of the API speaks.
    /// </remarks>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The body shape a sandbox delivery carries.</summary>
    internal sealed record Body
    {
        public string? EventId { get; init; }
        public string? Reference { get; init; }
        public string? Kind { get; init; }
        public long? AmountMinor { get; init; }
        public string? Currency { get; init; }
        public string? FailureCode { get; init; }
        public DateTimeOffset? OccurredAt { get; init; }
    }

    /// <summary>Signs a body the way this provider expects to receive it.</summary>
    /// <remarks>
    /// HMAC-SHA256 over the exact UTF-8 bytes, hex, lower case. Copied from the shape a real adapter
    /// has, because this file is what somebody will read when they write the MEPS or HyperPay one.
    /// </remarks>
    public static string Sign(string rawBody, string secret)
    {
        using var mac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(mac.ComputeHash(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
    }

    /// <summary>Builds a signed body, for the checkout console and for tests.</summary>
    /// <remarks>
    /// <paramref name="eventId"/> is the provider's id for THIS DELIVERY, so passing the same one
    /// twice is a replay — which is how the console's "deliver that again" button works, with no
    /// code behind it.
    /// </remarks>
    public static (string Body, string Signature) Build(
        string eventId,
        string reference,
        string kind,
        Money? amount,
        string secret,
        string? failureCode = null,
        DateTimeOffset? occurredAt = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["eventId"] = eventId,
            ["reference"] = reference,
            ["kind"] = kind,
            // Minor units, as every real provider sends them. The conversion is the thing most worth
            // exercising, because a factor of a thousand here is a paid booking that gets refunded.
            ["amountMinor"] = amount is null
                ? null
                : (long)Math.Round(amount.Amount * MinorUnitsPerDinar, MidpointRounding.AwayFromZero),
            ["currency"] = amount?.CurrencyCode,
            ["failureCode"] = failureCode,
            ["occurredAt"] = occurredAt?.ToString("O", CultureInfo.InvariantCulture),
        };

        var body = JsonSerializer.Serialize(payload, Json);
        return (body, Sign(body, secret));
    }
}
