using Khadra.Application.Common;
using Khadra.Domain.Common;

namespace Khadra.Application.Disputes.ReadModels;

/// <summary>One row of the Admin's dispute queue (spec 3.3).</summary>
public sealed record DisputeListItem(
    Guid TicketId,
    Guid BookingId,
    string BookingReference,
    string DealerName,
    string CustomerName,
    string OpenedByParty,
    string Reason,
    string Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset SlaDeadline,
    // Judged against the deadline frozen when the ticket was opened, so raising the SLA later never
    // retroactively breaches a promise already made.
    bool IsOverdue,
    Guid? AssignedAdminId,
    string? AssignedAdminName,
    DateTimeOffset? ClosedAt,
    int StatementCount);

/// <summary>
/// Which tickets. Status null means "live" -- the queue an admin works -- rather than "everything",
/// because everything is the one view nobody opens the screen for.
/// </summary>
public sealed record DisputeListFilter(string? Status, bool OverdueOnly);

/// <summary>
/// How the queue is shaped under the filters in force — for ALL of it, not the page on screen.
/// </summary>
/// <remarks>
/// The console used to count these from the rows it had. With a page size of 25 and ten tickets that
/// was right by accident; at thirty tickets it prints a page's overdue count beside a platform total
/// and nothing on screen says which is which. A figure the server already knows is the server's.
/// </remarks>
public sealed record DisputeQueueCounts(int Total, int Overdue, int Unassigned);

public interface IDisputeAdminReader
{
    Task<PagedResult<DisputeListItem>> ListAsync(
        DisputeListFilter filter,
        PageRequest page,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<DisputeQueueCounts> CountsAsync(
        DisputeListFilter filter,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Display names for a set of user ids. Missing ids are simply absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, string>> NamesAsync(
        IReadOnlyCollection<Id> userIds,
        CancellationToken cancellationToken = default);
}
