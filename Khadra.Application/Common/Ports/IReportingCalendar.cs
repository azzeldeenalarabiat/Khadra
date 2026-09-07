namespace Khadra.Application.Common.Ports;

// Turns instants into calendar days for reporting.
//
// This exists because "bookings today" and the fourteen daily buckets on the dashboard are calendar
// questions, and the calendar that matters is the one an admin in Amman is looking at. Jordan is
// UTC+3 with no daylight saving since 2022, so a booking taken at 01:00 local falls on the PREVIOUS
// day in UTC. Bucketing in UTC would quietly misreport every figure on the chart.
//
// The zone is configured rather than assumed, so the first non-Jordan market is a settings change.
public interface IReportingCalendar
{
    /// <summary>
    /// The zone every calendar answer here is expressed in, as an IANA id ("Asia/Amman").
    /// </summary>
    /// <remarks>
    /// Exposed because the customer app has to say it out loud. A traveller quoting a car from
    /// London must be shown Amman pickup times, and a phone that formatted the instants in its own
    /// zone would quietly offer them a car three hours early.
    /// </remarks>
    string TimeZoneId { get; }

    /// <summary>The local calendar day an instant falls on.</summary>
    DateOnly DayOf(DateTimeOffset instant);

    /// <summary>The local wall-clock time an instant falls at.</summary>
    /// <remarks>
    /// The other half of <see cref="DayOf"/>, and needed for the same reason: a gallery's opening
    /// hours are wall-clock times, and 09:00 in Amman is 06:00 UTC. Asking whether a pickup falls
    /// inside opening hours without converting first would refuse the entire morning.
    ///
    /// A gallery's hours are LOCAL TO THE GALLERY, and this zone is the platform's reporting zone.
    /// For a Jordan-only platform they are the same zone, which is why this is the right answer
    /// today; the first market in a second zone needs the gallery to carry its own.
    /// </remarks>
    TimeOnly TimeOfDay(DateTimeOffset instant);

    /// <summary>The instant local midnight begins for the given local day.</summary>
    DateTimeOffset StartOfDay(DateOnly day);

    /// <summary>The local day that is currently in progress.</summary>
    DateOnly Today(DateTimeOffset now);
}
