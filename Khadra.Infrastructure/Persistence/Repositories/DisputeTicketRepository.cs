using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Disputes.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence.Repositories;

// Write-side repository: a ticket is always loaded with its statements, because adding one is the
// most common change and the aggregate decides whether a party may still speak.
internal sealed class DisputeTicketRepository(KhadraDbContext context) : IDisputeTicketRepository
{
    public Task<DisputeTicket?> GetByIdAsync(Id id, CancellationToken cancellationToken = default) =>
        WithChildren().SingleOrDefaultAsync(ticket => ticket.Id == id, cancellationToken);

    // SingleOrDefault on purpose. The partial unique index on (booking_id) WHERE status is live is
    // what makes "at most one" a database fact rather than a hope; if it ever stopped holding, an
    // exception here is the right outcome, not a silently chosen first row.
    public Task<DisputeTicket?> GetLiveByBookingAsync(Id bookingId, CancellationToken cancellationToken = default) =>
        Live().SingleOrDefaultAsync(ticket => ticket.BookingId == bookingId, cancellationToken);

    public Task<bool> HasLiveTicketAsync(Id bookingId, CancellationToken cancellationToken = default) =>
        Live().AnyAsync(ticket => ticket.BookingId == bookingId, cancellationToken);

    public async Task<IReadOnlyList<DisputeTicket>> ListLiveAsync(CancellationToken cancellationToken = default) =>
        await Live().OrderBy(ticket => ticket.SlaDeadline).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DisputeTicket>> ListBreachingSlaAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await Live()
            .Where(ticket => ticket.SlaDeadline <= now)
            .OrderBy(ticket => ticket.SlaDeadline)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(DisputeTicket ticket, CancellationToken cancellationToken = default) =>
        await context.DisputeTickets.AddAsync(ticket, cancellationToken);

    // DisputeStatus.IsLive spelled out for the query translator; the domain remains the definition.
    private IQueryable<DisputeTicket> Live()
    {
        var open = DisputeStatus.Open;
        var underReview = DisputeStatus.UnderReview;
        return WithChildren().Where(ticket => ticket.Status == open || ticket.Status == underReview);
    }

    private IQueryable<DisputeTicket> WithChildren() =>
        context.DisputeTickets.Include(ticket => ticket.Statements);
}
