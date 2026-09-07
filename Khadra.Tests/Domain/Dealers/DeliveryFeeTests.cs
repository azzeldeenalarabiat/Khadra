using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Events;
using Khadra.Domain.Fleet;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Dealers;

/// <summary>
/// The delivery fee is the gallery's, not the platform's.
///
/// It used to be one configured figure — 10 JOD — applied to every delivery booking on every
/// dealership. The owner moved it onto the business that performs the delivery, which is where the
/// money was already going: `BookingPricing` leaves the fee out of the deposit base, so it never
/// touches platform commission, and puts it in `BalanceDue` for the driver to collect in cash.
/// </summary>
public sealed class DeliveryFeeTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    [Fact]
    public void A_gallery_sets_its_own_price_and_it_is_the_one_stored()
    {
        var dealer = Build.ApprovedDealer();

        var result = dealer.EnableDelivery(30m, Money.Jod(12.5m), Now);

        Assert.True(result.IsSuccess);
        Assert.True(dealer.Delivery.IsEnabled);
        // 12.5 is nobody's default; it is the figure this gallery chose.
        Assert.Equal(Money.Jod(12.5m), dealer.Delivery.Fee);
    }

    /// <summary>
    /// Free delivery is a real offer, not a missing answer. Refusing zero would be the platform
    /// pricing on the gallery's behalf again, in the opposite direction.
    /// </summary>
    [Fact]
    public void A_gallery_may_deliver_free_of_charge()
    {
        var dealer = Build.ApprovedDealer();

        var result = dealer.EnableDelivery(30m, Money.Jod(0m), Now);

        Assert.True(result.IsSuccess);
        Assert.True(dealer.Delivery.Fee!.IsZero);
    }

    [Fact]
    public void An_implausible_price_is_refused_as_a_typo()
    {
        var dealer = Build.ApprovedDealer();

        var result = dealer.EnableDelivery(30m, Money.Jod(DeliverySettings.MaxFee + 1m), Now);

        Assert.True(result.IsFailure);
        Assert.Equal("dealer.invalid_delivery_fee", result.Error.Code);
    }

    /// <summary>
    /// Switching delivery off clears the price rather than remembering it. A gallery coming back to
    /// delivery is quoting again, and a stale figure silently restored is a price nobody chose.
    /// </summary>
    [Fact]
    public void Turning_delivery_off_leaves_no_price_behind()
    {
        var dealer = Build.ApprovedDealer();
        dealer.EnableDelivery(30m, Money.Jod(12.5m), Now);

        dealer.DisableDelivery(Now);

        Assert.False(dealer.Delivery.IsEnabled);
        Assert.Null(dealer.Delivery.Fee);
    }

    [Fact]
    public void Changing_the_price_is_announced_with_the_new_amount_on_it()
    {
        var dealer = Build.ApprovedDealer();

        dealer.EnableDelivery(30m, Money.Jod(12.5m), Now);

        // A price change with nothing recording what it changed to is not much of a record.
        var raised = Assert.IsType<DealerDeliveryChanged>(Assert.Single(dealer.DomainEvents));
        Assert.True(raised.IsEnabled);
        Assert.Equal(Money.Jod(12.5m), raised.Fee);
    }

    /// <summary>
    /// A booking freezes the fee it was made under, so a gallery raising its price tomorrow can
    /// never re-price a booking taken today. The value object already carried the fee; this pins
    /// the guarantee now that the figure behind it can move.
    /// </summary>
    [Fact]
    public void A_booking_keeps_the_price_it_was_made_under_when_the_gallery_changes_it()
    {
        var dealer = Build.ApprovedDealer();
        dealer.EnableDelivery(30m, Money.Jod(10m), Now);

        var pricing = BookingPricing.Calculate(
            Money.Jod(30m),
            new DateOnly(2026, 9, 10),
            new DateOnly(2026, 9, 13),
            deliveryFee: dealer.Delivery.Fee!,
            depositPercent: Percentage.Create(20m).Value,
            securityDeposit: Money.Jod(150m),
            MileagePolicy.Unlimited(),
            FuelPolicy.FullToFull).Value;

        dealer.EnableDelivery(30m, Money.Jod(25m), Now.AddDays(1));

        Assert.Equal(Money.Jod(10m), pricing.DeliveryFee);
        Assert.Equal(Money.Jod(25m), dealer.Delivery.Fee);
        // And the total the customer agreed to is still built from the frozen figure.
        Assert.Equal(Money.Jod(100m), pricing.TotalPrice);
    }

    /// <summary>
    /// The fee sits outside the deposit, which is why the platform earns no commission on it and the
    /// gallery keeps all of it. Pinned because it is the reason the price is theirs to set.
    /// </summary>
    [Fact]
    public void The_fee_is_outside_the_deposit_and_collected_in_cash()
    {
        var pricing = BookingPricing.Calculate(
            Money.Jod(30m),
            new DateOnly(2026, 9, 10),
            new DateOnly(2026, 9, 13),
            deliveryFee: Money.Jod(10m),
            depositPercent: Percentage.Create(20m).Value,
            securityDeposit: Money.Jod(150m),
            MileagePolicy.Unlimited(),
            FuelPolicy.FullToFull).Value;

        // 20% of the 90 rental, not of the 100 total: the fee is not part of the deposit base.
        Assert.Equal(Money.Jod(18m), pricing.DepositAmount);
        // And it is in the cash balance the driver collects: 90 - 18 + 10.
        Assert.Equal(Money.Jod(82m), pricing.BalanceDue);
    }
}
