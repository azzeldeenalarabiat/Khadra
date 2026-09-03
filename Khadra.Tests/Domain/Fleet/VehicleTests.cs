using Khadra.Domain.Common;
using Khadra.Domain.Fleet;
using Khadra.Domain.Fleet.Events;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Fleet;

public sealed class VehicleTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    [Fact]
    public void A_new_vehicle_is_a_draft_and_is_not_bookable()
    {
        var vehicle = Build.Vehicle();

        Assert.Same(VehicleStatus.Draft, vehicle.Status);
        Assert.False(vehicle.IsBookable(dealerCanTrade: true));
    }

    [Fact]
    public void Publishing_needs_an_approved_dealer_and_at_least_one_photo()
    {
        var vehicle = Build.Vehicle();

        Assert.Equal("vehicle.dealer_not_approved", vehicle.Publish(dealerCanTrade: false, Now).Error.Code);
        Assert.Equal("vehicle.image_required", vehicle.Publish(dealerCanTrade: true, Now).Error.Code);

        vehicle.AddImage("cars/1.jpg", Now);
        Assert.True(vehicle.Publish(dealerCanTrade: true, Now).IsSuccess);
        Assert.True(vehicle.IsBookable(dealerCanTrade: true));
        Assert.Contains(vehicle.DomainEvents, domainEvent => domainEvent is VehiclePublished);
    }

    [Fact]
    public void A_published_vehicle_of_a_suspended_dealer_is_not_bookable()
    {
        var vehicle = Build.Vehicle();
        vehicle.AddImage("cars/1.jpg", Now);
        vehicle.Publish(true, Now);

        Assert.False(vehicle.IsBookable(dealerCanTrade: false));
    }

    [Fact]
    public void Publishing_twice_and_hiding_a_draft_are_both_refused()
    {
        var vehicle = Build.Vehicle();
        vehicle.AddImage("cars/1.jpg", Now);
        vehicle.Publish(true, Now);

        Assert.Equal("vehicle.already_published", vehicle.Publish(true, Now).Error.Code);
        Assert.True(vehicle.Hide(Now).IsSuccess);
        Assert.Equal("vehicle.not_published", vehicle.Hide(Now).Error.Code);
    }

    [Fact]
    public void A_zero_rate_is_refused_at_creation_and_on_change()
    {
        var created = Vehicle.Add(
            Id.New(), Id.New(), Build.VehicleDetails(), PlateNumber.Create("12345").Value,
            Money.Jod(0m), Money.Jod(100m), MileagePolicy.Unlimited(), FuelPolicy.FullToFull, false, Now);

        Assert.Equal("vehicle.rate_not_positive", created.Error.Code);
        Assert.Equal("vehicle.rate_not_positive", Build.Vehicle().ChangeDailyRate(Money.Jod(0m), Now).Error.Code);
    }

    [Fact]
    public void The_rate_and_the_security_deposit_must_share_a_currency()
    {
        var created = Vehicle.Add(
            Id.New(), Id.New(), Build.VehicleDetails(), PlateNumber.Create("12345").Value,
            Money.Jod(30m), Money.Create(100m, "USD"), MileagePolicy.Unlimited(), FuelPolicy.FullToFull, false, Now);

        Assert.Equal("vehicle.currency_mismatch", created.Error.Code);
        Assert.Equal("vehicle.currency_mismatch", Build.Vehicle().ChangeDailyRate(Money.Create(30m, "USD"), Now).Error.Code);
    }

    [Fact]
    public void Changing_the_rate_records_both_the_old_and_new_figure()
    {
        var vehicle = Build.Vehicle(dailyRate: 30m);

        vehicle.ChangeDailyRate(Money.Jod(45m), Now);

        var changed = Assert.Single(vehicle.DomainEvents.OfType<VehicleRateChanged>());
        Assert.Equal(30m, changed.PreviousDailyRate);
        Assert.Equal(45m, changed.NewDailyRate);
        Assert.Equal("JOD", changed.CurrencyCode);
    }

    [Fact]
    public void The_first_image_becomes_the_cover_and_removing_it_promotes_the_next()
    {
        var vehicle = Build.Vehicle();
        var first = vehicle.AddImage("cars/1.jpg", Now).Value;
        var second = vehicle.AddImage("cars/2.jpg", Now).Value;

        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);

        Assert.True(vehicle.RemoveImage(first.Id).IsSuccess);
        Assert.True(second.IsPrimary);
        Assert.Equal(0, second.Position);
    }

    [Fact]
    public void The_cover_photo_can_be_chosen_explicitly_and_only_one_wins()
    {
        var vehicle = Build.Vehicle();
        var first = vehicle.AddImage("cars/1.jpg", Now).Value;
        var second = vehicle.AddImage("cars/2.jpg", Now).Value;

        Assert.True(vehicle.SetPrimaryImage(second.Id).IsSuccess);

        Assert.False(first.IsPrimary);
        Assert.True(second.IsPrimary);
        Assert.Single(vehicle.Images, image => image.IsPrimary);
    }

    [Fact]
    public void Image_count_is_capped_and_unknown_images_report_not_found()
    {
        var vehicle = Build.Vehicle();
        for (var index = 0; index < Vehicle.MaxImages; index++)
            vehicle.AddImage($"cars/{index}.jpg", Now);

        Assert.Equal("vehicle.too_many_images", vehicle.AddImage("cars/extra.jpg", Now).Error.Code);
        Assert.Equal("vehicle.image_not_found", vehicle.RemoveImage(Id.New()).Error.Code);
        Assert.Equal("vehicle.image_not_found", vehicle.SetPrimaryImage(Id.New()).Error.Code);
    }

    [Fact]
    public void Deleting_hides_the_listing_and_cannot_repeat()
    {
        var vehicle = Build.Vehicle();
        vehicle.AddImage("cars/1.jpg", Now);
        vehicle.Publish(true, Now);

        Assert.True(vehicle.Delete(Now).IsSuccess);
        Assert.True(vehicle.IsDeleted);
        Assert.False(vehicle.IsBookable(dealerCanTrade: true));
        Assert.Equal("vehicle.already_deleted", vehicle.Delete(Now).Error.Code);
    }
}

