using Khadra.Domain.Common;

namespace Khadra.Domain.Disputes.Repositories;

public interface IDisputeTicketRepository
{
    Task<DisputeTicket?> GetByIdAsync(Id id, CancellationToken cancellationToken = default);

    // At most one live ticket per booking; a partial unique index enforces it in the database.
    Task<DisputeTicket?> GetLiveByBookingAsync(Id bookingId, CancellationToken cancellationToken = default);

    Task<bool> HasLiveTicketAsync(Id bookingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether anything claims this booking's deposit: a dispute that was not withdrawn — open, under
    /// review, or resolved (a resolution already decided the deposit, including by keeping it). A
    /// withdrawn ticket settles the booking "as if no dispute was raised" and claims nothing.
    /// </summary>
    Task<bool> HasClaimOnDepositAsync(Id bookingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The booking's RESOLVED tickets, oldest first, read-only and without their statements: what
    /// earlier decisions allocated is what a later dispute may no longer split (item 169).
    /// </summary>
    Task<IReadOnlyList<DisputeTicket>> ListResolvedForBookingAsync(Id bookingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every ticket that was not withdrawn — open, under review or resolved — on several bookings, in ONE
    /// read-only query without statements: for batches that run the calculator per booking.
    /// </summary>
    Task<IReadOnlyList<DisputeTicket>> ListClaimsForBookingsAsync(IReadOnlyCollection<Id> bookingIds, CancellationToken cancellationToken = default);

    // Feeds the Admin dashboard's open-dispute queue and the 48-hour SLA alert.
    Task<IReadOnlyList<DisputeTicket>> ListLiveAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DisputeTicket>> ListBreachingSlaAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task AddAsync(DisputeTicket ticket, CancellationToken cancellationToken = default);
}
