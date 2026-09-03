using Khadra.Application.Common;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Dealers;

namespace Khadra.Application.Dealers;

/// <summary>
/// Records an Admin's decision on a dealer.
///
/// Every one of the four review outcomes writes the same shape, so it lives in one place: the actor,
/// the business as an admin would recognise it, the status before and after, and the reason. Spec 3.2
/// makes these the actions that most need to be auditable, and 3.1's "three outcomes, not two" only
/// means anything if the console can show which one was chosen and why.
///
/// This only STAGES the entry. The calling handler's SaveChangesAsync commits it in the same
/// transaction as the change itself, so a recorded decision and an unrecorded one cannot diverge.
/// </summary>
public sealed class DealerReviewAuditor(IAuditTrail auditTrail, ICurrentActor actor, IClock clock)
{
    public void Record(Dealer dealer, AuditAction action, string previousStatus, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(dealer);
        ArgumentNullException.ThrowIfNull(action);

        // An admin action always has an authenticated actor; if that ever stops being true the entry
        // is still written, attributed to the system, rather than silently skipped.
        var entry = actor.UserId is { } actorId && actor.Role is { } role
            ? AuditEntry.By(
                actorId,
                actor.Name ?? "Unknown admin",
                role,
                action,
                AuditEntityType.Dealer,
                dealer.Id,
                dealer.BusinessName.Value,
                clock.UtcNow,
                previousStatus,
                DescribeStatus(dealer),
                reason,
                actor.CorrelationId)
            : AuditEntry.BySystem(
                action,
                AuditEntityType.Dealer,
                dealer.Id,
                dealer.BusinessName.Value,
                clock.UtcNow,
                previousStatus,
                DescribeStatus(dealer),
                reason,
                actor.CorrelationId);

        auditTrail.Record(entry);
    }

    /// <summary>
    /// What an admin reading the log would call the dealer's state. Suspension is reported ahead of
    /// verification because it is the condition that actually stops the business trading.
    /// </summary>
    public static string DescribeStatus(Dealer dealer)
    {
        ArgumentNullException.ThrowIfNull(dealer);
        return dealer.IsSuspended ? "Suspended" : dealer.VerificationStatus.Name;
    }
}
