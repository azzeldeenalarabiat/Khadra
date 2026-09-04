using Khadra.Application.Common;
using Khadra.Application.Disputes.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

internal sealed class DisputeAdminReader(KhadraDbContext context) : IDisputeAdminReader
{
    public async Task<PagedResult<DisputeListItem>> ListAsync(
        DisputeListFilter filter,
        PageRequest page,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        var open = DisputeStatus.Open;
        var underReview = DisputeStatus.UnderReview;
        var resolved = DisputeStatus.Resolved;
        var withdrawn = DisputeStatus.Withdrawn;

        var query = context.DisputeTickets.AsQueryable();
        var live = true;

        switch (filter.Status?.ToLowerInvariant())
        {
            case null or "live":
                query = query.Where(ticket => ticket.Status == open || ticket.Status == underReview);
                break;
            case "open":
                query = query.Where(ticket => ticket.Status == open);
                break;
            case "underreview":
                query = query.Where(ticket => ticket.Status == underReview);
                break;
            case "resolved":
                query = query.Where(ticket => ticket.Status == resolved);
                live = false;
                break;
            case "withdrawn":
                query = query.Where(ticket => ticket.Status == withdrawn);
                live = false;
                break;
            case "closed":
                query = query.Where(ticket => ticket.Status == resolved || ticket.Status == withdrawn);
                live = false;
                break;
            case "all":
                live = false;
                break;
            default:
                return PagedResult.Empty<DisputeListItem>(page.Page, page.PageSize);
        }

        if (filter.OverdueOnly)
        {
            query = query.Where(ticket =>
                (ticket.Status == open || ticket.Status == underReview) && ticket.SlaDeadline <= now);
        }

        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return PagedResult.Empty<DisputeListItem>(page.Page, page.PageSize);

        // A live queue is worked soonest-deadline first; a closed list is read newest first.
        var ordered = live
            ? query.OrderBy(ticket => ticket.SlaDeadline)
            : query.OrderByDescending(ticket => ticket.ClosedAt).ThenByDescending(ticket => ticket.OpenedAt);

        var items = await ordered
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(ticket => new DisputeListItem(
                ticket.Id.Value,
                ticket.BookingId.Value,
                context.Bookings
                    .Where(booking => booking.Id == ticket.BookingId)
                    .Select(booking => booking.Reference.Value)
                    .FirstOrDefault() ?? "—",
                context.Bookings
                    .Where(booking => booking.Id == ticket.BookingId)
                    .SelectMany(booking => context.Dealers
                        .Where(dealer => dealer.Id == booking.DealerId)
                        .Select(dealer => dealer.BusinessName.Value))
                    .FirstOrDefault() ?? "Dealer no longer on the platform",
                context.Bookings
                    .Where(booking => booking.Id == ticket.BookingId)
                    .SelectMany(booking => context.Users
                        .Where(user => user.Id == booking.CustomerId)
                        .Select(user => user.Name.Value))
                    .FirstOrDefault() ?? "Customer account closed",
                ticket.OpenedByParty.Name,
                ticket.Reason,
                ticket.Status.Name,
                ticket.OpenedAt,
                ticket.SlaDeadline,
                (ticket.Status == open || ticket.Status == underReview) && ticket.SlaDeadline <= now,
                ticket.AssignedAdminId != null ? ticket.AssignedAdminId.Value.Value : null,
                // An assigned ticket whose admin cannot be named is still assigned. Falling through
                // to null let the queue print "Unassigned" against a ticket someone already holds --
                // contradicting both its own "N unassigned" summary and the workspace, which says
                // "Account closed" for exactly this case. The dealer and customer above take the
                // same shape for the same reason.
                ticket.AssignedAdminId != null
                    ? context.Users
                        .Where(user => user.Id == ticket.AssignedAdminId.Value)
                        .Select(user => user.Name.Value)
                        .FirstOrDefault() ?? "Account closed"
                    : null,
                ticket.ClosedAt,
                ticket.Statements.Count))
            .ToListAsync(cancellationToken);

        return new PagedResult<DisputeListItem>(items, page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(
        IReadOnlyCollection<Id> userIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0)
            return new Dictionary<Guid, string>();

        var ids = userIds.ToList();
        return await context.Users
            .Where(user => ids.Contains(user.Id))
            .Select(user => new { user.Id, Name = user.Name.Value })
            .ToDictionaryAsync(row => row.Id.Value, row => row.Name, cancellationToken);
    }
}
