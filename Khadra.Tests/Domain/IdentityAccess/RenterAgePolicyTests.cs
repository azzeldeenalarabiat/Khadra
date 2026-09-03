using Khadra.Domain.IdentityAccess;

namespace Khadra.Tests.Domain.IdentityAccess;

public sealed class RenterAgePolicyTests
{
    private static readonly DateOnly Today = new(2026, 9, 3);
    private const int Minimum = 21;

    [Fact]
    public void With_no_configured_minimum_nobody_is_refused_on_age()
    {
        // The owner had not set a value until this session, and the platform still has to run without
        // one. A missing rule means no rule, not a rule of zero.
        Assert.True(RenterAgePolicy.Validate(new DateOnly(2015, 1, 1), null, Today).IsSuccess);
        Assert.True(RenterAgePolicy.Validate(null, null, Today).IsSuccess);
    }

    [Fact]
    public void A_configured_minimum_makes_the_date_of_birth_mandatory()
    {
        var result = RenterAgePolicy.Validate(null, Minimum, Today);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.date_of_birth_required", result.Error.Code);
    }

    [Fact]
    public void Someone_below_the_minimum_is_refused_and_told_the_figure()
    {
        var twentyToday = Today.AddYears(-20);

        var result = RenterAgePolicy.Validate(twentyToday, Minimum, Today);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.under_minimum_age", result.Error.Code);
        Assert.Contains("21", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_birthday_itself_qualifies_and_the_day_before_does_not()
    {
        var turnsTwentyOneToday = Today.AddYears(-Minimum);

        Assert.True(RenterAgePolicy.Validate(turnsTwentyOneToday, Minimum, Today).IsSuccess);
        Assert.True(RenterAgePolicy.Validate(turnsTwentyOneToday.AddDays(1), Minimum, Today).IsFailure);
    }

    [Fact]
    public void A_date_in_the_future_is_rejected_as_invalid_rather_than_as_too_young()
    {
        var result = RenterAgePolicy.Validate(Today.AddDays(1), Minimum, Today);

        Assert.True(result.IsFailure);
        Assert.Equal("auth.invalid_date_of_birth", result.Error.Code);
    }

    [Theory]
    // Ordinary birthdays.
    [InlineData(2000, 6, 15, 2026, 6, 14, 25)]
    [InlineData(2000, 6, 15, 2026, 6, 15, 26)]
    // Born on a leap day. DateOnly.AddYears clamps 29 February to the 28th, so in a common year the
    // birthday is reached on 28 February -- the convention most civil registries use, and the one
    // that never leaves a leap-day renter a year older than the calendar says.
    [InlineData(2004, 2, 29, 2025, 2, 27, 20)]
    [InlineData(2004, 2, 29, 2025, 2, 28, 21)]
    [InlineData(2004, 2, 29, 2028, 2, 29, 24)]
    public void Ages_are_counted_in_completed_years(
        int birthYear, int birthMonth, int birthDay,
        int onYear, int onMonth, int onDay,
        int expected)
    {
        var age = RenterAgePolicy.AgeOn(new DateOnly(birthYear, birthMonth, birthDay), new DateOnly(onYear, onMonth, onDay));

        Assert.Equal(expected, age);
    }
}
