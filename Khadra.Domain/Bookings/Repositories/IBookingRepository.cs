using Khadra.Domain.Common;

namespace Khadra.Domain.Bookings.Repositories;

public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(Id id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The booking as stored, NOT settled against the clock on load. Only for the one command whose act IS the
    /// settlement: an administrator's expiry (pre-launch item 232). Loaded settled, a lapsed booking already reads
    /// Expired by the system, and the administrator's own expiry can only be refused. Every other caller uses
    /// <see cref="GetByIdAsync"/>.
    /// </summary>
    Task<Booking?> GetByIdAsStoredAsync(Id id, CancellationToken cancellationToken = default);

    Task<Booking?> GetByReferenceAsync(BookingReference reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether anything already holds this vehicle across the candidate period.
    /// </summary>
    /// <remarks>
    /// A database exclusion constraint backs this up, because a check-then-act in application code
    /// loses the race between two customers booking the same car for the same dates. This is the
    /// friendly check that produces a sensible answer; the constraint is the floor that cannot be
    /// raced.
    ///
    /// The caller supplies the CURRENT turnaround gap rather than the repository reading it, because
    /// business numbers come from IBusinessRulesProvider and infrastructure has no business knowing
    /// one. It pads the candidate only; each stored booking already carries its own frozen gap.
    ///
    /// `now` decides whether an unpaid booking still holds anything: its claim dies with its payment
    /// deadline.
    /// </remarks>
    Task<bool> HasOverlappingBookingAsync(
        Id vehicleId,
        DateRange period,
        TimeSpan turnaroundBuffer,
        DateTimeOffset now,
        Id? excludingBookingId,
        CancellationToken cancellationToken = default);

    // Drive the settlement sweep. IDS, not aggregates (Wave 4, checklist 233): the sweep loads, acts on and saves ONE
    // booking at a time on a clean tracker, so a booking that fails costs only its own work — a list of aggregates
    // loaded together left every later save in the pass re-issuing a stale row after one conflict.
    Task<IReadOnlyList<Id>> ListIdsDueForPaymentExpiryAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Id>> ListIdsDueForDecisionExpiryAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Id>> ListIdsDueForNoShowAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Id>> ListIdsDueForSettlementAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paid bookings that ended before pickup whose deposit nothing has decided yet: candidates for
    /// the clean-close release (owner, 2026-09-26). The aggregate judges each against its own frozen
    /// window (<c>Booking.DepositReleasedOnCleanClose</c>).
    /// </summary>
    Task<IReadOnlyList<Id>> ListIdsDueForDepositReleaseAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bookings on one vehicle, overlapping one candidate window, whose hold has run out but whose
    /// status has not caught up.
    /// </summary>
    /// <remarks>
    /// The narrow twin of the two ListDueFor* queries above, and it exists for a reason those cannot
    /// serve: whatever creates a booking must clear these in its OWN transaction before it inserts.
    ///
    /// The database's exclusion constraint cannot mention <c>now()</c>, so a request past its
    /// decision deadline, or an approval past its payment deadline, still occupies the index.
    /// <c>BookingHolds.Live</c> correctly ignores both. The two therefore disagree: the guard says
    /// the car is free and the INSERT is refused. Expiring them first is what makes the application
    /// and the database describe the same world.
    ///
    /// It is scoped BOTH ways on purpose, and the window half matters as much as the vehicle half.
    /// Only a stale hold that overlaps the candidate can trip the constraint, so only those need
    /// settling — and a customer booking a car in March has no business ending someone's abandoned
    /// request for the same car in July, nor losing a race to another customer who was touching it.
    ///
    /// The candidate is padded by its caller with the CURRENT turnaround gap, exactly as the overlap
    /// guard pads it; each stored booking carries its own frozen gap in <c>HoldStart</c>.
    /// </remarks>
    Task<IReadOnlyList<Booking>> ListStaleHoldsForVehicleAsync(
        Id vehicleId,
        DateRange candidatePeriod,
        TimeSpan turnaroundBuffer,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task AddAsync(Booking booking, CancellationToken cancellationToken = default);
}
