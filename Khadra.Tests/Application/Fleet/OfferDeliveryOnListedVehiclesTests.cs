using Khadra.Application.Common;
using Khadra.Application.Fleet.ManageVehicles;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Fleet;
using Khadra.Domain.Fleet.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Fleet;

/// <summary>
/// Offering delivery on cars that were listed before the gallery offered it.
/// </summary>
/// <remarks>
/// Pre-launch item 75. A car takes its delivery flag from whether the dealership offered delivery AT
/// THE MOMENT THE CAR WAS SAVED, so a gallery that lists its fleet first and turns delivery on
/// afterwards advertises a service none of its cars provides.
///
/// The per-car flag stays, and the tests below are mostly about what this does NOT do to it.
/// </remarks>
public sealed class OfferDeliveryOnListedVehiclesTests
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly Id OwnerId = Id.New();

    private sealed class Context
    {
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public IVehicleRepository Vehicles { get; } = Substitute.For<IVehicleRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public Dealer Dealer { get; }

        public Context(bool delivers = true, bool approved = true, bool suspended = false)
        {
            Dealer = approved ? Build.ApprovedDealer(Now, ownerUserId: OwnerId) : Build.Dealer(Now, OwnerId);
            if (delivers)
                Dealer.EnableDelivery(15m, Money.Jod(5m), Now);
            if (suspended)
                Dealer.Suspend(Id.New(), "Under investigation.", Now);

            Dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(Dealer);
            Vehicles.ListPublishedNotDeliveryEligibleAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>())
                .Returns([]);
        }

        public void GivenPending(params Vehicle[] vehicles) =>
            Vehicles.ListPublishedNotDeliveryEligibleAsync(Dealer.Id, Arg.Any<CancellationToken>())
                .Returns(vehicles);

        public OfferDeliveryOnListedVehiclesHandler Handler() => new(Dealers, Vehicles, UnitOfWork);
    }

    private static Vehicle Listed(Id dealerId) =>
        Build.Vehicle(dealerId: dealerId, isDeliveryEligible: false);

    [Fact]
    public async Task Every_listed_car_that_was_not_offered_now_is()
    {
        var context = new Context();
        var first = Build.Vehicle(dealerId: context.Dealer.Id, isDeliveryEligible: false, plateNumber: "11-11111");
        var second = Build.Vehicle(dealerId: context.Dealer.Id, isDeliveryEligible: false, plateNumber: "22-22222");
        context.GivenPending(first, second);

        var result = await context.Handler().Handle(
            new OfferDeliveryOnListedVehiclesCommand(OwnerId), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(2, result.Value.Updated);
        Assert.True(first.IsDeliveryEligible);
        Assert.True(second.IsDeliveryEligible);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Nothing to do is a success that changed nothing, not an error and not a save.</summary>
    [Fact]
    public async Task A_fleet_that_already_delivers_is_left_alone()
    {
        var context = new Context();

        var result = await context.Handler().Handle(
            new OfferDeliveryOnListedVehiclesCommand(OwnerId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Updated);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A gallery that does not deliver cannot offer delivery on its cars.
    /// </summary>
    /// <remarks>
    /// Refused rather than quietly doing nothing: the screen cannot reach this state, and if it ever
    /// does, the answer that helps is "switch delivery on first".
    /// </remarks>
    [Fact]
    public async Task A_gallery_that_does_not_deliver_is_refused()
    {
        var context = new Context(delivers: false);
        context.GivenPending(Listed(context.Dealer.Id));

        var result = await context.Handler().Handle(
            new OfferDeliveryOnListedVehiclesCommand(OwnerId), CancellationToken.None);

        Assert.Equal("dealer.delivery_not_offered", result.Error.Code);
    }

    [Fact]
    public async Task A_suspended_dealership_cannot_change_its_fleet()
    {
        var context = new Context(suspended: true);
        context.GivenPending(Listed(context.Dealer.Id));

        var result = await context.Handler().Handle(
            new OfferDeliveryOnListedVehiclesCommand(OwnerId), CancellationToken.None);

        Assert.True(result.IsFailure);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// By OWNER, like every other change to what the business offers. An employee has no dealership
    /// by this route, and gets the same answer somebody with no dealership at all gets.
    /// </summary>
    [Fact]
    public async Task Somebody_who_is_not_the_owner_gets_nothing()
    {
        var context = new Context();
        context.GivenPending(Listed(context.Dealer.Id));

        var result = await context.Handler().Handle(
            new OfferDeliveryOnListedVehiclesCommand(Id.New()), CancellationToken.None);

        Assert.Equal("dealer.not_registered", result.Error.Code);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
