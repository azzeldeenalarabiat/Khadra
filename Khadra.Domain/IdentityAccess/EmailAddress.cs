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

    /// <summary>
    /// A value already in the database, taken as-is.
    /// </summary>
    /// <remarks>
    /// Reading a row is not the moment to re-litigate whether it should have been allowed in. The EF
    /// converters used to rebuild these through <c>Create(...).Value</c>, and <c>.Value</c> on a
    /// failed result THROWS — so the day a rule is tightened in a way some stored row no longer
    /// satisfies, that row stops being readable at all. Not a validation error the caller could
    /// handle: an exception on load, for every query that touches the aggregate.
    ///
    /// Writes still go through <see cref="Create"/>, which is where the rule belongs.
    /// </remarks>
    public static EmailAddress FromPersisted(string value) => new(value);

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
