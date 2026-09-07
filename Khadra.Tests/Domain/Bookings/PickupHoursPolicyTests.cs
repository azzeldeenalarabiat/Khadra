using CSharpFunctionalExtensions;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;

namespace Khadra.Tests.Domain.Bookings;

/// <summary>
/// Whether a gallery is open when a self-pickup rental needs somebody behind the counter.
/// </summary>
/// <remarks>
/// Settled by the owner on 2026-09-07: refuse a self-pickup outside opening hours, and never judge a
/// delivery against counter hours. The asymmetry is the substance of this file — a customer cannot
/// collect keys from a closed office, but a gallery driving a car out may do that whenever it likes.
/// </remarks>
public sealed class PickupHoursPolicyTests
{
    // Open 09:00-17:00 every day except Friday, which is the weekend day a Jordanian office is most
    // likely to close.
    private static OperatingHours WeekdaysNineToFive()
    {
        var schedules = Enum.GetValues<DayOfWeek>()
            .Select(day => day == DayOfWeek.Friday
                ? DaySchedule.Closed(day)
                : DaySchedule.Open(day, new TimeOnly(9, 0), new TimeOnly(17, 0)).Value);
        return OperatingHours.Create(schedules).Value;
    }

    // A Monday and a Thursday, so no test accidentally lands on the closed day.
    private static readonly DateOnly Monday = new(2026, 9, 7);
    private static readonly DateOnly Thursday = new(2026, 9, 10);
    private static readonly DateOnly Friday = new(2026, 9, 11);

    private static UnitResult<Error> Validate(
        PickupMethod method,
        TimeOnly pickupTime,
        TimeOnly? returnTime = null,
        DateOnly? pickupDate = null,
        DateOnly? returnDate = null) =>
        PickupHoursPolicy.Validate(
            WeekdaysNineToFive(),
            method,
            pickupDate ?? Monday,
            pickupTime,
            returnDate ?? Thursday,
            returnTime ?? new TimeOnly(11, 0));

    [Fact]
    public void A_self_pickup_inside_opening_hours_is_allowed()
    {
        Assert.True(Validate(PickupMethod.SelfPickup, new TimeOnly(10, 0)).IsSuccess);
    }

    [Fact]
    public void A_self_pickup_before_the_gallery_opens_is_refused()
    {
        var result = Validate(PickupMethod.SelfPickup, new TimeOnly(3, 0));

        Assert.Equal("booking.pickup_outside_opening_hours", result.Error.Code);
    }

    [Fact]
    public void A_self_pickup_after_the_gallery_closes_is_refused()
    {
        Assert.Equal(
            "booking.pickup_outside_opening_hours",
            Validate(PickupMethod.SelfPickup, new TimeOnly(21, 0)).Error.Code);
    }

    [Fact]
    public void A_self_pickup_on_a_day_the_gallery_is_shut_is_refused()
    {
        var result = Validate(PickupMethod.SelfPickup, new TimeOnly(10, 0), pickupDate: Friday);

        Assert.Equal("booking.pickup_outside_opening_hours", result.Error.Code);
        Assert.Contains("closed on Friday", result.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The customer brings the car back to the same counter, so a return at 03:00 is the same
    /// impossibility as a collection at 03:00 — it is just discovered three days later.
    /// </summary>
    [Fact]
    public void A_self_pickup_returning_outside_opening_hours_is_refused()
    {
        var result = Validate(PickupMethod.SelfPickup, new TimeOnly(10, 0), returnTime: new TimeOnly(23, 30));

        Assert.Equal("booking.return_outside_opening_hours", result.Error.Code);
    }

    /// <summary>Reported separately, so the customer knows WHICH date to move.</summary>
    [Fact]
    public void The_pickup_is_reported_before_the_return()
    {
        var result = Validate(PickupMethod.SelfPickup, new TimeOnly(3, 0), returnTime: new TimeOnly(23, 30));

        Assert.Equal("booking.pickup_outside_opening_hours", result.Error.Code);
    }

    /// <summary>
    /// "Outside opening hours" on its own tells somebody they are wrong without telling them what
    /// would be right, and they would have to go and find the gallery's page to learn it.
    /// </summary>
    [Fact]
    public void The_refusal_says_what_the_gallery_actually_does_that_day()
    {
        var result = Validate(PickupMethod.SelfPickup, new TimeOnly(3, 0));

        Assert.Contains("09:00-17:00", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("Monday", result.Error.Message, StringComparison.Ordinal);
    }

    // ── Delivery is never judged against counter hours ─────────────────────────────────────────

    [Fact]
    public void A_delivery_at_three_in_the_morning_is_allowed()
    {
        Assert.True(
            Validate(PickupMethod.Delivery, new TimeOnly(3, 0), returnTime: new TimeOnly(4, 0)).IsSuccess);
    }

    [Fact]
    public void A_delivery_on_a_day_the_counter_is_shut_is_allowed()
    {
        Assert.True(
            Validate(PickupMethod.Delivery, new TimeOnly(10, 0), pickupDate: Friday, returnDate: Friday).IsSuccess);
    }

    // ── Boundaries ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Opening_time_itself_is_open()
    {
        Assert.True(Validate(PickupMethod.SelfPickup, new TimeOnly(9, 0)).IsSuccess);
    }

    /// <summary>
    /// Closing time is NOT open: <c>DaySchedule.IsOpenAt</c> is half-open, and a customer arriving at
    /// the exact moment the shutters come down has not been served.
    /// </summary>
    [Fact]
    public void Closing_time_itself_is_closed()
    {
        Assert.Equal(
            "booking.pickup_outside_opening_hours",
            Validate(PickupMethod.SelfPickup, new TimeOnly(17, 0)).Error.Code);
    }

    /// <summary>
    /// A gallery that has never set real hours refuses its own self-pickup bookings. That is loud,
    /// immediately visible to them, and fixable from their own console — the failure mode worth
    /// having, against the alternative of a customer told at the counter.
    /// </summary>
    [Fact]
    public void A_gallery_that_is_closed_all_week_takes_no_self_pickup_bookings()
    {
        var result = PickupHoursPolicy.Validate(
            OperatingHours.AlwaysClosed(),
            PickupMethod.SelfPickup,
            Monday, new TimeOnly(10, 0),
            Thursday, new TimeOnly(11, 0));

        Assert.Equal("booking.pickup_outside_opening_hours", result.Error.Code);
    }

    [Fact]
    public void But_it_can_still_deliver()
    {
        var result = PickupHoursPolicy.Validate(
            OperatingHours.AlwaysClosed(),
            PickupMethod.Delivery,
            Monday, new TimeOnly(10, 0),
            Thursday, new TimeOnly(11, 0));

        Assert.True(result.IsSuccess);
    }
}
