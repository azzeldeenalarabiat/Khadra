using Khadra.Application.Common;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Common;

namespace Khadra.Application.Auditing;

/// <summary>
/// Records a privileged action against the record it changed.
///
/// Every admin action writes the same shape — who acted, what they acted on, the state before and
/// after, and on what grounds — and the only part that varies is the label and the two values. That
/// branch (an authenticated admin, or the platform when there somehow is not one) was written out
/// twice already, in <c>DealerReviewAuditor</c> and <c>DisputeAuditor</c>, and bookings, customers
/// and admin accounts would each have made a third, fourth and fifth copy of it.
///
/// This only STAGES the entry. The calling handler's own <c>SaveChangesAsync</c> commits it in the
/// same transaction as the change itself, so a recorded decision and an unrecorded one cannot
/// diverge — the rule CLAUDE.md states for the audit trail.
/// </summary>
public sealed class AdminActionRecorder(IAuditTrail auditTrail, ICurrentActor actor, IClock clock)
{
    /// <param name="label">
    /// How an admin reading the log would recognise the record — a business name, a booking
    /// reference. For a Customer it is a REFERENCE, never a name, email or phone: the audit table is
    /// append-only and can never be erased, so identity written into it cannot be taken back out.
    /// </param>
    public void Record(
        AuditAction action,
        AuditEntityType entityType,
        Id entityId,
        string label,
        string? previousValue,
        string? newValue,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(entityType);

        // An admin action always has an authenticated actor; if that ever stops being true the entry
        // is still written, attributed to the system, rather than silently skipped.
        var entry = actor.UserId is { } actorId && actor.Role is { } role
            ? AuditEntry.By(
                actorId,
                actor.Name ?? "Unknown admin",
                role,
                action,
                entityType,
                entityId,
                label,
                clock.UtcNow,
                previousValue,
                newValue,
                reason,
                actor.CorrelationId)
            : AuditEntry.BySystem(
                action,
                entityType,
                entityId,
                label,
                clock.UtcNow,
                previousValue,
                newValue,
                reason,
                actor.CorrelationId);

        auditTrail.Record(entry);
    }
}
