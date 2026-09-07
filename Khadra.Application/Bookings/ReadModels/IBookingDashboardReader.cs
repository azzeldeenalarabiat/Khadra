using Khadra.Domain.Common;

namespace Khadra.Application.Bookings.ReadModels;

/// <summary>
/// Booking headcounts. "Active" is the domain's own idea of a live booking (BookingStatus.HoldsVehicle):
/// the states in which a vehicle is committed to this rental. Anything looser would be a number the
/// operations team could not act on.
/// </summary>
public sealed record BookingCounts(int Total, int Today, int Active, int PendingApproval);

public sealed record BookingLabel(Id BookingId, string Reference, Id DealerId);

public interface IBookingDashboardReader
{
    /// <summary>
    /// The four booking figures the KPI card shows, in one aggregate.
    ///
    /// <paramref name="createdSince"/> is local midnight of the reporting day, and "today" is counted
    /// as another filter on the same single-row query rather than a second round trip. It used to be
    /// derived from the trend, so the KPI and the last bar of the chart could not disagree; now that
    /// they are separate endpoints, the part of that guarantee worth keeping survives because BOTH
    /// take their day from <see cref="Common.Ports.IReportingCalendar"/> — one definition of which
    /// local day it is, not one query. Any other way of deciding "today" would be a second calendar.
    /// </summary>
    Task<BookingCounts> CountsAsync(
        DateTimeOffset createdSince,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creation instants inside a window, for the daily trend.
    ///
    /// Returns the raw instants rather than pre-grouped counts because the grouping is a LOCAL
    /// calendar question (see IReportingCalendar) and pushing a time zone into SQL puts the one part
    /// of this that is easy to get wrong somewhere it cannot be unit-tested. The window is bounded by
    /// the chart, so the row count is bounded too; if that stops being true this becomes a grouped
    /// query using AT TIME ZONE.
    /// </summary>
    Task<IReadOnlyList<DateTimeOffset>> CreatedBetweenAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BookingLabel>> LabelsAsync(
        IReadOnlyCollection<Id> bookingIds,
        CancellationToken cancellationToken = default);
}
