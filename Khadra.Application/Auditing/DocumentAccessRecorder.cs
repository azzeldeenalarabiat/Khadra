using Khadra.Application.Common;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.Auditing;

/// <summary>
/// Records that a private document was released to somebody, or reviewed by them (item 86).
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <see cref="AdminActionRecorder"/>, and it stages rather than saves for the same
/// reason: the record and the thing it records have to commit in ONE transaction. For a review that
/// is the review row. For a view it is the row on its own, committed before a single byte is sent,
/// so that a disclosure the platform could not account for never happens.
/// </para>
/// <para>
/// The actor is read from <see cref="ICurrentActor"/> — the validated token — and never from
/// anything the caller sent. That is the whole reason reviewer identity and timestamp cannot be
/// forged from a browser: neither is an input.
/// </para>
/// </remarks>
public sealed class DocumentAccessRecorder(IDocumentAccessLog log, ICurrentActor actor, IClock clock)
{
    /// <param name="subjectUserId">The renter the document belongs to, read off the booking.</param>
    /// <param name="documentUploadedAt">
    /// Which UPLOAD this concerns. A document row's file is replaced in place when the renter
    /// re-photographs it, so without this the log would appear to describe whichever file is there
    /// when somebody eventually reads it.
    /// </param>
    public void Record(
        DocumentAccessAction action,
        Id dealerId,
        Id bookingId,
        Id subjectUserId,
        Id documentId,
        CustomerDocumentType documentType,
        DateTimeOffset documentUploadedAt)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(documentType);

        // Both routes sit behind an authenticated dealer-staff policy, so there is always an actor.
        // Throwing rather than falling back to "system" is deliberate: an unattributed disclosure is
        // not a lesser record, it is a useless one, and this would only fire on a wiring mistake.
        if (actor.UserId is not { } actorUserId || actor.Role is not { } role)
            throw new InvalidOperationException("A document access entry requires an authenticated actor.");

        log.Record(DocumentAccessEntry.Record(
            action,
            actorUserId,
            actor.Name ?? "Unknown",
            role,
            dealerId,
            bookingId,
            subjectUserId,
            documentId,
            documentType,
            documentUploadedAt,
            clock.UtcNow,
            actor.CorrelationId));
    }
}
