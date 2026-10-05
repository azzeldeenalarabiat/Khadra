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
    /// <remarks>
    /// The amount is written at the currency's full scale (1.500, not 1.5). A value EF materialises
    /// skips <see cref="Money"/>'s constructor and keeps whatever scale it was stored with -- a dispute
    /// decided before 2026-10-05 holds 1.5 in its JSON -- so the wire form is fixed here as well. The
    /// number is the same; only its written form is (E2E F36).
    /// </remarks>
    public static MoneyDto From(Money money)
    {
        ArgumentNullException.ThrowIfNull(money);
        return new MoneyDto(Money.AtScale(money.Amount), money.CurrencyCode);
    }

    public static MoneyDto? FromOptional(Money? money) => money is null ? null : From(money);
}