public sealed class VehicleDetailsTests
{
    [Theory]
    [InlineData(1989)]
    [InlineData(2028)]
    public void Rejects_an_implausible_model_year(int year)
    {
        var details = VehicleDetails.Create(
            "Toyota", "Corolla", year, 5, TransmissionType.Automatic, FuelType.Petrol, currentYear: 2026);

        Assert.Equal("vehicle.invalid_year", details.Error.Code);
    }

    [Fact]
    public void Allows_next_years_model_because_they_go_on_sale_early()
    {
        var details = VehicleDetails.Create(
            "Toyota", "Corolla", 2027, 5, TransmissionType.Automatic, FuelType.Petrol, currentYear: 2026);

        Assert.True(details.IsSuccess);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void Rejects_an_implausible_seat_count(int seats)
    {
        var details = VehicleDetails.Create(
            "Toyota", "Hiace", 2024, seats, TransmissionType.Manual, FuelType.Diesel, currentYear: 2026);

        Assert.Equal("vehicle.invalid_seats", details.Error.Code);
    }

    [Fact]
    public void Requires_a_make_and_model()
    {
        var missing = VehicleDetails.Create(
            " ", "Corolla", 2024, 5, TransmissionType.Automatic, FuelType.Petrol, currentYear: 2026);

        Assert.Equal("vehicle.invalid_make_model", missing.Error.Code);
    }
}

public sealed class PlateNumberTests
{
    [Fact]
    public void Keeps_only_digits()
    {
        Assert.Equal("1234567", PlateNumber.Create("12-34567").Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("12")]
    [InlineData("12345678901")]
    public void Rejects_implausible_plates(string? raw) =>
        Assert.Equal("vehicle.invalid_plate", PlateNumber.Create(raw).Error.Code);
}

public sealed class MileagePolicyTests
{
    [Fact]
    public void Unlimited_never_charges()
    {
        Assert.True(MileagePolicy.Unlimited().ExcessChargeFor(10_000, 3).IsZero);
    }

    [Fact]
    public void Excess_is_measured_against_the_whole_rental_not_each_day()
    {
        var policy = MileagePolicy.Limited(200, Money.Jod(0.25m)).Value;

        // 600 km allowed over three days; 750 driven leaves 150 km of excess.
        Assert.Equal(Money.Jod(37.5m), policy.ExcessChargeFor(750, 3));
        // A heavy day balanced by a light one stays inside the allowance and costs nothing.
        Assert.True(policy.ExcessChargeFor(600, 3).IsZero);
        Assert.True(policy.ExcessChargeFor(10, 3).IsZero);
    }

    [Fact]
    public void A_limited_policy_needs_a_positive_daily_limit()
    {
        Assert.Equal("vehicle.invalid_mileage_policy", MileagePolicy.Limited(0, Money.Jod(1m)).Error.Code);
    }

    [Fact]
    public void Nonsense_inputs_are_programming_errors()
    {
        var policy = MileagePolicy.Limited(200, Money.Jod(0.25m)).Value;

        Assert.Throws<DomainException>(() => policy.ExcessChargeFor(-1, 3));
        Assert.Throws<DomainException>(() => policy.ExcessChargeFor(100, 0));
    }
}
