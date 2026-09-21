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
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var requested = BookingStatus.Requested;
        // "Active" is BookingStatus.HoldsVehicle spelled out. It cannot be expressed as a property
        // call in a LINQ predicate (the domain computes it in memory), so the member statuses are
        // listed here; the domain remains the definition and this is the projection of it.
        var approved = BookingStatus.Approved;
        var confirmed = BookingStatus.Confirmed;
        var pickedUp = BookingStatus.PickedUp;

        // The two figures that are not about liveness, still in one aggregate: a booking that
        // expired still happened, and neither of these asks whether it is over.
        var totals = await context.Bookings
            .GroupBy(_ => 1)
            .Select(group => new
            {
                All = group.Count(),
                // Today, as one more filter on the aggregate the card already pays for. No upper
                // bound: createdSince is local midnight of the day in progress, and a booking cannot
                // be created after now.
                Today = group.Count(booking => booking.CreatedAt >= createdSince),
            })
            .SingleOrDefaultAsync(cancellationToken);

        // ...and the two that ARE, asked separately so the rule stays in one place.
        //
        // `BookingLapse.HasNotLapsedAt` is an expression over a Booking, which composes into a
        // `Where` but cannot go inside the conditional aggregates above. Spelling the deadlines out
        // there instead would put a second copy of the lapse rule in this file -- the duplication
        // `CatalogueReader` already warns about, and the one `BookingLapseTests` exists to prevent.
        // Two extra indexed counts is the cheaper mistake.
        //
        // Why it is needed at all: "active" and "pending" both mean a window that has not closed. A
        // request past its decision deadline and an approval past its payment deadline are finished
        // the moment the clock says so -- the catalogue has already released the car -- and the row
        // only catches up when the settlement sweep runs. On Render's free tier the API sleeps, so
        // that can be hours, and both figures were inflated for the whole of it.
        var live = context.Bookings.Where(BookingLapse.HasNotLapsedAt(now));

        var active = await live.CountAsync(
            booking => booking.Status == requested ||
                       booking.Status == approved ||
                       booking.Status == confirmed ||
                       booking.Status == pickedUp,
            cancellationToken);

        var pending = await live.CountAsync(booking => booking.Status == requested, cancellationToken);

        return new BookingCounts(totals?.All ?? 0, totals?.Today ?? 0, active, pending);
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
