using Khadra.Application.AdminDashboard;
using Khadra.Domain.Disputes;

namespace Khadra.Application.Disputes.Dtos;

/// <summary>
/// Where a dispute stands against its SLA, as the administrator's SLA panel colours it (E2E F42).
/// </summary>
/// <remarks>
/// The panel used to wear the overdue colour always, so a ticket closed in good time read like a breach. At risk
/// is the work queue's own rule (<see cref="AttentionQueueBuilder.IsAtRisk"/>) with the same configured
/// threshold, so the queue and the ticket cannot disagree about which disputes need attention. Administrators
/// only: the parties' copies carry null.
/// </remarks>
public static class DisputeSlaStates
{
    public const string Closed = "Closed";
    public const string OnTime = "OnTime";
    public const string AtRisk = "AtRisk";
    public const string Overdue = "Overdue";

    public static string For(DisputeTicket ticket, decimal warningThreshold, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        if (!ticket.Status.IsLive)
            return Closed;
        if (ticket.IsBreachingSla(now))
            return Overdue;
        return AttentionQueueBuilder.IsAtRisk(ticket.OpenedAt, ticket.SlaDeadline, warningThreshold, now)
            ? AtRisk
            : OnTime;
    }
}
