using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence.Repositories;

// Write-side repository: every read loads the WHOLE booking -- handovers, status history and the
// dealership's document reviews -- because the caller is about to transition it and the aggregate
// appends to them. Lists for screens go through the reader ports, never through here.
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

    public async Task<IReadOnlyList<Id>> ListIdsDueForPaymentExpiryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // Approved, and the customer let the payment window close.
        var approved = BookingStatus.Approved;
        return await Ids(context.Bookings
            .Where(booking => booking.Status == approved && booking.PaymentDeadline <= now), cancellationToken);
    }

    public async Task<IReadOnlyList<Id>> ListIdsDueForDecisionExpiryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // The dealer let their ANSWER window close. This used to wait for the rental period to
        // arrive, which was harmless while a deposit gated the hold and is not now: a request costs
        // nothing, so without a real window one account could hold a car for the booking horizon.
        var requested = BookingStatus.Requested;
        return await Ids(context.Bookings
            .Where(booking => booking.Status == requested && booking.DecisionDeadline <= now), cancellationToken);
    }

    public async Task<IReadOnlyList<Id>> ListIdsDueForNoShowAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // Candidates: CONFIRMED and past the start. Approved-but-unpaid is not a no-show -- nobody
        // failed to collect a car they had not paid for, and the aggregate refuses it anyway. The
        // no-show timeout itself is per booking, in its Terms.
        var confirmed = BookingStatus.Confirmed;
        return await Ids(context.Bookings
            .Where(booking => booking.Status == confirmed && booking.Period.Start <= now), cancellationToken);
    }

    public async Task<IReadOnlyList<Id>> ListIdsDueForSettlementAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // Candidates: returned, and past the window stored when it opened (Wave 4, B5; checklist 210) — or, for a
        // booking returned before the column existed, any returned one, as before. The window is per booking, in its
        // Terms, and Booking.Settle judges it again; this only stops re-reading the ones still inside it.
        var returned = BookingStatus.Returned;
        return await Ids(context.Bookings
            .Where(booking => booking.Status == returned && booking.ReturnedAt <= now)
            .Where(WindowClosedOrUnknown(now)), cancellationToken);
    }

    /// <summary>
    /// The window stored when an ending opened it has closed by <paramref name="now"/> — or no window is stored, for a
    /// booking that ended before the column existed (Wave 4, B5; checklist 210). Never later than before, and the
    /// aggregate still judges each booking against its own frozen window.
    /// </summary>
    private static System.Linq.Expressions.Expression<Func<Booking, bool>> WindowClosedOrUnknown(DateTimeOffset now) =>
        booking =>
            EF.Property<DateTimeOffset?>(booking, Configurations.Bookings.BookingConfiguration.DisputeWindowEndsAtField) == null ||
            EF.Property<DateTimeOffset?>(booking, Configurations.Bookings.BookingConfiguration.DisputeWindowEndsAtField) <= now;

    /// <summary>The sweep's candidates as ids, oldest booking first, so a pass works through them in a stable order.</summary>
    private static async Task<IReadOnlyList<Id>> Ids(IQueryable<Booking> due, CancellationToken cancellationToken) =>
        await due
            .OrderBy(booking => booking.CreatedAt)
            .ThenBy(booking => booking.Id)
            .Select(booking => booking.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Booking>> ListStaleHoldsForVehicleAsync(
        Id vehicleId,
        DateRange candidatePeriod,
        TimeSpan turnaroundBuffer,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidatePeriod);

        // The two expiry predicates above, taken together because the caller does not care
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

    public async Task<IReadOnlyList<Id>> ListIdsDueForDepositReleaseAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // Everything that decides a deposit FOR GOOD is excluded here, in SQL, so a cancelled booking
        // is not re-read every minute for the rest of time (Cancelled and NoShow are terminal): a refund
        // that returned or released the deposit, a claim on it (any dispute not withdrawn), and a
        // penalty against the customer. What is left is the handful still inside their window and the
        // few the next pass releases. Joined by id, never navigated: Payments and Disputes are other
        // contexts. The window itself is frozen per booking, in its Terms; the aggregate judges it.
        var cancelled = BookingStatus.Cancelled;
        var noShow = BookingStatus.NoShow;
        var customer = BookingParty.Customer;
        var freeCancellation = RefundReason.FreeCancellation;
        var platformCancellation = RefundReason.PlatformCancellation;
        var released = RefundReason.DisputeWindowClosed;
        var withdrawn = DisputeStatus.Withdrawn;

        return await Ids(context.Bookings
            .Where(booking =>
                (booking.Status == cancelled || booking.Status == noShow) &&
                booking.PickedUpAt == null &&
                booking.DepositPaymentId != null &&
                booking.FinishedAt != null &&
                booking.FinishedAt <= now &&
                (booking.Penalty == null ||
                 booking.Penalty.AttributedTo != customer ||
                 booking.Penalty.MaxAmount.Amount == 0m) &&
                !context.Set<Refund>().Any(refund =>
                    refund.PaymentId == booking.DepositPaymentId &&
                    (refund.Reason == freeCancellation || refund.Reason == platformCancellation || refund.Reason == released)) &&
                !context.DisputeTickets.Any(ticket => ticket.BookingId == booking.Id && ticket.Status != withdrawn))
            .Where(WindowClosedOrUnknown(now)),
            cancellationToken);
    }

    public async Task AddAsync(Booking booking, CancellationToken cancellationToken = default) =>
        await context.Bookings.AddAsync(booking, cancellationToken);

    private IQueryable<Booking> WithChildren() =>
        context.Bookings
            .Include(booking => booking.Handovers)
            .Include(booking => booking.StatusHistory)
            // The dealership's document reviews. Loaded with the rest because the aggregate refuses a
            // duplicate review by looking at this collection, and a check against a collection EF
            // never filled would pass every time.
            .Include(booking => booking.RenterDocumentReviews);
}
