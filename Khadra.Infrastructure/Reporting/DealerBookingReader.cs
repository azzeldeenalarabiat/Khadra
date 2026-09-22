using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

// Facts only. Every method is scoped to one dealer by id and returns instants and amounts as stored;
// nothing here knows the Amman calendar or how Money rounds -- that is the handler's job.
internal sealed class DealerBookingReader(KhadraDbContext context) : IDealerBookingReader
{
    public async Task<DealerBookingCounts> CountsAsync(Id dealerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var requested = BookingStatus.Requested;
        var approved = BookingStatus.Approved;
        var confirmed = BookingStatus.Confirmed;
        var pickedUp = BookingStatus.PickedUp;

        // Six small indexed counts rather than one grouped query: a conditional aggregate over the
        // owned Period does not translate, and the dealer's scope is a few hundred rows at most.
        var mine = context.Bookings.Where(booking => booking.DealerId == dealerId);

        // Only the ones whose window is still open.
        //
        // `Status` alone was the bug. A request past its decision deadline and an approval past its
        // payment deadline are over the instant the clock says so -- the car is already free, and
        // `BookingHolds.Live` has always known it -- but the row reads Requested or Approved until
        // the settlement sweep gets to it. On Render's free tier the API sleeps after fifteen minutes
        // idle, so "until the sweep gets to it" can be hours, and this queue counted every one of
        // them as work waiting for the dealer.
        var live = mine.Where(BookingLapse.HasNotLapsedAt(now));

        var requestedCount = await live.CountAsync(booking => booking.Status == requested, cancellationToken);
        var oldest = requestedCount == 0
            ? null
            : await live.Where(booking => booking.Status == requested).MinAsync(booking => booking.RequestedAt, cancellationToken);
        // Counted apart, never summed into one "approved" figure: a car nobody has paid for is not
        // the same thing to a gallery as a rental going out on Tuesday.
        var awaitingDeposit = await live.CountAsync(booking => booking.Status == approved, cancellationToken);
        var confirmedCount = await mine.CountAsync(booking => booking.Status == confirmed, cancellationToken);
        var pickedUpCount = await mine.CountAsync(booking => booking.Status == pickedUp, cancellationToken);
        var overdue = await mine.CountAsync(booking => booking.Status == pickedUp && booking.Period.End < now, cancellationToken);

        return new DealerBookingCounts(requestedCount, oldest, awaitingDeposit, confirmedCount, pickedUpCount, overdue);
    }

    public async Task<DealerQueueSignature> QueueSignatureAsync(Id dealerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var mine = context.Bookings.Where(booking => booking.DealerId == dealerId);

        // ONE grouped round trip, over two indexed columns. It moves when a booking is created,
        // approved, rejected, paid for, cancelled, picked up or returned -- every change this
        // dealer's queue can show that the dealer did not make themselves, which is precisely the
        // set they currently find out about by reloading the browser.
        var byStatus = await mine
            .GroupBy(booking => booking.Status)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        // And one more, because a LAPSE changes the queue without changing a row. Counting only the
        // live ones is what makes this marker move when nothing was written at all -- without it a
        // dealer would sit looking at a request that died twenty minutes ago, and the console would
        // be certain it was up to date.
        var live = await mine.Where(BookingLapse.HasNotLapsedAt(now)).CountAsync(cancellationToken);

        // And the dispute flag, which is the one thing a row shows that moves WITHOUT its status
        // moving: a customer opens a ticket, or an admin resolves one, and the row gains or loses its
        // marker while reading Confirmed throughout. Counting by status alone would have missed it,
        // and the dealer would have been certain the queue was current.
        //
        // The same predicate the list itself uses (see `BookingReader`), so the marker and the rows
        // cannot disagree about what "live dispute" means.
        var open = DisputeStatus.Open;
        var underReview = DisputeStatus.UnderReview;
        var disputed = await mine
            .CountAsync(
                booking => context.DisputeTickets.Any(ticket =>
                    ticket.BookingId == booking.Id &&
                    (ticket.Status == open || ticket.Status == underReview)),
                cancellationToken);

        return new DealerQueueSignature(
            [.. byStatus.Select(entry => new DealerStatusCount(entry.Key.Name, entry.Count))],
            live,
            disputed);
    }

