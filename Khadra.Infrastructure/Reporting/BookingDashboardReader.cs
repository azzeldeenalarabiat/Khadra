using Khadra.Application.Bookings.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

internal sealed class BookingDashboardReader(KhadraDbContext context) : IBookingDashboardReader
{
    public async Task<BookingCounts> CountsAsync(
        DateTimeOffset createdSince,
        CancellationToken cancellationToken = default)
    {
        var requested = BookingStatus.Requested;
        // "Active" is BookingStatus.HoldsVehicle spelled out. It cannot be expressed as a property
        // call in a LINQ predicate (the domain computes it in memory), so the member statuses are
        // listed here; the domain remains the definition and this is the projection of it.
        var approved = BookingStatus.Approved;
        var confirmed = BookingStatus.Confirmed;
        var pickedUp = BookingStatus.PickedUp;

        var counts = await context.Bookings
            .GroupBy(_ => 1)
            .Select(group => new BookingCounts(
                group.Count(),
                // Today, as one more filter on the aggregate the card already pays for. No upper
                // bound: createdSince is local midnight of the day in progress, and a booking cannot
                // be created after now.
                group.Count(booking => booking.CreatedAt >= createdSince),
                group.Count(booking =>
                    booking.Status == requested ||
                    booking.Status == approved ||
                    booking.Status == confirmed ||
                    booking.Status == pickedUp),
                group.Count(booking => booking.Status == requested)))
            .SingleOrDefaultAsync(cancellationToken);

        return counts ?? new BookingCounts(0, 0, 0, 0);
    }

    public async Task<IReadOnlyList<DateTimeOffset>> CreatedBetweenAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken = default) =>
        await context.Bookings
            .Where(booking => booking.CreatedAt >= fromInclusive && booking.CreatedAt < toExclusive)
            .Select(booking => booking.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BookingLabel>> LabelsAsync(
        IReadOnlyCollection<Id> bookingIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bookingIds);
        if (bookingIds.Count == 0)
            return [];

        var ids = bookingIds.ToList();
        return await context.Bookings
            .Where(booking => ids.Contains(booking.Id))
            .Select(booking => new BookingLabel(booking.Id, booking.Reference.Value, booking.DealerId))
            .ToListAsync(cancellationToken);
    }
}
