using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;

namespace Khadra.Application.FinancialDocuments.ReadModels;

/// <summary>
/// One PDF owed: an issued document, a language, and which kind — the document as issued, or (for a voided
/// document) its voided copy.
/// </summary>
public sealed record RenditionCandidate(Id DocumentId, Language Language, RenditionKind Kind);

/// <summary>
/// Finds the PDFs owed (payments Phase 6). Like issuing, the durable facts are the queue: an issued document
/// with no PDF rendition in a language IS the work, so nothing records that a PDF is due and nothing can be
/// lost — a pass that stops half way leaves the rest for the next.
/// </summary>
public interface IFinancialDocumentRenditionWorkReader
{
    /// <summary>
    /// The PDFs owed, oldest issue first, at most <paramref name="limit"/>: each document as issued, in English then
    /// Arabic, and for a voided document its voided copy in English then Arabic — only documents whose snapshot
    /// schema this build can read, since any other would be refused whole, and none of <paramref name="excluded"/>.
    /// </summary>
    /// <param name="excluded">PDFs this process already failed to draw; they wait for the next start.</param>
    Task<IReadOnlyList<RenditionCandidate>> ListAsync(
        IReadOnlyCollection<int> schemaVersions,
        IReadOnlyCollection<RenditionCandidate> excluded,
        int limit,
        CancellationToken cancellationToken = default);
}
