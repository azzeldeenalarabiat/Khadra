using Khadra.Domain.Common;

namespace Khadra.Application.Common.Dtos;

/// <summary>
/// Every money value travels with its currency; the console never assumes JOD.
///
/// One definition for the whole Application layer. Fleet and the admin dashboard each grew their own
/// identical copy, and a third context about to serialise money is the moment to stop that: two
/// records with the same name in different namespaces are a using-directive away from a confusing
/// compile error, and a wire shape should have exactly one owner.
/// </summary>
public sealed record MoneyDto(decimal Amount, string Currency)
{
    public static MoneyDto From(Money money)
    {
        ArgumentNullException.ThrowIfNull(money);
        return new MoneyDto(money.Amount, money.CurrencyCode);
    }

    public static MoneyDto? FromOptional(Money? money) => money is null ? null : From(money);
}
