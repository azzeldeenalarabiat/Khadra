using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.IdentityAccess;

// Stored in E.164. Jordanian local forms are normalised; tourists may register with any country code.
public sealed partial class PhoneNumber : ValueObject
{
    public const int MaxLength = 20;
    private const string JordanCountryCode = "+962";

    public string Value { get; }

    private PhoneNumber(string value)
    {
        Value = value;
    }

    public bool IsJordanian => Value.StartsWith(JordanCountryCode, StringComparison.Ordinal);

    public static Result<PhoneNumber, Error> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return IdentityErrors.InvalidPhone;

        // Keep digits and a leading '+'; drop spaces, dashes, dots and parentheses.
        var compact = new string(raw.Where(character => char.IsAsciiDigit(character) || character == '+').ToArray());

        if (compact.StartsWith("00", StringComparison.Ordinal))
            compact = "+" + compact[2..];
        else if (compact.Length == 10 && compact.StartsWith("07", StringComparison.Ordinal))
            compact = JordanCountryCode + compact[1..];
        else if (compact.Length == 12 && compact.StartsWith("9627", StringComparison.Ordinal))
            compact = "+" + compact;

        if (!E164Pattern().IsMatch(compact))
            return IdentityErrors.InvalidPhone;

        if (compact.StartsWith(JordanCountryCode, StringComparison.Ordinal) && !JordanMobilePattern().IsMatch(compact))
            return IdentityErrors.InvalidPhone;

        return new PhoneNumber(compact);
    }

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164Pattern();

    // Jordanian mobiles: +962 7[7|8|9] XXXXXXX.
    [GeneratedRegex(@"^\+9627[789]\d{7}$")]
    private static partial Regex JordanMobilePattern();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
