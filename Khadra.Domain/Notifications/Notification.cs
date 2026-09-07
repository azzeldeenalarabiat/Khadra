using Khadra.Domain.Common;

namespace Khadra.Domain.Notifications;

// One thing worth telling one person, and whether they have seen it.
//
// Four deliberate decisions:
//
// - ONE ROW PER RECIPIENT. Read state is a fact about a person, not about an event: two colleagues
//   reading the same approval have seen it independently. A shared row plus a per-user marker would
//   need a join on every unread count, which the bell asks for on every navigation.
//
// - RAISED IN THE ACTION'S OWN TRANSACTION, never from a domain event. `UnitOfWork` dispatches
//   events AFTER commit with no outbox, so an event-fed write is at-most-once: the booking would be
//   approved and the notification silently absent. Same reasoning, and the same remedy, as
//   `IAuditTrail` — the handler stages it and its own SaveChangesAsync commits both together.
//
// - THE ACTOR'S NAME IS SNAPSHOTTED; the subject's identity is NOT stored at all. Snapshotting the
//   actor follows AuditEntry: "Ahmad approved KR-1042" has to still read correctly after Ahmad is
//   renamed or his account is soft-deleted. But the SUBJECT is only ever an id and a reference the
//   platform issued (`KR-1042`), never a customer's name — this table is never deleted from, and a
//   customer's name written into it would outlive every promise made about it.
//
// - NO SENTENCE IS STORED. A row carries a Kind and its parts; the screen composes the line. Storing
//   English would have to be rewritten to add Arabic, which is planned, and would freeze wording that
//   is not a fact about what happened.
public sealed class Notification : AggregateRoot, ISoftDeletable
{
    public const int MaxActorNameLength = 150;
    public const int MaxReferenceLength = 50;

    // Whose notification this is. Every query is scoped by it, and nothing else may read the row.
    public Id RecipientUserId { get; private set; }
    public NotificationKind Kind { get; private set; } = null!;
    // What it is about: a booking, a dispute, a dealership, an employment. Cross-context by id only.
    public Id? SubjectId { get; private set; }
    // What a person would recognise: a booking reference the platform issued. Never a personal name.
    public string? SubjectReference { get; private set; }
    // Who caused it. Null when the platform itself did (an expiry, a bootstrap).
    public Id? ActorUserId { get; private set; }
    public string ActorName { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    private Notification()
    {
    }

    private Notification(Id id) : base(id)
    {
    }

    public static Notification Raise(
        Id recipientUserId,
        NotificationKind kind,
        string actorName,
        DateTimeOffset occurredAt,
        Id? subjectId = null,
        string? subjectReference = null,
        Id? actorUserId = null)
    {
        ArgumentNullException.ThrowIfNull(kind);

        if (recipientUserId.IsEmpty)
            throw new DomainException("A notification requires a recipient.");
        if (string.IsNullOrWhiteSpace(actorName))
            throw new DomainException("A notification requires an actor name.");

        return new Notification(Id.New())
        {
            RecipientUserId = recipientUserId,
            Kind = kind,
            SubjectId = subjectId,
            SubjectReference = Trim(subjectReference, MaxReferenceLength),
            ActorUserId = actorUserId,
            ActorName = actorName.Trim()[..Math.Min(actorName.Trim().Length, MaxActorNameLength)],
            OccurredAt = occurredAt,
            IsDeleted = false
        };
    }

    /// <summary>Idempotent: reading twice does not move the moment it was first seen.</summary>
    public void MarkRead(DateTimeOffset now)
    {
        ReadAt ??= now;
    }

    /// <summary>
    /// Only the recipient may act on their own notification.
    ///
    /// Checked in the domain rather than only in the handler so that a future caller cannot mark
    /// someone else's row read by knowing its id.
    /// </summary>
    public bool BelongsTo(Id userId) => RecipientUserId == userId;

    public void Delete(DateTimeOffset now)
    {
        if (IsDeleted) return;
        IsDeleted = true;
        DeletedAt = now;
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed[..Math.Min(trimmed.Length, max)];
    }
}
