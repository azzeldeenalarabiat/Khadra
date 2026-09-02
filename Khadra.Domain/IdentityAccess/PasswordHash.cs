using Khadra.Domain.Common;

namespace Khadra.Domain.IdentityAccess;

// Opaque hash produced by the application's IPasswordHasher (BCrypt). The domain never sees plaintext.
public sealed class PasswordHash : ValueObject
{
    public const int MaxLength = 100;

    public string Value { get; }

    private PasswordHash(string value)
    {
        Value = value;
    }

    public static PasswordHash FromHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Length > MaxLength)
            throw new DomainException("A password hash is required.");

        return new PasswordHash(hash);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => "[password hash]";
}
