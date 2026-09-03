using Khadra.Domain.Common;

namespace Khadra.Domain.Bookings.Repositories;

public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(Id id, CancellationToken cancellationToken = default);

    Task<Booking?> GetByReferenceAsync(BookingReference reference, CancellationToken cancellationToken = default);

    // The overlap guard. A database exclusion constraint backs this up, because a check-then-act in
    // application code loses the race between two customers booking the same car for the same dates.
    Task<bool> HasOverlappingBookingAsync(
        Id vehicleId,
        DateRange period,
        Id? excludingBookingId,
        CancellationToken cancellationToken = default);

    // Drives the expiry and no-show background jobs.
    Task<IReadOnlyList<Booking>> ListDueForPaymentExpiryAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Booking>> ListDueForDecisionExpiryAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Booking>> ListDueForNoShowAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Booking>> ListDueForSettlementAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task AddAsync(Booking booking, CancellationToken cancellationToken = default);
}
