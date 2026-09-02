using CSharpFunctionalExtensions;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.Common;

// A 0-100 percentage. Commission, deposit and penalty rates are percentages that arrive from
// configuration, so they get a type that refuses nonsense rather than being passed around as a decimal.
public sealed class Percentage : ValueObject
{
    public static readonly Percentage Zero = new(0m);

    public decimal Value { get; }

    private Percentage(decimal value)
    {
        Value = value;
    }

    public static Result<Percentage, Error> Create(decimal value)
    {
        if (value is < 0m or > 100m)
            return Error.Validation("percentage.out_of_range", "A percentage must be between 0 and 100.");

        return new Percentage(decimal.Round(value, 4, MidpointRounding.ToEven));
    }

    // For configuration and tests that have already been validated at the boundary.
    public static Percentage FromValidated(decimal value) =>
        Create(value).GetValueOrThrow(error => new DomainException(error.Message));

    public bool IsZero => Value == 0m;

    // Rounds once, in Money, so the result matches what is stored and charged.
    public Money Of(Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);
        return amount.Percentage(Value);
    }

    public bool IsGreaterThan(Percentage other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Value > other.Value;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => $"{Value:0.##}%";
}

internal static class ResultExtensions
{
    // Small helper so factories can turn a validated Result into a value or a programming-error throw.
    public static T GetValueOrThrow<T>(this Result<T, Error> result, Func<Error, Exception> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onFailure);
        return result.IsSuccess ? result.Value : throw onFailure(result.Error);
    }
}
