using Khadra.Application.AdminDashboard;
using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Reporting;
using Microsoft.Extensions.Options;

namespace Khadra.Tests.Application.AdminDashboard;

public sealed class BookingTrendBuilderTests
{
    private static readonly IReportingCalendar Amman = new ReportingCalendar(
        Options.Create(new AdminDashboardOptions { ReportingTimeZone = "Asia/Amman" }));

    private static readonly DateOnly Today = new(2026, 9, 3);

    // The instant the panel speaks for. Not what these tests are about -- they are about the
    // bucketing -- so it is one constant, passed through and asserted once below.
    private static readonly DateTimeOffset GeneratedAt = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_day_in_the_window_gets_a_bar_even_when_nothing_happened()
    {
        // A missing bar reads as a broken chart; a zero-height bar reads as a quiet day.
        var trend = BookingTrendBuilder.Build([], Amman, Today, trendDays: 14, GeneratedAt);

        Assert.Equal(14, trend.Points.Count);
        Assert.All(trend.Points, point => Assert.Equal(0, point.Count));
        Assert.Equal(new DateOnly(2026, 8, 21), trend.From);
        Assert.Equal(Today, trend.To);
    }

    [Fact]
    public void Bookings_are_bucketed_by_the_Amman_day_not_the_UTC_one()
    {
        // 22:30 UTC on 2 September is 01:30 on 3 September in Amman. Bucketing in UTC would file this
        // booking under the wrong day and quietly understate today.
        var lateNightInAmman = new DateTimeOffset(2026, 9, 2, 22, 30, 0, TimeSpan.Zero);

        var trend = BookingTrendBuilder.Build([lateNightInAmman], Amman, Today, trendDays: 14, GeneratedAt);

        Assert.Equal(1, trend.Points.Single(point => point.Date == new DateOnly(2026, 9, 3)).Count);
        Assert.Equal(0, trend.Points.Single(point => point.Date == new DateOnly(2026, 9, 2)).Count);
    }

    [Fact]
    public void The_change_compares_the_charted_window_against_the_one_before_it()
    {
        // Four bookings inside the charted week, two in the week before it: +100%.
        var instants = new List<DateTimeOffset>();
        instants.AddRange(Enumerable.Repeat(Noon(2026, 9, 1), 4));
        instants.AddRange(Enumerable.Repeat(Noon(2026, 8, 25), 2));

        var trend = BookingTrendBuilder.Build(instants, Amman, Today, trendDays: 7, GeneratedAt);

        Assert.Equal(100m, trend.ChangePercent);
    }

    [Fact]
    public void No_baseline_means_no_percentage_rather_than_a_made_up_one()
    {
        var trend = BookingTrendBuilder.Build([Noon(2026, 9, 1)], Amman, Today, trendDays: 7, GeneratedAt);

        Assert.Null(trend.ChangePercent);
    }

    [Fact]
    public void A_decline_is_reported_as_a_negative_change()
    {
        var instants = new List<DateTimeOffset>();
        instants.AddRange(Enumerable.Repeat(Noon(2026, 9, 1), 2));
        instants.AddRange(Enumerable.Repeat(Noon(2026, 8, 25), 4));

        var trend = BookingTrendBuilder.Build(instants, Amman, Today, trendDays: 7, GeneratedAt);

        Assert.Equal(-50m, trend.ChangePercent);
    }

    [Fact]
    public void Bookings_outside_the_charted_window_do_not_appear_as_bars()
    {
        // It still counts towards the comparison window, which is exactly what it is read for.
        var trend = BookingTrendBuilder.Build([Noon(2026, 8, 20)], Amman, Today, trendDays: 14, GeneratedAt);

        Assert.Equal(14, trend.Points.Count);
        Assert.All(trend.Points, point => Assert.Equal(0, point.Count));
    }

    [Fact]
    public void The_reader_is_asked_for_both_windows_so_the_comparison_has_something_to_stand_on()
    {
        Assert.Equal(new DateOnly(2026, 8, 7), BookingTrendBuilder.EarliestDayNeeded(Today, trendDays: 14));
    }

    [Fact]
    public void A_window_of_no_days_is_a_programming_error()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BookingTrendBuilder.Build([], Amman, Today, trendDays: 0, GeneratedAt));
    }

    private static DateTimeOffset Noon(int year, int month, int day) =>
        new(year, month, day, 9, 0, 0, TimeSpan.Zero);
}
