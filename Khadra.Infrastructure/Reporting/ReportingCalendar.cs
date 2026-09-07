using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Reporting;

// Jordan is UTC+3 year round (it abolished daylight saving in 2022), but this resolves a real time
// zone rather than adding three hours, so the first market that does observe DST is a settings change
// and not a bug hunt.
//
// Windows and Linux name zones differently ("Jordan Standard Time" vs "Asia/Amman"); .NET 8+ accepts
// either on either platform, so the configured IANA id works in a container and on a developer laptop.
internal sealed class ReportingCalendar : IReportingCalendar
{
    private readonly TimeZoneInfo _zone;

    public ReportingCalendar(IOptions<AdminDashboardOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.ReportingTimeZone);
    }

    public string TimeZoneId => _zone.Id;

    public DateOnly DayOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _zone).DateTime);

    public DateTimeOffset StartOfDay(DateOnly day)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue);
        // Unspecified kind is required: it tells ConvertTimeToUtc to read the value as wall-clock time
        // in the target zone rather than as a UTC or local instant.
        var unspecified = DateTime.SpecifyKind(midnight, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, _zone), TimeSpan.Zero);
    }

    public DateOnly Today(DateTimeOffset now) => DayOf(now);
}
