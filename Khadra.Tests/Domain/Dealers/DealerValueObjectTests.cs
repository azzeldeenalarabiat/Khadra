using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Dealers;

public sealed class BusinessNameTests
{
    [Fact]
    public void Collapses_whitespace_and_accepts_arabic()
    {
        Assert.Equal("Petra Rentals", BusinessName.Create("  Petra   Rentals ").Value.Value);
        Assert.True(BusinessName.Create("تأجير البتراء").IsSuccess);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("A")]
    public void Rejects_empty_or_too_short(string? raw) =>
        Assert.Equal("dealer.invalid_business_name", BusinessName.Create(raw).Error.Code);

    [Fact]
    public void Rejects_over_the_maximum_length() =>
        Assert.True(BusinessName.Create(new string('x', 151)).IsFailure);
}

public sealed class CommercialRegistrationNumberTests
{
    /// <summary>
    /// Separators come out because people type the number the way it is printed on the certificate.
    /// </summary>
    [Theory]
    [InlineData("123 456", "123456")]
    [InlineData("123-456", "123456")]
    [InlineData("12/34/56", "123456")]
    [InlineData("123.456", "123456")]
    [InlineData("  123456  ", "123456")]
    public void Drops_the_separators_people_type(string raw, string expected) =>
        Assert.Equal(expected, CommercialRegistrationNumber.Create(raw).Value.Value);

    /// <summary>
    /// The regression. Letters used to be DELETED rather than refused, so "E2E20260906" was stored
    /// and displayed as "220260906": a different number, accepted silently, checked by an
    /// administrator against a certificate it no longer matches. The column is UNIQUE too, so
    /// "AB-1234" and "1234" collapsed onto one another and the second applicant was turned away for
    /// a number they had never typed.
    /// </summary>
    [Theory]
    [InlineData("E2E20260906")]
    [InlineData("CR-123 456")]
    [InlineData("AB1234")]
    [InlineData("1234x")]
    public void Refuses_anything_that_is_not_a_digit_or_a_separator(string raw) =>
        Assert.Equal(
            "dealer.invalid_commercial_registration",
            CommercialRegistrationNumber.Create(raw).Error.Code);

    [Theory]
    [InlineData(null)]
    [InlineData("12")]
    [InlineData("abc")]
    [InlineData("123456789012345678901")]
    [InlineData("---")]
    public void Rejects_implausible_numbers(string? raw) =>
        Assert.True(CommercialRegistrationNumber.Create(raw).IsFailure);
}

public sealed class OperatingHoursTests
{
    [Fact]
    public void A_uniform_week_covers_all_seven_days()
    {
        var hours = OperatingHours.Uniform(new TimeOnly(9, 0), new TimeOnly(17, 0)).Value;

        Assert.Equal(7, hours.Days.Count);
        Assert.True(hours.IsOpenAt(DayOfWeek.Monday, new TimeOnly(12, 0)));
        Assert.False(hours.IsOpenAt(DayOfWeek.Monday, new TimeOnly(8, 59)));
        // Closing time is exclusive: at 17:00 the office is shut.
        Assert.False(hours.IsOpenAt(DayOfWeek.Monday, new TimeOnly(17, 0)));
    }

    [Fact]
    public void Closing_before_opening_is_rejected()
    {
        Assert.True(DaySchedule.Open(DayOfWeek.Friday, new TimeOnly(18, 0), new TimeOnly(9, 0)).IsFailure);
        Assert.True(OperatingHours.Uniform(new TimeOnly(17, 0), new TimeOnly(9, 0)).IsFailure);
    }

    [Fact]
    public void A_partial_or_duplicated_week_is_rejected()
    {
        var monday = DaySchedule.Open(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0)).Value;

        Assert.True(OperatingHours.Create([monday]).IsFailure);
        Assert.True(OperatingHours.Create([monday, monday]).IsFailure);
    }

    [Fact]
    public void A_closed_day_is_never_open()
    {
        var schedules = Enum.GetValues<DayOfWeek>()
            .Select(day => day == DayOfWeek.Friday
                ? DaySchedule.Closed(day)
                : DaySchedule.Open(day, new TimeOnly(9, 0), new TimeOnly(17, 0)).Value);
        var hours = OperatingHours.Create(schedules).Value;

        Assert.False(hours.IsOpenAt(DayOfWeek.Friday, new TimeOnly(12, 0)));
        Assert.True(hours.IsOpenAt(DayOfWeek.Saturday, new TimeOnly(12, 0)));
        Assert.True(OperatingHours.AlwaysClosed().IsClosedAllWeek);
    }
}

public sealed class DeliverySettingsTests
{
    [Fact]
    public void Disabled_settings_cover_nothing_even_at_the_same_address()
    {
        Assert.False(DeliverySettings.Disabled.Covers(Build.Amman, Build.Amman));
    }

    [Fact]
    public void Coverage_is_inclusive_of_the_radius_boundary()
    {
        var settings = DeliverySettings.Enabled(25m, Money.Jod(8m)).Value;

        Assert.True(settings.Covers(Build.Amman, Build.Amman));
        Assert.True(settings.Covers(Build.Amman, Build.Zarqa));
        Assert.False(settings.Covers(Build.Amman, Build.Aqaba));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(201)]
    public void Rejects_an_implausible_radius(decimal radiusKm) =>
        Assert.True(DeliverySettings.Enabled(radiusKm, Money.Jod(8m)).IsFailure);
}

public sealed class PercentageTests
{
    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void Rejects_values_outside_zero_to_one_hundred(decimal value) =>
        Assert.Equal("percentage.out_of_range", Percentage.Create(value).Error.Code);

    [Fact]
    public void Applies_to_money_and_rounds_once()
    {
        var twenty = Percentage.FromValidated(20m);

        Assert.Equal(Money.Jod(18m), twenty.Of(Money.Jod(90m)));
        Assert.Equal(Money.Jod(0m), Percentage.Zero.Of(Money.Jod(90m)));
        Assert.True(Percentage.Zero.IsZero);
    }

    [Fact]
    public void Compares_and_prints_readably()
    {
        Assert.True(Percentage.FromValidated(50m).IsGreaterThan(Percentage.FromValidated(25m)));
        Assert.False(Percentage.FromValidated(25m).IsGreaterThan(Percentage.FromValidated(25m)));
        Assert.Equal("20%", Percentage.FromValidated(20m).ToString());
    }

    [Fact]
    public void Invalid_validated_construction_is_a_programming_error() =>
        Assert.Throws<DomainException>(() => Percentage.FromValidated(120m));
}
