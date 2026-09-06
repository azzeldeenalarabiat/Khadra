using System.Text.Json;
using Khadra.Application.PlatformSettings.GetBusinessRules;
using Khadra.Tests.Support;

namespace Khadra.Tests.Application.Common;

/// <summary>
/// The platform settings screen, and the money that is no longer on it.
///
/// This endpoint used to serialise the domain's `BusinessRules` snapshot straight over the wire, so
/// its one money value — the delivery fee — went out as `{ amount, currencyCode }` while every other
/// endpoint maps money through `MoneyDto` as `{ amount, currency }`. The console reads `currency`,
/// so the screen showed "Delivery fee 10 undefined".
///
/// The fee has since moved onto the dealership that performs the delivery, so it is gone from here
/// entirely and the shape guarantee travelled with it (see `DeliverySettingsViewTests`). What is
/// left to protect is that this endpoint no longer claims a platform-wide delivery price, and that
/// every number it does serve arrives intact.
/// </summary>
public sealed class BusinessRulesViewTests
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// There is no single delivery fee to print any more: each gallery sets its own. A row here
    /// would have to invent a figure, or pick one gallery's and call it the platform's.
    /// </summary>
    [Fact]
    public void The_platform_no_longer_states_a_delivery_fee()
    {
        var view = new BusinessRulesView(
            BusinessRulesDto.From(TestBusinessRules.Values()), "Configuration", IsEditable: false);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(view, WireOptions));
        var rules = json.RootElement.GetProperty("rules");

        Assert.False(rules.TryGetProperty("deliveryFee", out _));
    }

    [Fact]
    public void Every_configured_number_reaches_the_screen()
    {
        var rules = TestBusinessRules.Values();

        var dto = BusinessRulesDto.From(rules);

        // Mapping by hand is where a field goes missing silently, and the screen would then simply
        // not draw that row rather than fail.
        Assert.Equal(rules.CommissionPercent, dto.CommissionPercent);
        Assert.Equal(rules.DepositPercent, dto.DepositPercent);
        Assert.Equal(rules.NoShowTimeoutHours, dto.NoShowTimeoutHours);
        Assert.Equal(rules.DealerNonDeliveryPenaltyMinPercent, dto.DealerNonDeliveryPenaltyMinPercent);
        Assert.Equal(rules.DealerNonDeliveryPenaltyMaxPercent, dto.DealerNonDeliveryPenaltyMaxPercent);
        Assert.Equal(rules.FreeCancellationWindowMinutes, dto.FreeCancellationWindowMinutes);
        Assert.Equal(rules.AdminSlaHours, dto.AdminSlaHours);
        Assert.Equal(rules.CustomerCancellationPenaltyPercent, dto.CustomerCancellationPenaltyPercent);
        Assert.Equal(rules.PaymentWindowMinutes, dto.PaymentWindowMinutes);
        Assert.Equal(rules.PostReturnSettlementHours, dto.PostReturnSettlementHours);
        Assert.Equal(rules.MinimumRenterAge, dto.MinimumRenterAge);
        Assert.Equal(rules.EarliestVehicleModelYear, dto.EarliestVehicleModelYear);
    }

    /// <summary>A null minimum age is a real shipping state, not a missing value.</summary>
    [Fact]
    public void An_unset_minimum_age_survives_the_mapping_as_null()
    {
        var dto = BusinessRulesDto.From(TestBusinessRules.Values(minimumRenterAge: null));

        Assert.Null(dto.MinimumRenterAge);
    }
}
