namespace Khadra.Domain.Auditing.Repositories;

/// <summary>
/// Write side of the document disclosure log (pre-launch item 86).
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
}