    public async Task<IReadOnlyList<UpcomingHandover>> UpcomingPickupsAsync(Id dealerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Approved AND confirmed. Listing only one would either have staff preparing cars for
        // customers who have not paid, or hide the ones who have.
        //
        // Lapse-filtered for the same reason as the counts above, and it matters more here: this is
        // the list staff work from. An approval whose payment window closed is a car nobody is
        // coming for, and the platform has already put it back on the market -- preparing it wastes
        // the morning, and worse, hides that the slot is free.
        var approved = BookingStatus.Approved;
        var confirmed = BookingStatus.Confirmed;
        return await Handovers(context.Bookings
                .Where(BookingLapse.HasNotLapsedAt(from))
                .Where(booking => booking.DealerId == dealerId &&
                                  (booking.Status == approved || booking.Status == confirmed) &&
                                  booking.Period.Start >= from && booking.Period.Start < to)
                .OrderBy(booking => booking.Period.Start), pickup: true, now: from)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UpcomingHandover>> UpcomingReturnsAsync(Id dealerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var pickedUp = BookingStatus.PickedUp;
        // Overdue ones too: a car that should already be back is the most urgent return of all.
        return await Handovers(context.Bookings
                .Where(booking => booking.DealerId == dealerId && booking.Status == pickedUp && booking.Period.End < to)
                .OrderBy(booking => booking.Period.End), pickup: false, now: from)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> HeldVehicleIdsAsync(Id dealerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        // Composed on BookingHolds.Live, the same predicate the catalogue's availability anti-join
        // uses, rather than a second spelling of the holding statuses. It used to be its own list,
        // and once the deadlines went in that made this screen contradict the catalogue by
        // construction: both windows are capped at the rental start, so a Requested or Approved
        // booking whose period has begun is necessarily past its own deadline. The old list counted
        // exactly those as held -- the dealer's dashboard would say a car was spoken for on the same
        // afternoon the customer app was offering it to somebody else.
        //
        // What Live cannot answer is which of those holds is on the car TODAY, so that stays here:
        //
        // - A RESERVATION holds the car only while now is inside its period. A booking next month
        //   holds the car next month.
        // - A COLLECTED car is a different question, and the period test used to be applied to it
        //   too. The car is physically with the customer, so the dates cannot decide it:
        //     * an overdue return (End < now) dropped out and the dashboard called the car
        //       available, on the same payload that was reporting it under "1 overdue";
        //     * a car handed over the evening before its period starts (Start > now -- RecordPickup
        //       has no time guard, deliberately) dropped out the same way.
        //   Both are exactly the days a dealer looks at this figure. PickedUp holds, full stop.
        var pickedUp = BookingStatus.PickedUp;

        return await BookingHolds
            .Live(context.Bookings.Where(booking => booking.DealerId == dealerId), now)
            .Where(booking => booking.Status == pickedUp ||
                              (booking.Period.Start <= now && booking.Period.End > now))
            .Select(booking => booking.VehicleId.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RevenueFact>> RevenueAsync(Id dealerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var pickedUp = BookingStatus.PickedUp;
        var returned = BookingStatus.Returned;
        var completed = BookingStatus.Completed;

        return await context.Bookings
            .Where(booking => booking.DealerId == dealerId &&
                              ((booking.Status == returned || booking.Status == completed) &&
                               booking.ReturnedAt >= from && booking.ReturnedAt < to
                               // In progress: earned nothing yet, but the console shows it separately.
                               || booking.Status == pickedUp))
            .Select(booking => new RevenueFact(
                booking.Id.Value,
                booking.Status.Name,
                booking.ReturnedAt ?? booking.PickedUpAt ?? booking.CreatedAt,
                booking.Pricing.RentalTotal.Amount,
                booking.Pricing.RentalTotal.CurrencyCode,
                booking.Terms.CommissionPercent.Value))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OccupancyFact>> OccupancyAsync(Id dealerId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var pickedUp = BookingStatus.PickedUp;
        var returned = BookingStatus.Returned;
        var completed = BookingStatus.Completed;

        return await context.Bookings
            .Where(booking => booking.DealerId == dealerId &&
                              (booking.Status == pickedUp || booking.Status == returned || booking.Status == completed) &&
                              booking.Period.Start < to && booking.Period.End > from)
            .Select(booking => new OccupancyFact(booking.VehicleId.Value, booking.Period.Start, booking.Period.End, booking.Status.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<DealerActivityEntry>> ActivityAsync(Id dealerId, PageRequest page, Id? actorUserId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        var dealerParty = BookingParty.Dealer;
        var query =
            from change in context.Set<BookingStatusChange>()
            join booking in context.Bookings on change.BookingId equals booking.Id
            where booking.DealerId == dealerId && change.ActorParty == dealerParty
            select new { change, booking };

        // "What I did", filtered in SQL rather than over a page. Filtering the fetched page in the
        // client would silently drop everything past the first 25 rows -- a personal record that is
        // quietly incomplete is worse than a shared one that is honest.
        if (actorUserId is { } actor)
            query = query.Where(row => row.change.ActorUserId == actor);

        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return PagedResult.Empty<DealerActivityEntry>(page.Page, page.PageSize);

        var items = await query
            // (occurred_at DESC, id DESC): occurred_at alone is not a total order, and a non-total
            // order lets a page boundary drop an entry -- the defect the audit log documents. The id
            // is UUIDv7, so it agrees with time rather than fighting it.
            .OrderByDescending(row => row.change.OccurredAt)
            .ThenByDescending(row => row.change.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(row => new DealerActivityEntry(
                row.booking.Id.Value,
                row.booking.Reference.Value,
                row.change.To.Name,
                row.change.From != null ? row.change.From.Name : null,
                row.change.ActorUserId != null ? row.change.ActorUserId.Value.Value : null,
                // Null for a change no person signed AND for an account that no longer resolves; the
                // id beside it tells the console which, and the console words both. The English
                // stand-ins that used to be written here reached Arabic screens untranslated.
                row.change.ActorUserId != null
                    ? context.Users.Where(user => user.Id == row.change.ActorUserId.Value).Select(user => user.Name.Value).FirstOrDefault()
                    : null,
                row.change.Reason,
                row.change.OccurredAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<DealerActivityEntry>(items, page.Page, page.PageSize, total);
    }

    private IQueryable<UpcomingHandover> Handovers(IQueryable<Booking> bookings, bool pickup, DateTimeOffset now) =>
        bookings.Select(booking => new UpcomingHandover(
            booking.Id.Value,
            booking.Reference.Value,
            booking.Status.Name,
            pickup ? booking.Period.Start : booking.Period.End,
            booking.PickupMethod.Name,
            booking.VehicleId.Value,
            // Null when the customer's account no longer resolves; the console words that case.
            context.Users.Where(user => user.Id == booking.CustomerId).Select(user => user.Name.Value).FirstOrDefault(),
            !pickup && booking.Period.End < now));
}
