using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence.Repositories;

// Write-side repository: every read loads the WHOLE booking, handovers and status history included,
// because the caller is about to transition it and the aggregate appends to both. Lists for screens
// go through the reader ports, never through here.
//
// The "due for" queries return CANDIDATES, not verdicts. The windows they are judged against (payment
// deadline aside) are frozen inside each booking's own Terms document, so the database can only say
// "this one might be due"; the domain method then applies that booking's own rule and refuses if it
// is too early. A job that trusted the query alone would judge every booking against one number.
internal sealed class BookingRepository(KhadraDbContext context) : IBookingRepository
{
    public Task<Booking?> GetByIdAsync(Id id, CancellationToken cancellationToken = default) =>
        WithChildren().SingleOrDefaultAsync(booking => booking.Id == id, cancellationToken);

    public Task<Booking?> GetByReferenceAsync(BookingReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return WithChildren().SingleOrDefaultAsync(booking => booking.Reference == reference, cancellationToken);
    }

    public Task<bool> HasOverlappingBookingAsync(
        Id vehicleId,
        DateRange period,
        TimeSpan turnaroundBuffer,
        DateTimeOffset now,
        Id? excludingBookingId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(period);

        // What this rental would claim: its period, opened earlier by the gap the gallery needs to
        // turn the car around. The stored side of the comparison already carries its own frozen gap
        // in HoldStart, which is why only the candidate is padded here.
        var candidateHoldStart = period.Start.Subtract(turnaroundBuffer);

        return BookingHolds
            .Colliding(context.Bookings, now, candidateHoldStart, period.End)
            .AnyAsync(
                booking =>
                    booking.VehicleId == vehicleId &&
                    (excludingBookingId == null || booking.Id != excludingBookingId.Value),
                cancellationToken);
    }

    public async Task<IReadOnlyList<Booking>> ListDueForPaymentExpiryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // Approved, and the customer let the payment window close.
        var approved = BookingStatus.Approved;
        return await WithChildren()
            .Where(booking => booking.Status == approved && booking.PaymentDeadline <= now)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Booking>> ListDueForDecisionExpiryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // The dealer let their ANSWER window close. This used to wait for the rental period to
        // arrive, which was harmless while a deposit gated the hold and is not now: a request costs
        // nothing, so without a real window one account could hold a car for the booking horizon.
        var requested = BookingStatus.Requested;
        return await WithChildren()
            .Where(booking => booking.Status == requested && booking.DecisionDeadline <= now)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Booking>> ListDueForNoShowAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // Candidates: CONFIRMED and past the start. Approved-but-unpaid is not a no-show -- nobody
        // failed to collect a car they had not paid for, and the aggregate refuses it anyway. The
        // no-show timeout itself is per booking, in its Terms.
        var confirmed = BookingStatus.Confirmed;
        return await WithChildren()
            .Where(booking => booking.Status == confirmed && booking.Period.Start <= now)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Booking>> ListDueForSettlementAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // Candidates: returned. The settlement window is per booking, in its Terms.
        var returned = BookingStatus.Returned;
        return await WithChildren()
            .Where(booking => booking.Status == returned && booking.ReturnedAt <= now)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Booking>> ListStaleHoldsForVehicleAsync(
        Id vehicleId,
        DateRange candidatePeriod,
        TimeSpan turnaroundBuffer,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidatePeriod);

        // The two ListDueFor* predicates above, taken together because the caller does not care
        // WHICH clock ran out -- only that the row still sits in the exclusion constraint's index
        // while the application has stopped counting it.
        //
        // Written out rather than composed from those methods: each returns a materialised list, and
        // a caller that wants one vehicle should not pull the whole platform's expiries into memory
        // to filter them.
        var requested = BookingStatus.Requested;
        var approved = BookingStatus.Approved;

        // The same half-open overlap the guard and the constraint use, against the same padded
        // window, so this returns exactly the rows that could refuse the caller's insert.
        var candidateHoldStart = candidatePeriod.Start.Subtract(turnaroundBuffer);
        var candidateEnd = candidatePeriod.End;

        return await WithChildren()
            .Where(booking =>
                booking.VehicleId == vehicleId &&
                booking.HoldStart < candidateEnd &&
                booking.Period.End > candidateHoldStart &&
                ((booking.Status == requested && booking.DecisionDeadline <= now) ||
                 (booking.Status == approved && booking.PaymentDeadline <= now)))
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Booking booking, CancellationToken cancellationToken = default) =>
        await context.Bookings.AddAsync(booking, cancellationToken);

    private IQueryable<Booking> WithChildren() =>
        context.Bookings
            .Include(booking => booking.Handovers)
            .Include(booking => booking.StatusHistory);
}
