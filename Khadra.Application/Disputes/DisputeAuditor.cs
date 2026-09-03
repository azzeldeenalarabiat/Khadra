using Khadra.Application.Common;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Bookings;
using Khadra.Domain.Disputes;

namespace Khadra.Application.Disputes;

/// <summary>
/// Records an Admin's act on a dispute: taking it on, and deciding it.
///
/// Opening a ticket is not recorded here on purpose. It is a customer's or dealer's action, not a
/// privileged one, and the ticket itself -- with its statements and timestamps -- is already the
/// record of it. The audit trail is for what the platform's own staff did.
///
/// Like DealerReviewAuditor, this only STAGES the entry; the handler's single SaveChangesAsync commits
/// it with the decision, so a resolution that happened and one that was logged cannot diverge.
/// </summary>
public sealed class DisputeAuditor(IAuditTrail auditTrail, ICurrentActor actor, IClock clock)
{
    public void Record(
        DisputeTicket ticket,
        Booking booking,
        AuditAction action,
        string previousStatus,
        string? newValue,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(action);

        // The booking reference is the label an admin would search the log by; a ticket id is not
        // something anyone remembers.
        var subject = $"Dispute on {booking.Reference.Value}";

        var entry = actor.UserId is { } actorId && actor.Role is { } role
            ? AuditEntry.By(
                actorId,
                actor.Name ?? "Unknown admin",
                role,
                action,
                AuditEntityType.Dispute,
                ticket.Id,
                subject,
                clock.UtcNow,
                previousStatus,
                newValue,
                reason,
                actor.CorrelationId)
            : AuditEntry.BySystem(
                action,
                AuditEntityType.Dispute,
                ticket.Id,
                subject,
                clock.UtcNow,
                previousStatus,
                newValue,
                reason,
                actor.CorrelationId);

        auditTrail.Record(entry);
    }

    /// <summary>
    /// The money decision in one line, for the audit log's "new value" column. Compact on purpose:
    /// the column is capped, and the full document lives on the ticket.
    /// </summary>
    public static string Describe(DisputeResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        var deposit = resolution.Deposit;
        var currency = deposit.DepositHeld.CurrencyCode;
        var summary =
            $"Resolved: of {deposit.DepositHeld.Amount} {currency} held, " +
            $"refund {deposit.RefundToCustomer.Amount}, platform {deposit.RetainedByPlatform.Amount}, " +
            $"dealer {deposit.TransferredToDealer.Amount}";

        return resolution.DealerCharge is { } charge
            ? $"{summary}; dealer charged {charge.Amount} {charge.CurrencyCode}"
            : summary;
    }
}
