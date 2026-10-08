using Khadra.Domain.Auditing;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Persistence;

namespace Khadra.Infrastructure.Reporting;

/// <summary>One audit entry as both audit readers read it: its own columns, and the booking it is about.</summary>
/// <param name="Booking">
/// The value object, not its string: <c>Reference</c> is stored through a value converter, and asking
/// SQL for <c>.Value</c> on it cannot be translated. EF then evaluated the lookup on the client, as a
/// join over a ROW_NUMBER() window across every booking on every page read. Selected whole, it stays
/// one primary-key lookup per row; the readers unwrap it after the read.
/// </param>
internal sealed record AuditRow(
    Id Id,
    DateTimeOffset OccurredAt,
    Id? ActorUserId,
    string ActorName,
    string? ActorRole,
    string Action,
    string EntityType,
    Id? EntityId,
    string SubjectLabel,
    string? PreviousValue,
    string? NewValue,
    string? Reason,
    string? CorrelationId,
    BookingReference? Booking,
    string? SubjectLabelAr);

/// <summary>
/// The one projection the activity feed and the audit log share, so the two cannot disagree about
/// which booking an entry is about.
/// </summary>
/// <remarks>
/// <para>
/// The booking reference exists because disputes were labelled with the English sentence
/// "Dispute on KH-…" until 2026-09-27, in a table that refuses UPDATE. The console words a dispute
/// from this fact instead of from its label, so the entries written before then read in Arabic too —
/// and nothing is read back out of an English sentence to get there.
/// </para>
/// <para>
/// It is READ, not snapshotted, and that is safe only because of two facts about the domain: a
/// booking's reference is assigned once, when the booking is created, and nothing changes it; and
/// neither a booking nor a dispute ticket is ever deleted — neither is soft-deletable, and no
/// aggregate is hard-deleted. So the reference read today is the one the entry was written about.
/// A NAME has no such guarantee — a dealership is renamed, an account is closed — which is why names
/// stay snapshotted in the label and this lookup is for the reference alone. If either fact stops
/// holding, the reference has to become a column written with the entry.
/// </para>
/// </remarks>
internal static class AuditRows
{
    public static IQueryable<AuditRow> SelectRows(this IQueryable<AuditEntry> entries, KhadraDbContext context)
    {
        // Hoisted, the way the other readers hoist their statuses; the comparison runs on the stored name.
        var booking = AuditEntityType.Booking;
        var dispute = AuditEntityType.Dispute;

        return entries.Select(entry => new AuditRow(
            entry.Id,
            entry.OccurredAt,
            entry.ActorUserId,
            entry.ActorName,
            // Null for a background job. The client renders that as "System", not as a blank.
            entry.ActorRole == null ? null : entry.ActorRole.Name,
            entry.Action.Name,
            entry.EntityType.Name,
            entry.EntityId,
            entry.SubjectLabel,
            entry.PreviousValue,
            entry.NewValue,
            entry.Reason,
            entry.CorrelationId,
            // Null rather than a guess when nothing matches, and an entry with no id matches nothing.
            // For the reasons above it cannot happen; if it ever does, the console shows the label as
            // it was stored.
            entry.EntityType == dispute
                ? context.DisputeTickets
                    .Where(ticket => ticket.Id == entry.EntityId)
                    .SelectMany(ticket => context.Bookings
                        .Where(disputed => disputed.Id == ticket.BookingId)
                        .Select(disputed => disputed.Reference))
                    .FirstOrDefault()
                : entry.EntityType == booking
                    ? context.Bookings
                        .Where(own => own.Id == entry.EntityId)
                        .Select(own => own.Reference)
                        .FirstOrDefault()
                    : null,
            entry.SubjectLabelAr));
    }
}
