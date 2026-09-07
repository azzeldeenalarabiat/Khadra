using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.IdentityAccess;

public sealed partial class PersonName : ValueObject
{
    public const int MinLength = 2;
    public const int MaxLength = 150;

    public string Value { get; }

    private PersonName(string value)
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
    public static PersonName FromPersisted(string value) => new(value);

    public static Result<PersonName, Error> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return IdentityErrors.InvalidName;

        var normalized = WhitespaceRun().Replace(raw.Trim(), " ");
        if (normalized.Length is < MinLength or > MaxLength || normalized.Any(char.IsControl))
            return IdentityErrors.InvalidName;

        return new PersonName(normalized);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
