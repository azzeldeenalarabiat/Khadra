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

public interface IDisputeAdminReader
{
    Task<PagedResult<DisputeListItem>> ListAsync(
        DisputeListFilter filter,
        PageRequest page,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Display names for a set of user ids. Missing ids are simply absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, string>> NamesAsync(
        IReadOnlyCollection<Id> userIds,
        CancellationToken cancellationToken = default);
}
