using Khadra.Domain.Common;

namespace Khadra.Domain.Auditing.Repositories;

/// <summary>
/// The document disclosure log (pre-launch item 86): written by every disclosure, and asked one question, whether an
/// administrator has opened an upload before rejecting it (Wave 4, W4-9).
/// </summary>
/// <remarks>
/// The same shape, and the same reasoning, as <see cref="IAuditTrail"/>: <c>Record</c> only STAGES
/// the entry, and the calling handler's <c>IUnitOfWork.SaveChangesAsync</c> commits it in the same
/// transaction as the thing being recorded. For a review that means the review row and its record
/// land together or not at all. For a view it means the row is committed BEFORE the bytes are
/// streamed, and a failure to write it fails the request — the platform does not release a private
/// document it cannot account for.
/// </remarks>
public interface IDocumentAccessLog
{
    void Record(DocumentAccessEntry entry);

    /// <summary>
    /// Whether this administrator has opened this upload of this person's document (Wave 4, W4-9): a rejection must
    /// name a file its author looked at. Matched on the upload's instant, because a document row is a slot whose file
    /// is replaced in place: having opened an earlier file of the same slot is not having opened this one.
    /// </summary>
    Task<bool> AdministratorHasViewedAsync(
        Id administratorUserId,
        Id subjectUserId,
        Id documentId,
        DateTimeOffset documentUploadedAt,
        CancellationToken cancellationToken = default);
}
