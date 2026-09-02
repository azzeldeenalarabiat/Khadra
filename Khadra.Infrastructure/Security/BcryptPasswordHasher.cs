using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Security;

internal sealed class BcryptPasswordHasher : IPasswordHasher
{
    private readonly int _workFactor;

    public BcryptPasswordHasher(IOptions<AuthOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _workFactor = options.Value.BcryptWorkFactor;
        DummyHash = BCrypt.Net.BCrypt.EnhancedHashPassword(Guid.NewGuid().ToString("N"), _workFactor);
    }

    public string DummyHash { get; }

    // Enhanced mode pre-hashes with SHA-384, which removes bcrypt's 72-byte truncation concern.
    public string Hash(string password) => BCrypt.Net.BCrypt.EnhancedHashPassword(password, _workFactor);

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
            return false;

        try
        {
            return BCrypt.Net.BCrypt.EnhancedVerify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
