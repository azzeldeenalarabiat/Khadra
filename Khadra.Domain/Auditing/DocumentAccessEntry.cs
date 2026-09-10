using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Domain.Auditing;

/// <summary>What somebody did with a private document.</summary>
/// <remarks>
/// Two members, and adding a third should be a deliberate act. In particular there is no
/// <c>Listed</c>: the metadata endpoint fires on every booking-detail open without anyone pressing
/// anything, and recording that as a "view" would fill this table with disclosures nobody chose to
/// make. See pre-launch item 86 for the reasoning and the limit it accepts.
/// </remarks>
public sealed class DocumentAccessAction : Enumeration
{
    /// <summary>
    /// The bytes were released to this caller's session.
    /// </summary>
    /// <remarks>
    /// NOT "somebody looked at it". The server cannot know that, and nothing downstream should read
    /// it as though it could: the row means the file left the platform towards this session, which is
    /// the disclosure. It is written BEFORE the response body, because a row written afterwards could
    /// not be made atomic with the send.
    /// </remarks>
    public static readonly DocumentAccessAction Viewed = new(1, "Viewed");

    /// <summary>The dealership recorded that it had checked the document. Never "Khadra verified".</summary>
    public static readonly DocumentAccessAction Reviewed = new(2, "Reviewed");

    private DocumentAccessAction(int id, string name) : base(id, name)
    {
    }
}

/// <summary>
/// One immutable record that a private document was disclosed to, or reviewed by, somebody.
/// </summary>
/// <remarks>
/// <para>
/// Its own table rather than a row in <c>audit_entries</c>, and that is a readership decision rather
/// than a technical one. <c>AuditEntry</c> is the ADMIN's trail: its own doc comment says every admin
/// can read it, and <c>IAuditFeedReader</c> puts its newest rows on the dashboard's activity glance.
/// A handover screen opens three or four documents, so routine gallery views would turn that feed
/// into a list of licence openings. More importantly, this record exists to answer a question the
/// admin trail was never for -- "who looked at MY passport, and when" -- and that answer belongs to
/// the person the papers describe (pre-launch item 86).
/// </para>
/// <para>
/// <b>Nothing here can reach the file.</b> No storage key, no URL, no bucket, no bytes, no content
/// type. Every field is an id, an instant, or a name already snapshotted elsewhere for the same
/// reason. A disclosure log that carried the key would be a second way into the very documents it
/// exists to protect.
/// </para>
/// <para>
/// Append-only, enforced twice: <c>KhadraDbContext</c> refuses to modify or delete one, and a
/// database trigger refuses it again for anything that bypasses the application.
/// </para>
/// </remarks>
public sealed class DocumentAccessEntry : AggregateRoot, IAppendOnly
{
    public const int MaxActorNameLength = 200;

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Who did it. Never null: nothing on this table happens without a signed-in person.</summary>
    public Id ActorUserId { get; private set; }

    /// <summary>Their name at the time, so the line still reads after they leave the dealership.</summary>
    public string ActorName { get; private set; } = null!;

    public UserRole ActorRole { get; private set; } = null!;

    /// <summary>The dealership they were acting for.</summary>
    public Id DealerId { get; private set; }

    /// <summary>The booking that granted the access. The relationship IS the authorization.</summary>
    public Id BookingId { get; private set; }

    /// <summary>
    /// The renter whose papers these are.
    /// </summary>
    /// <remarks>
    /// Not one of the fields the requirement listed, and the one that makes the table worth having:
    /// "who has seen my documents" has to be an index scan on the customer's own id, not a join
    /// through <c>customer_documents</c> — which is exactly the table that would have been emptied by
    /// the time anybody asks. An id, never a name.
    /// </remarks>
    public Id SubjectUserId { get; private set; }

    public Id DocumentId { get; private set; }

    public CustomerDocumentType DocumentType { get; private set; } = null!;

    /// <summary>
    /// Which UPLOAD was disclosed.
    /// </summary>
    /// <remarks>
    /// A document row is a slot whose file is replaced in place when the renter re-photographs it, so
    /// the id alone does not say what was disclosed. Without this, a log entry from before a
    /// replacement would appear to describe the file that is there now.
    /// </remarks>
    public DateTimeOffset DocumentUploadedAt { get; private set; }

    public DocumentAccessAction Action { get; private set; } = null!;

    public string? CorrelationId { get; private set; }

    private DocumentAccessEntry()
    {
    }

    private DocumentAccessEntry(Id id) : base(id)
    {
    }

    public static DocumentAccessEntry Record(
        DocumentAccessAction action,
        Id actorUserId,
        string actorName,
        UserRole actorRole,
        Id dealerId,
        Id bookingId,
        Id subjectUserId,
        Id documentId,
        CustomerDocumentType documentType,
        DateTimeOffset documentUploadedAt,
        DateTimeOffset occurredAt,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(actorRole);
        ArgumentNullException.ThrowIfNull(documentType);
        if (actorUserId.IsEmpty)
            throw new DomainException("A document access entry requires an actor.");
        if (bookingId.IsEmpty || documentId.IsEmpty)
            throw new DomainException("A document access entry requires a booking and a document.");
        if (subjectUserId.IsEmpty)
            throw new DomainException("A document access entry requires the person the document is about.");
        if (string.IsNullOrWhiteSpace(actorName))
            throw new DomainException("A document access entry requires an actor name.");

        var trimmedName = actorName.Trim();
        return new DocumentAccessEntry(Id.New())
        {
            OccurredAt = occurredAt,
            ActorUserId = actorUserId,
            ActorName = trimmedName[..Math.Min(trimmedName.Length, MaxActorNameLength)],
            ActorRole = actorRole,
            DealerId = dealerId,
            BookingId = bookingId,
            SubjectUserId = subjectUserId,
            DocumentId = documentId,
            DocumentType = documentType,
            DocumentUploadedAt = documentUploadedAt,
            Action = action,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId)
                ? null
                : correlationId.Trim()[..Math.Min(correlationId.Trim().Length, 64)]
        };
    }
}
