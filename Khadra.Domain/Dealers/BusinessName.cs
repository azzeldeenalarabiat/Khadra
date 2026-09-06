using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.Dealers;

public sealed partial class BusinessName : ValueObject
{
    public const int MinLength = 2;
    public const int MaxLength = 150;

    public string Value { get; }

    private BusinessName(string value)
    {
        Value = value;
    }

    public static Result<BusinessName, Error> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DealerErrors.InvalidBusinessName;

        var normalized = WhitespaceRun().Replace(raw.Trim(), " ");
        if (normalized.Length is < MinLength or > MaxLength || normalized.Any(char.IsControl))
            return DealerErrors.InvalidBusinessName;

        return new BusinessName(normalized);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}

// Jordanian commercial registration number. Kept deliberately permissive on format (digits, 4-20)
// because the authoritative check is the Admin reading the uploaded certificate, not a regex.
public sealed class CommercialRegistrationNumber : ValueObject
{
    public const int MinLength = 4;
    public const int MaxLength = 20;

    public string Value { get; }

    private CommercialRegistrationNumber(string value)
    {
        Value = value;
    }

    public static Result<CommercialRegistrationNumber, Error> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DealerErrors.InvalidCommercialRegistration;

        // Separators are dropped because people type the number as it is printed. Anything else is
        // refused rather than deleted: silently turning "E2E20260906" into "220260906" changed the
        // licence of record, and this column is UNIQUE, so two different inputs could collide.
        var compact = DigitIdentifier.Normalise(raw);
        if (compact is null || compact.Length is < MinLength or > MaxLength)
            return DealerErrors.InvalidCommercialRegistration;

        return new CommercialRegistrationNumber(compact);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
