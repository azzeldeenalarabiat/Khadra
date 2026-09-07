using Khadra.Application.Auditing.ReadAuditLog;
using Khadra.Application.Common;
using Khadra.Domain.Common;

namespace Khadra.Application.Auditing.ReadModels;

/// <summary>
/// One line of the audit log, as an auditor reads it.
///
/// Wider than <see cref="ActivityEntry"/> on purpose. The dashboard's feed answers "what has been
/// happening"; this answers "who did this, when, and on what grounds" — so it carries the three
/// fields the feed leaves out and which are the whole reason the table exists:
///
///   - <paramref name="Reason"/>: the written justification an admin had to give. Without it the log
///     records that a dealer was suspended and not why, which settles nothing.
///   - <paramref name="PreviousValue"/> / <paramref name="NewValue"/>: the change itself.
///   - <paramref name="ActorRole"/>: the role held AT THE TIME, snapshotted on the entry. An admin
///     later demoted must still read as an admin on the line where they acted.
///
/// <paramref name="ActorUserId"/> is null when a background job acted rather than a person — an
/// expiry sweep, a no-show timeout. That is a real distinction an auditor must be able to see, not a
/// missing value to paper over.
/// </summary>
public sealed record AuditLogEntry(
    // A plain guid: this record is serialised straight to the client, and Id is a struct whose
    // Value/IsEmpty members would otherwise leak into the JSON.
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? ActorUserId,
    string ActorName,
    string? ActorRole,
    string Action,
    string EntityType,
    Guid? EntityId,
    string SubjectLabel,
    string? PreviousValue,
    string? NewValue,
    string? Reason,
    // Everything one request did shares this, so a support question about one incident can be
    // followed across the entries it produced.
    string? CorrelationId);

/// <summary>
/// Which slice of the log an admin is looking at.
///
/// Every field is optional and they compose: "what did Rania do to dealers last week" is three of
/// them at once. The window is half-open — from inclusive, before exclusive — because an admin
/// picking "to 3 September" means the whole of that day, and comparing against midnight at its START
/// would silently drop everything that happened during it.
/// </summary>
public sealed record AuditLogFilter(
    string? Action = null,
    string? EntityType = null,
    Id? ActorUserId = null,
    DateTimeOffset? OccurredFrom = null,
    /// <summary>EXCLUSIVE. The caller passes the start of the day after the one it wants included.</summary>
    DateTimeOffset? OccurredBefore = null,
    /// <summary>Free text over the subject label and the actor's name.</summary>
    string? Search = null,
    /// <summary>Everything recorded against one record, however it was reached.</summary>
    Id? EntityId = null,
    /// <summary>
    /// Only what a background job did, rather than a person.
    ///
    /// Its own flag because "the System" cannot be expressed as an actor id — it IS the absence of
    /// one. Without it, sweeps and expiries are the single class of entry an auditor cannot isolate,
    /// which is backwards: an action nobody was present for is the one most worth reviewing.
    /// </summary>
    bool? SystemOnly = null);

public interface IAuditLogReader
{
    /// <summary>
    /// One page of the log, newest first.
    ///
    /// The order is TOTAL — occurred_at then id — and that is not a detail. Two entries can share an
    /// instant: a handler records its action and its audit line in one transaction, off one clock
    /// read. Paging on a non-unique key leaves the database free to order ties differently between
    /// two queries, which silently drops a row at a page boundary or shows it twice. For most lists
    /// that is a cosmetic bug; in the record that exists to settle arguments it is the whole failure.
    /// Id is UUIDv7, so it is unique AND agrees with time order.
    /// </summary>
    Task<PagedResult<AuditLogEntry>> ListAsync(
        AuditLogFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Who appears in the log, for the "who did it" filter.
///
/// Its own port because the answer comes from the ENTRIES, not from the users table. The log is the
/// record of people who acted, and some of them have since left: an admin whose account was
/// deactivated still has to be findable, or the filter quietly hides their decisions. The name is the
/// one snapshotted on the entry at the time, for the same reason.
/// </summary>
public interface IAuditActorReader
{
    Task<IReadOnlyList<AuditActorDto>> ListAsync(CancellationToken cancellationToken = default);
}
