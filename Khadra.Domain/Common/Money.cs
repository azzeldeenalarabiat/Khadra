namespace Khadra.Domain.Common;

public sealed class Money : ValueObject
{
    public const string JordanianDinar = "JOD";

    public decimal Amount { get; }
    public string CurrencyCode { get; }

    private Money(decimal amount, string currencyCode)
    {
        if (amount < 0)
            throw new DomainException("Money amount cannot be negative.");
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyCode);
        if (currencyCode.Length != 3)
            throw new DomainException("Currency code must be an ISO 4217 three-letter code.");

        // Persisted precision is (18,3): JOD has three minor units (fils). Round once at the boundary.
        Amount = decimal.Round(amount, 3, MidpointRounding.ToEven);
        CurrencyCode = currencyCode.ToUpperInvariant();
    }

    public static Money Create(decimal amount, string currencyCode) => new(amount, currencyCode);

    public static Money Jod(decimal amount) => new(amount, JordanianDinar);

    public static Money ZeroIn(string currencyCode) => new(0m, currencyCode);

    public bool IsZero => Amount == 0m;

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, CurrencyCode);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, CurrencyCode);
    }

    public Money MultiplyBy(int quantity)
    {
        if (quantity < 0)
            throw new DomainException("Quantity cannot be negative.");
        return new Money(Amount * quantity, CurrencyCode);
    }

    // Percentages (commission, deposit, penalties) always come from configured business rules.
    public Money Percentage(decimal percent)
    {
        if (percent < 0)
            throw new DomainException("Percentage cannot be negative.");
        return new Money(Amount * percent / 100m, CurrencyCode);
    }

    public bool IsGreaterThan(Money other)
    {
        EnsureSameCurrency(other);
        return Amount > other.Amount;
    }

    private void EnsureSameCurrency(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!string.Equals(CurrencyCode, other.CurrencyCode, StringComparison.Ordinal))
            throw new DomainException($"Currency mismatch: cannot combine {CurrencyCode} with {other.CurrencyCode}.");
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return CurrencyCode;
    }

    public override string ToString() => $"{Amount:0.000} {CurrencyCode}";
}
