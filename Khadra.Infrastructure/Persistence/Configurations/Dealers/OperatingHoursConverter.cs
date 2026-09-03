using System.Globalization;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Khadra.Infrastructure.Persistence.Configurations.Dealers;

// OperatingHours holds a dictionary of seven DaySchedules, which is not a shape EF can own as
// columns. Nothing queries into it, so it is stored as one compact, human-readable string:
//
//   0=09:00-17:00;1=closed;2=09:00-17:00;...
//
// A custom encoding rather than JSON because it is short, diffable in psql, and the parse is total:
// anything malformed falls back to AlwaysClosed rather than throwing while materialising a dealer.
internal static class OperatingHoursConverter
{
    private const string ClosedMarker = "closed";
    private const string TimeFormat = "HH\\:mm";

    public static readonly ValueConverter<OperatingHours, string> Instance =
        new(hours => Write(hours), value => Read(value));

    // Value objects are immutable, so equality is by content and the snapshot is the same instance.
    public static readonly ValueComparer<OperatingHours> Comparer =
        new((left, right) => left != null && right != null && left.Equals(right),
            hours => hours.GetHashCode(),
            hours => hours);

    public const int MaxLength = 160;

    private static string Write(OperatingHours hours) =>
        string.Join(';', hours.Days.Select(day =>
            day.IsClosed
                ? $"{(int)day.Day}={ClosedMarker}"
                : $"{(int)day.Day}={day.OpensAt.ToString(TimeFormat, CultureInfo.InvariantCulture)}-{day.ClosesAt.ToString(TimeFormat, CultureInfo.InvariantCulture)}"));

    private static OperatingHours Read(string value)
    {
        var schedules = new List<DaySchedule>();
        foreach (var segment in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2);
            if (parts.Length != 2 || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out var dayNumber))
                return OperatingHours.AlwaysClosed();

            var day = (DayOfWeek)dayNumber;
            if (string.Equals(parts[1], ClosedMarker, StringComparison.Ordinal))
            {
                schedules.Add(DaySchedule.Closed(day));
                continue;
            }

            var window = parts[1].Split('-', 2);
            if (window.Length != 2 ||
                !TimeOnly.TryParseExact(window[0], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var opensAt) ||
                !TimeOnly.TryParseExact(window[1], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var closesAt))
            {
                return OperatingHours.AlwaysClosed();
            }

            var schedule = DaySchedule.Open(day, opensAt, closesAt);
            if (schedule.IsFailure)
                return OperatingHours.AlwaysClosed();

            schedules.Add(schedule.Value);
        }

        var hours = OperatingHours.Create(schedules);
        return hours.IsSuccess ? hours.Value : OperatingHours.AlwaysClosed();
    }
}
