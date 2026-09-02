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
