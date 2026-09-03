using Khadra.Domain.Common;

namespace Khadra.Application.Disputes.ReadModels;

/// <summary>
/// Dispute headcounts. Open and UnderReview are reported separately because they mean different
/// things to an admin: nobody has picked it up yet, versus someone has.
/// </summary>
/// <param name="Open">Live and unassigned.</param>
/// <param name="UnderReview">Live and assigned to an admin.</param>
/// <param name="Overdue">Live and past the SLA deadline frozen when the ticket was opened.</param>
/// <param name="ResolvedInWindow">Resolved inside the configured recent window.</param>
public sealed record DisputeCounts(int Open, int UnderReview, int Overdue, int ResolvedInWindow);

/// <summary>A ticket an admin still owes a decision on.</summary>
public sealed record LiveDispute(
    Id TicketId,
    Id BookingId,
    string Reason,
    DateTimeOffset OpenedAt,
    DateTimeOffset SlaDeadline,
    bool IsAssigned);

public interface IDisputeDashboardReader
{
    Task<DisputeCounts> CountsAsync(
        DateTimeOffset resolvedSince,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LiveDispute>> LiveAsync(CancellationToken cancellationToken = default);
}
