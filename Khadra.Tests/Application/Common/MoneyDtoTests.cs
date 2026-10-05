using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Khadra.Application.Common.Dtos;
using Khadra.Domain.Common;

namespace Khadra.Tests.Application.Common;

/// <summary>The wire form of money: every amount at the currency's full scale (E2E F36).</summary>
public sealed class MoneyDtoTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_typed_amount_goes_on_the_wire_at_full_scale()
    {
        var json = JsonSerializer.Serialize(MoneyDto.From(Money.Jod(1.5m)), Web);

        Assert.Equal("""{"amount":1.500,"currency":"JOD"}""", json);
    }

    /// <summary>
    /// EF materialises a value object by writing its backing fields, skipping the constructor, so a value
    /// stored with its typed scale -- a dispute decided before 2026-10-05 keeps 1.5 in its JSON -- comes back
    /// as 1.5. The DTO pads it, so old rows are served like new ones.
    /// </summary>
    [Fact]
    public void A_value_read_back_with_a_shorter_scale_is_served_at_full_scale()
    {
        var stored = Materialised(1.5m, "JOD");
        Assert.Equal("1.5", stored.Amount.ToString(CultureInfo.InvariantCulture));

        var dto = MoneyDto.From(stored);

        Assert.Equal("1.500", dto.Amount.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(1.5m, dto.Amount);
    }

    // What EF does: the private parameterless constructor, then the backing fields.
    private static Money Materialised(decimal amount, string currency)
    {
        var money = (Money)Activator.CreateInstance(typeof(Money), nonPublic: true)!;
        typeof(Money).GetField("<Amount>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(money, amount);
        typeof(Money).GetField("<CurrencyCode>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(money, currency);
        return money;
    }
}
