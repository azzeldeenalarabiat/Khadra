using Khadra.Domain.Common;

namespace Khadra.Application.Bookings.ReadModels;

/// <summary>
/// Booking headcounts. "Active" is the domain's own idea of a live booking (BookingStatus.HoldsVehicle):
/// the states in which a vehicle is committed to this rental. Anything looser would be a number the
/// operations team could not act on.
/// </summary>
public sealed record BookingCounts(int Total, int Active, int PendingApproval);

public sealed record BookingLabel(Id BookingId, string Reference, Id DealerId);

public interface IBookingDashboardReader
{
    Task<BookingCounts> CountsAsync(CancellationToken cancellationToken = default);

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
