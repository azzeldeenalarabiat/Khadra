using Khadra.Domain.Common;

namespace Khadra.Domain.Bookings.Repositories;

public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(Id id, CancellationToken cancellationToken = default);

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

    // Drives the expiry and no-show background jobs.
    Task<IReadOnlyList<Booking>> ListDueForPaymentExpiryAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Booking>> ListDueForDecisionExpiryAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Booking>> ListDueForNoShowAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Booking>> ListDueForSettlementAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task AddAsync(Booking booking, CancellationToken cancellationToken = default);
}
