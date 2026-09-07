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
        var pendingPayment = BookingStatus.PendingPayment;
        return await WithChildren()
            .Where(booking => booking.Status == pendingPayment && booking.PaymentDeadline <= now)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Booking>> ListDueForDecisionExpiryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var requested = BookingStatus.Requested;
        return await WithChildren()
            .Where(booking => booking.Status == requested && booking.Period.Start <= now)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Booking>> ListDueForNoShowAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // Candidates: approved and past the start. The no-show timeout is per booking, in its Terms.
        var approved = BookingStatus.Approved;
        return await WithChildren()
            .Where(booking => booking.Status == approved && booking.Period.Start <= now)
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

    public async Task AddAsync(Booking booking, CancellationToken cancellationToken = default) =>
        await context.Bookings.AddAsync(booking, cancellationToken);

    private IQueryable<Booking> WithChildren() =>
        context.Bookings
            .Include(booking => booking.Handovers)
            .Include(booking => booking.StatusHistory);
}
