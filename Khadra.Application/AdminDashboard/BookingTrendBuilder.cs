using Khadra.Application.AdminDashboard.Dtos;
using Khadra.Application.Common.Ports;

namespace Khadra.Application.AdminDashboard;

/// <summary>
/// Buckets booking creation instants into local calendar days and works out the period-over-period
/// change.
///
/// Pure, and separate from the handler, because two things here are easy to get wrong and worth
/// pinning down in tests: days with no bookings must still appear (a gap in a bar chart reads as
/// "fewer", a missing bar reads as "broken"), and the comparison window must be the same length as
/// the charted one or the percentage is meaningless.
/// </summary>
public static class BookingTrendBuilder
{
    public static BookingTrendDto Build(
        IReadOnlyCollection<DateTimeOffset> createdInstants,
        IReportingCalendar calendar,
        DateOnly today,
        int trendDays)
    {
        ArgumentNullException.ThrowIfNull(createdInstants);
        ArgumentNullException.ThrowIfNull(calendar);
        if (trendDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(trendDays), "The trend must cover at least one day.");

        var perDay = new Dictionary<DateOnly, int>();
        foreach (var instant in createdInstants)
        {
            var day = calendar.DayOf(instant);
            perDay[day] = perDay.GetValueOrDefault(day) + 1;
        }

        var from = today.AddDays(-(trendDays - 1));
        var points = new List<DailyCountDto>(trendDays);
        for (var offset = 0; offset < trendDays; offset++)
        {
            var day = from.AddDays(offset);
            points.Add(new DailyCountDto(day, perDay.GetValueOrDefault(day)));
        }

        var current = points.Sum(point => point.Count);
        var previousFrom = from.AddDays(-trendDays);
        var previous = 0;
        for (var offset = 0; offset < trendDays; offset++)
            previous += perDay.GetValueOrDefault(previousFrom.AddDays(offset));

        // No baseline means no percentage. Reporting "+100%" against zero would be a made-up number.
        decimal? changePercent = previous == 0
            ? null
            : decimal.Round((current - previous) / (decimal)previous * 100m, 1, MidpointRounding.ToEven);

        return new BookingTrendDto(from, today, points, changePercent);
    }

    /// <summary>
    /// How far back the reader must look: the charted window plus the window it is compared against.
    /// </summary>
    public static DateOnly EarliestDayNeeded(DateOnly today, int trendDays) =>
        today.AddDays(-((trendDays * 2) - 1));
}
