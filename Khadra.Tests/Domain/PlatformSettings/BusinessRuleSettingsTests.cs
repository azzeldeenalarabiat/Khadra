using Khadra.Domain.Common;
using Khadra.Domain.PlatformSettings;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.PlatformSettings;

public sealed class BusinessRuleSettingsTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    // The owner's confirmed numbers from spec section 2.
    private static BusinessRuleSettings Confirmed(
        decimal commission = 20m,
        decimal deposit = 20m,
        int noShowHours = 8,
        decimal penaltyMin = 25m,
        decimal penaltyMax = 50m,
        int freeCancelMinutes = 60,
        int slaHours = 48) =>
        BusinessRuleSettings.Create(
            Build.Percent(commission),
            Build.Percent(deposit),
            noShowHours,
            Money.Jod(10m),
            Build.Percent(penaltyMin),
            Build.Percent(penaltyMax),
            freeCancelMinutes,
            slaHours,
            Now).Value;

    [Fact]
    public void The_confirmed_numbers_load_and_expose_usable_time_spans()
    {
        var settings = Confirmed();

        Assert.Equal(20m, settings.CommissionPercent.Value);
        Assert.Equal(Money.Jod(10m), settings.DeliveryFee);
        Assert.Equal(TimeSpan.FromHours(8), settings.NoShowTimeout);
        Assert.Equal(TimeSpan.FromMinutes(60), settings.FreeCancellationWindow);
        Assert.Equal(TimeSpan.FromHours(48), settings.AdminSla);
        Assert.Equal(1, settings.Version);
    }

    [Fact]
    public void Commission_cannot_exceed_the_deposit_it_is_collected_from()
    {
        // Spec 2.1 collects commission out of the card deposit. A commission above the deposit would
        // leave the platform chasing the dealer for the difference on every single booking.
        var settings = BusinessRuleSettings.Create(
            Build.Percent(25m), Build.Percent(20m), 8, Money.Jod(10m),
            Build.Percent(25m), Build.Percent(50m), 60, 48, Now);

        Assert.Equal("settings.deposit_below_commission", settings.Error.Code);
    }

    [Fact]
    public void An_inverted_penalty_range_is_refused()
    {
        var settings = BusinessRuleSettings.Create(
            Build.Percent(20m), Build.Percent(20m), 8, Money.Jod(10m),
            Build.Percent(50m), Build.Percent(25m), 60, 48, Now);

        Assert.Equal("settings.penalty_range_inverted", settings.Error.Code);
    }

    [Theory]
    [InlineData(0, 60, 48, "settings.invalid_no_show_timeout")]
    [InlineData(8, -1, 48, "settings.invalid_cancellation_window")]
    [InlineData(8, 60, 0, "settings.invalid_sla")]
    public void Out_of_range_windows_are_refused(int noShowHours, int freeCancelMinutes, int slaHours, string code)
    {
        var settings = BusinessRuleSettings.Create(
            Build.Percent(20m), Build.Percent(20m), noShowHours, Money.Jod(10m),
            Build.Percent(25m), Build.Percent(50m), freeCancelMinutes, slaHours, Now);

        Assert.Equal(code, settings.Error.Code);
    }

    [Fact]
    public void An_update_bumps_the_version_and_records_the_admin()
    {
        var settings = Confirmed();
        var admin = Id.New();

        var updated = settings.Update(
            Build.Percent(15m), Build.Percent(20m), 6, Money.Jod(12m),
            Build.Percent(30m), Build.Percent(40m), 90, 24, admin, Now.AddDays(1));

        Assert.True(updated.IsSuccess);
        Assert.Equal(2, settings.Version);
        Assert.Equal(admin, settings.UpdatedByAdminId);
        Assert.Equal(15m, settings.CommissionPercent.Value);
    }

    [Fact]
    public void A_rejected_update_changes_nothing_and_does_not_bump_the_version()
    {
        var settings = Confirmed();

        var rejected = settings.Update(
            Build.Percent(50m), Build.Percent(20m), 8, Money.Jod(10m),
            Build.Percent(25m), Build.Percent(50m), 60, 48, Id.New(), Now.AddDays(1));

        Assert.True(rejected.IsFailure);
        Assert.Equal(1, settings.Version);
        Assert.Equal(20m, settings.CommissionPercent.Value);
    }

    [Fact]
    public void The_minimum_renter_age_is_optional_but_must_be_plausible()
    {
        var settings = Confirmed();

        Assert.Equal("settings.invalid_minimum_age", settings.SetMinimumRenterAge(16).Error.Code);
        Assert.True(settings.SetMinimumRenterAge(21).IsSuccess);
        Assert.Equal(21, settings.MinimumRenterAge);
        Assert.True(settings.SetMinimumRenterAge(null).IsSuccess);
        Assert.Null(settings.MinimumRenterAge);
    }

    [Fact]
    public void The_owners_outstanding_decisions_are_surfaced_rather_than_guessed()
    {
        var settings = Confirmed();

        var pending = settings.PendingOwnerDecisions();

        // Spec 2.2 and 2.3: the penalty tier, the processing fee, the minimum age and the IDP rule.
        Assert.Equal(4, pending.Count);
        Assert.Contains(pending, item => item.Contains("penalty", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(pending, item => item.Contains("processing fee", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Settling_the_open_questions_clears_them_from_the_pending_list()
    {
        var settings = Confirmed(penaltyMin: 50m, penaltyMax: 50m);
        settings.SetQuickCancellationProcessingFee(Money.Jod(1.5m));
        settings.SetMinimumRenterAge(21);
        settings.SetInternationalPermitRequirement(true);

        Assert.Empty(settings.PendingOwnerDecisions());
    }
}

public sealed class LookupTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    [Fact]
    public void Lookups_are_bilingual_from_the_start()
    {
        var carType = CarType.Create("Sedan", "سيدان", 1, Now).Value;

        Assert.Equal("Sedan", carType.NameEn);
        Assert.Equal("سيدان", carType.NameAr);
        Assert.True(carType.IsActive);
    }

    [Fact]
    public void Both_names_are_required()
    {
        Assert.Equal("lookup.invalid_name", CarType.Create("Sedan", " ", 1, Now).Error.Code);
        Assert.Equal("lookup.invalid_name", City.Create(" ", "عمان", 1, Now).Error.Code);
    }

    [Fact]
    public void A_lookup_is_deactivated_rather_than_deleted_so_old_bookings_still_read()
    {
        var city = City.Create("Amman", "عمان", 1, Now, Build.Amman).Value;

        Assert.True(city.Deactivate().IsSuccess);
        Assert.False(city.IsActive);
        Assert.Equal("lookup.already_inactive", city.Deactivate().Error.Code);

        Assert.True(city.Activate().IsSuccess);
        Assert.Equal("lookup.already_active", city.Activate().Error.Code);
    }

    [Fact]
    public void A_lookup_can_be_renamed_and_reordered()
    {
        var carType = CarType.Create("Sedan", "سيدان", 1, Now).Value;

        Assert.True(carType.Rename("Saloon", "صالون").IsSuccess);
        Assert.Equal("Saloon", carType.NameEn);

        carType.SetDisplayOrder(5);
        Assert.Equal(5, carType.DisplayOrder);
        Assert.Equal("lookup.invalid_name", carType.Rename("", "صالون").Error.Code);
    }

    [Fact]
    public void A_city_can_carry_a_centre_point()
    {
        var city = City.Create("Amman", "عمان", 1, Now, Build.Amman).Value;

        Assert.Equal(Build.Amman, city.Centre);
        city.SetCentre(null);
        Assert.Null(city.Centre);
    }
}
