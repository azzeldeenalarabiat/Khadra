using System.Text.Json;
using Khadra.Application.Dealers;
using Khadra.Application.Dealers.UpdateDeliverySettings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Dealers;

// Spec 4.4: the delivery page shows the dealership's own switch, radius and price.
//
// The fee used to be the platform's — one configured figure served to every gallery from
// IBusinessRulesProvider. The owner moved it onto the business that performs the delivery, so this
// handler no longer reads the business rules at all, and the number on the screen is the gallery's.
public sealed class DeliverySettingsViewTests
{
    private static readonly Id OwnerId = Id.New();

    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    private static GetMyDeliverySettingsHandler Handler(IDealerRepository dealers) =>
        new(new DealerMembershipResolver(dealers));

    [Fact]
    public async Task Owner_sees_their_own_radius_and_their_own_fee()
    {
        var dealers = Substitute.For<IDealerRepository>();
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        dealer.EnableDelivery(12.5m, Money.Jod(7.5m), Build.Now);
        dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(dealer);

        var result = await Handler(dealers).Handle(new GetMyDeliverySettingsQuery(OwnerId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsEnabled);
        Assert.Equal(12.5m, result.Value.RadiusKm);
        Assert.Equal(200m, result.Value.MaxRadiusKm);
        // The gallery's figure, not a platform one — 7.5, which no configuration anywhere holds.
        Assert.Equal(7.5m, result.Value.Fee!.Amount);
        Assert.Equal("JOD", result.Value.Fee.Currency);
        Assert.Equal(1000m, result.Value.MaxFee);
    }

    /// <summary>
    /// A gallery that does not deliver has no price, and the screen is told so rather than being
    /// handed a zero it would have to render as a real offer of free delivery.
    /// </summary>
    [Fact]
    public async Task A_dealership_that_does_not_deliver_has_no_fee_at_all()
    {
        var dealers = Substitute.For<IDealerRepository>();
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(dealer);

        var result = await Handler(dealers).Handle(new GetMyDeliverySettingsQuery(OwnerId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsEnabled);
        Assert.Null(result.Value.Fee);
    }

    /// <summary>
    /// The money leaves this endpoint with its currency, under the name every other endpoint uses.
    ///
    /// Inherited from the platform settings screen, where the same money once went out as
    /// `currencyCode` while the console read `currency` and printed “10 undefined”. The fee lives
    /// here now, so the guarantee does too, and it is asserted on the serialised JSON because the
    /// property NAME is the contract — a test against the C# type passed throughout that bug.
    /// </summary>
    [Fact]
    public async Task The_fee_travels_with_its_currency_under_the_name_the_console_reads()
    {
        var dealers = Substitute.For<IDealerRepository>();
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        dealer.EnableDelivery(12.5m, Money.Jod(7.5m), Build.Now);
        dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(dealer);

        var result = await Handler(dealers).Handle(new GetMyDeliverySettingsQuery(OwnerId), CancellationToken.None);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value, WireOptions));
        var fee = json.RootElement.GetProperty("fee");

        Assert.Equal("JOD", fee.GetProperty("currency").GetString());
        Assert.Equal(7.5m, fee.GetProperty("amount").GetDecimal());
        Assert.False(fee.TryGetProperty("currencyCode", out _));
    }

    [Fact]
    public async Task Someone_with_no_dealership_gets_not_registered()
    {
        var dealers = Substitute.For<IDealerRepository>();

        var result = await Handler(dealers).Handle(new GetMyDeliverySettingsQuery(Id.New()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("dealer.not_registered", result.Error.Code);
    }
}
