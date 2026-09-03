using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Documents;

/// <summary>
/// Signs short-lived upload tickets.
///
/// The key is derived from the JWT signing key by HKDF with its OWN info string, so an upload ticket
/// and a document-read link are cryptographically distinct: neither can be replayed as the other,
/// even though both come from one configured secret.
/// </summary>
internal sealed class HmacUploadTicketService : IUploadTicketService
{
    private const string UploadPath = "/api/v1/uploads";
    private static readonly byte[] DerivationInfo = "khadra:upload-ticket:v1"u8.ToArray();

    private readonly byte[] _key;
    private readonly TimeSpan _lifetime;

    public HmacUploadTicketService(IOptions<JwtOptions> jwt, IDocumentPolicySettings policy)
    {
        ArgumentNullException.ThrowIfNull(jwt);
        ArgumentNullException.ThrowIfNull(policy);

        _key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(jwt.Value.SigningKey ?? string.Empty),
            outputLength: 32,
            info: DerivationInfo);
        // An upload window is longer than a read link: someone on a phone is choosing a photo.
        _lifetime = policy.LinkLifetime + TimeSpan.FromMinutes(10);
    }

    public UploadTicket Issue(string storageKey, string contentType, DateTimeOffset now)
    {
        var expiresAt = now.Add(_lifetime);
        var payload = Payload(storageKey, contentType, expiresAt.ToUnixTimeSeconds());
        var token = $"{Base64UrlEncode(Encoding.UTF8.GetBytes(payload))}.{Sign(payload)}";

        return new UploadTicket($"{UploadPath}/{token}", storageKey, expiresAt);
    }

    public bool TryRedeem(string token, DateTimeOffset now, out RedeemedUpload upload)
    {
        upload = new RedeemedUpload(string.Empty, string.Empty);
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var separator = token.LastIndexOf('.');
        if (separator <= 0 || separator == token.Length - 1)
            return false;

        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(Base64UrlDecode(token[..separator]));
        }
        catch (FormatException)
        {
            return false;
        }

        // Fixed-time comparison: anything shorter leaks the signature a byte at a time.
        var expected = Encoding.ASCII.GetBytes(Sign(payload));
        var supplied = Encoding.ASCII.GetBytes(token[(separator + 1)..]);
        if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
            return false;

        var parts = payload.Split('|');
        if (parts.Length != 3)
            return false;
        if (!long.TryParse(parts[2], CultureInfo.InvariantCulture, out var expires))
            return false;
        if (DateTimeOffset.FromUnixTimeSeconds(expires) <= now)
            return false;

        upload = new RedeemedUpload(parts[0], parts[1]);
        return true;
    }

    private static string Payload(string storageKey, string contentType, long expires) =>
        $"{storageKey}|{contentType}|{expires.ToString(CultureInfo.InvariantCulture)}";

    private string Sign(string payload) =>
        Base64UrlEncode(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload)));

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}
