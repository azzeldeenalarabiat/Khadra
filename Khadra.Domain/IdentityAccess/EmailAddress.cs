using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.IdentityAccess;

public sealed partial class EmailAddress : ValueObject
{
    public const int MaxLength = 256;

    public string Value { get; }

    private EmailAddress(string value)
    {
        Value = value;
    }

    // Normalises (trim + lower-case) so uniqueness and lookups are case-insensitive.
    public static Result<EmailAddress, Error> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return IdentityErrors.InvalidEmail;

        var normalized = raw.Trim().ToLowerInvariant();
        if (normalized.Length > MaxLength || !EmailPattern().IsMatch(normalized))
            return IdentityErrors.InvalidEmail;

        return new EmailAddress(normalized);
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$")]
    private static partial Regex EmailPattern();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
