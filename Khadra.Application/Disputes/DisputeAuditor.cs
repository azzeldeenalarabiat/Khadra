using System.Globalization;
using Khadra.Application.Common;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
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
        // something anyone remembers. It is stored bare, as every other writer stores a reference or a
        // name, and the console words it from the entry's type ("Dispute on KH-…", "نزاع على الحجز
        // KH-…"). Entries written before 2026-09-27 hold the English sentence "Dispute on KH-…" and
        // always will, since the table refuses UPDATE; the readers send the booking reference beside
        // the label as its own fact, so the console never has to read it back out of either shape.
        var subject = booking.Reference.Value;

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
            $"Resolved: of {Figure(deposit.DepositHeld)} {currency} held, " +
            $"refund {Figure(deposit.RefundToCustomer)}, platform {Figure(deposit.RetainedByPlatform)}, " +
            $"dealer {Figure(deposit.TransferredToDealer)}";

        return resolution.DealerCharge is { } charge
            ? $"{summary}; dealer charged {Figure(charge)} {charge.CurrencyCode}"
            : summary;
    }

    // Every figure at the currency's full scale and in the invariant culture: this line is stored for ever
    // in an append-only table, so it cannot depend on the culture of whichever server wrote it, and it must
    // read 1.500 like every other amount on the platform rather than the scale the admin typed (E2E F36).
    private static string Figure(Money money) =>
        Money.AtScale(money.Amount).ToString(CultureInfo.InvariantCulture);
}
