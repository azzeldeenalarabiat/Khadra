using System.Security.Cryptography;
using System.Text;
using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Documents;

/// <summary>
/// Short-lived signed links for private documents (spec 7).
///
/// The signing key is DERIVED from the JWT signing key with HKDF rather than being a second secret to
/// deploy. Two reasons: an operator cannot forget to set it, and the derivation keeps the two uses
/// cryptographically separate, so a signed document link can never be mistaken for a token or vice
/// versa. If document links ever need to be revocable independently of sessions, this becomes its own
/// configured key.
/// </summary>
internal sealed class HmacDocumentLinkSigner : IDocumentLinkSigner
{
    private const string LinkPath = "/api/v1/documents";
    private static readonly byte[] DerivationInfo = "khadra:document-link:v1"u8.ToArray();

    private readonly byte[] _key;
    private readonly TimeSpan _lifetime;

    public HmacDocumentLinkSigner(IOptions<JwtOptions> jwt, IDocumentPolicySettings policy)
    {
        ArgumentNullException.ThrowIfNull(jwt);
        ArgumentNullException.ThrowIfNull(policy);

        _key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(jwt.Value.SigningKey ?? string.Empty),
            outputLength: 32,
            info: DerivationInfo);
        _lifetime = policy.LinkLifetime;
    }

    public SignedDocumentLink Sign(string storageKey, DateTimeOffset now)
    {
        var expiresAt = now.Add(_lifetime);
        var expires = expiresAt.ToUnixTimeSeconds();
        var token = Base64UrlEncode(Encoding.UTF8.GetBytes(storageKey));
        var signature = Compute(storageKey, expires);

        return new SignedDocumentLink(
            $"{LinkPath}/{token}?expires={expires}&signature={signature}",
            expiresAt);
    }

    public bool IsValid(string storageKey, long expiresAtUnixSeconds, string signature, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return false;
        if (DateTimeOffset.FromUnixTimeSeconds(expiresAtUnixSeconds) <= now)
            return false;

        var expected = Compute(storageKey, expiresAtUnixSeconds);
        // Fixed-time comparison: a length-or-content shortcut here would leak the signature byte by byte.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(signature));
    }

    public bool TryDecodeToken(string token, out string storageKey)
    {
        storageKey = string.Empty;
        try
        {
            var padded = token.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
            storageKey = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            return storageKey.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private string Compute(string storageKey, long expires)
    {
        var payload = Encoding.UTF8.GetBytes($"{storageKey}|{expires}");
        return Base64UrlEncode(HMACSHA256.HashData(_key, payload));
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
