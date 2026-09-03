using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Reporting;
using Microsoft.Extensions.Options;

namespace Khadra.Tests.Infrastructure;

// Jordan is UTC+3 and has not observed daylight saving since 2022. These tests exist because getting
// this wrong is silent: every count on the dashboard would still render, just against the wrong day.
public sealed class ReportingCalendarTests
{
    private static ReportingCalendar Amman() =>
        new(Options.Create(new AdminDashboardOptions { ReportingTimeZone = "Asia/Amman" }));

    [Fact]
    public void An_instant_late_in_the_UTC_evening_already_belongs_to_the_next_Amman_day()
    {
        var calendar = Amman();

        Assert.Equal(new DateOnly(2026, 9, 3), calendar.DayOf(new DateTimeOffset(2026, 9, 2, 21, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 9, 2), calendar.DayOf(new DateTimeOffset(2026, 9, 2, 20, 59, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Local_midnight_is_three_hours_before_UTC_midnight()
    {
        var calendar = Amman();

        Assert.Equal(
            new DateTimeOffset(2026, 9, 2, 21, 0, 0, TimeSpan.Zero),
            calendar.StartOfDay(new DateOnly(2026, 9, 3)));
    }

    [Fact]
    public void The_start_of_a_day_falls_on_that_day()
    {
        var calendar = Amman();
        var day = new DateOnly(2026, 9, 3);

        Assert.Equal(day, calendar.DayOf(calendar.StartOfDay(day)));
    }

    [Fact]
    public void The_last_instant_of_a_day_still_falls_on_it()
    {
        var calendar = Amman();
        var day = new DateOnly(2026, 9, 3);

        var lastInstant = calendar.StartOfDay(day.AddDays(1)).AddTicks(-1);

        Assert.Equal(day, calendar.DayOf(lastInstant));
    }

    [Fact]
    public void The_offset_a_caller_supplies_does_not_change_the_answer()
    {
        var calendar = Amman();
        // The same instant, expressed two ways.
        var asUtc = new DateTimeOffset(2026, 9, 2, 21, 30, 0, TimeSpan.Zero);
        var asAmman = new DateTimeOffset(2026, 9, 3, 0, 30, 0, TimeSpan.FromHours(3));

        Assert.Equal(calendar.DayOf(asUtc), calendar.DayOf(asAmman));
        Assert.Equal(new DateOnly(2026, 9, 3), calendar.DayOf(asAmman));
    }

    [Fact]
    public void A_zone_this_machine_does_not_know_fails_immediately_rather_than_on_first_use()
    {
        Assert.ThrowsAny<Exception>(() =>
            new ReportingCalendar(Options.Create(new AdminDashboardOptions { ReportingTimeZone = "Mars/Olympus" })));
    }
}
