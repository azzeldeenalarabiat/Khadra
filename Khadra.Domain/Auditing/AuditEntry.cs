using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Domain.Auditing;

// One immutable record of a privileged action.
//
// Three deliberate decisions:
//
// - The actor name and role are snapshotted, not looked up. An audit line must still read correctly
//   after the admin is renamed, changes role, or has their account soft-deleted. SubjectLabel is
//   snapshotted for the same reason: "approved dealer Aqaba Coast Cars" has to survive a rename.
// - There are no mutators and no public constructor. Immutability is enforced again in
//   KhadraDbContext.SaveChangesAsync and by a database trigger, because an audit trail the
//   application can quietly rewrite is not an audit trail.
// - PreviousValue and NewValue are short display strings, never serialised entities. Nothing secret
//   (hashes, tokens, contact details) is written here; every admin can read this table.
public sealed class AuditEntry : AggregateRoot, IAppendOnly
{
    public const int MaxValueLength = 400;
    public const int MaxReasonLength = 1000;
    public const int MaxLabelLength = 200;

    public DateTimeOffset OccurredAt { get; private set; }
    // Null when a background job acted rather than a person.
    public Id? ActorUserId { get; private set; }
    public string ActorName { get; private set; } = null!;
    public UserRole? ActorRole { get; private set; }
    public AuditAction Action { get; private set; } = null!;
    public AuditEntityType EntityType { get; private set; } = null!;
    public Id? EntityId { get; private set; }
    // What an admin would recognise in the UI: a dealer name, a booking reference, a setting key.
    public string SubjectLabel { get; private set; } = null!;
    public string? PreviousValue { get; private set; }
    public string? NewValue { get; private set; }
    public string? Reason { get; private set; }
    public string? CorrelationId { get; private set; }

    private AuditEntry()
    {
    }

    private AuditEntry(Id id) : base(id)
    {
    }

    public static AuditEntry By(
        Id actorUserId,
        string actorName,
        UserRole actorRole,
        AuditAction action,
        AuditEntityType entityType,
        Id? entityId,
        string subjectLabel,
        DateTimeOffset occurredAt,
        string? previousValue = null,
        string? newValue = null,
        string? reason = null,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(actorRole);
        if (actorUserId.IsEmpty)
            throw new DomainException("An attributed audit entry requires an actor.");

        return Build(
            actorUserId, actorName, actorRole, action, entityType, entityId, subjectLabel,
            occurredAt, previousValue, newValue, reason, correlationId);
    }

    // Expiries, no-show sweeps and other scheduled work. Recorded with no actor rather than
    // attributed to whichever admin happened to be signed in.
    public static AuditEntry BySystem(
        AuditAction action,
        AuditEntityType entityType,
        Id? entityId,
        string subjectLabel,
        DateTimeOffset occurredAt,
        string? previousValue = null,
        string? newValue = null,
        string? reason = null,
        string? correlationId = null) =>
        Build(
            null, SystemActorName, null, action, entityType, entityId, subjectLabel,
            occurredAt, previousValue, newValue, reason, correlationId);

    public const string SystemActorName = "System";

    private static AuditEntry Build(
        Id? actorUserId,
        string actorName,
        UserRole? actorRole,
        AuditAction action,
        AuditEntityType entityType,
        Id? entityId,
        string subjectLabel,
        DateTimeOffset occurredAt,
        string? previousValue,
        string? newValue,
        string? reason,
        string? correlationId)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(entityType);
        if (string.IsNullOrWhiteSpace(actorName))
            throw new DomainException("An audit entry requires an actor name.");
        if (string.IsNullOrWhiteSpace(subjectLabel))
            throw new DomainException("An audit entry requires a subject label.");

        return new AuditEntry(Id.New())
        {
            OccurredAt = occurredAt,
            ActorUserId = actorUserId,
            ActorName = Clip(actorName, MaxLabelLength)!,
            ActorRole = actorRole,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            SubjectLabel = Clip(subjectLabel, MaxLabelLength)!,
            PreviousValue = Clip(previousValue, MaxValueLength),
            NewValue = Clip(newValue, MaxValueLength),
            Reason = Clip(reason, MaxReasonLength),
            CorrelationId = Clip(correlationId, 64)
        };
    }

    private static string? Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
