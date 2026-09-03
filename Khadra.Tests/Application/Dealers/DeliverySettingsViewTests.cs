using Khadra.Application.Dealers;
using Khadra.Application.Dealers.UpdateDeliverySettings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Dealers;

// Spec 4.4: the delivery page shows the dealer's own switch and radius beside the fee the PLATFORM
// charges. The fee comes from the business rules, never from the dealer and never from a constant.
public sealed class DeliverySettingsViewTests
{
    private static readonly Id OwnerId = Id.New();

    [Fact]
    public async Task Owner_sees_their_radius_and_the_platform_fee()
    {
        var dealers = Substitute.For<IDealerRepository>();
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        dealer.EnableDelivery(12.5m, Build.Now);
        dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(dealer);
        var handler = new GetMyDeliverySettingsHandler(new DealerMembershipResolver(dealers), TestBusinessRules.Provider());

        var result = await handler.Handle(new GetMyDeliverySettingsQuery(OwnerId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsEnabled);
        Assert.Equal(12.5m, result.Value.RadiusKm);
        Assert.Equal(200m, result.Value.MaxRadiusKm);
        var fee = TestBusinessRules.Values().DeliveryFee;
        Assert.Equal(fee.Amount, result.Value.PlatformDeliveryFee.Amount);
        Assert.Equal(fee.CurrencyCode, result.Value.PlatformDeliveryFee.Currency);
    }

    [Fact]
    public async Task Someone_with_no_dealership_gets_not_registered()
    {
        var dealers = Substitute.For<IDealerRepository>();
        var handler = new GetMyDeliverySettingsHandler(new DealerMembershipResolver(dealers), TestBusinessRules.Provider());

        var result = await handler.Handle(new GetMyDeliverySettingsQuery(Id.New()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("dealer.not_registered", result.Error.Code);
    }
}
