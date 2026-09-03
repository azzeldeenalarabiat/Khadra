using Khadra.Application.Disputes.ReadModels;
using Khadra.Domain.Disputes;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

internal sealed class DisputeDashboardReader(KhadraDbContext context) : IDisputeDashboardReader
{
    public async Task<DisputeCounts> CountsAsync(
        DateTimeOffset resolvedSince,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var open = DisputeStatus.Open;
        var underReview = DisputeStatus.UnderReview;
        var resolved = DisputeStatus.Resolved;

        var counts = await context.DisputeTickets
            .GroupBy(_ => 1)
            .Select(group => new DisputeCounts(
                group.Count(ticket => ticket.Status == open),
                group.Count(ticket => ticket.Status == underReview),
                // Overdue is judged against the deadline frozen when the ticket was opened, so
                // changing the SLA never retroactively breaches a promise already made.
                group.Count(ticket =>
                    (ticket.Status == open || ticket.Status == underReview) && ticket.SlaDeadline <= now),
                group.Count(ticket =>
                    ticket.Status == resolved && ticket.ClosedAt != null && ticket.ClosedAt >= resolvedSince)))
            .SingleOrDefaultAsync(cancellationToken);

        return counts ?? new DisputeCounts(0, 0, 0, 0);
    }

    public async Task<IReadOnlyList<LiveDispute>> LiveAsync(CancellationToken cancellationToken = default)
    {
        var open = DisputeStatus.Open;
        var underReview = DisputeStatus.UnderReview;

        return await context.DisputeTickets
            .Where(ticket => ticket.Status == open || ticket.Status == underReview)
            .OrderBy(ticket => ticket.SlaDeadline)
            .Select(ticket => new LiveDispute(
                ticket.Id,
                ticket.BookingId,
                ticket.Reason,
                ticket.OpenedAt,
                ticket.SlaDeadline,
                ticket.AssignedAdminId != null))
            .ToListAsync(cancellationToken);
    }
}
