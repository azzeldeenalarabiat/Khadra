using System.Security.Cryptography;
using System.Text;
using Khadra.Application.Common.Ports;

namespace Khadra.Infrastructure.Security;

// 256 random bits, base64url for transport; SHA-256 hex (64 chars) at rest.
internal sealed class OpaqueTokenService : IOpaqueTokenService
{
    private const int TokenBytes = 32;

    public GeneratedOpaqueToken Generate()
    {
        var value = Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));
        return new GeneratedOpaqueToken(value, Hash(value));
    }

    public string Hash(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
