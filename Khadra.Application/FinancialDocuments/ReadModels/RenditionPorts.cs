using Khadra.Domain.Common;

namespace Khadra.Application.FinancialDocuments.ReadModels;

/// <summary>One PDF owed: an issued document, and a language it has no PDF in yet.</summary>
public sealed record RenditionCandidate(Id DocumentId, Language Language);

/// <summary>
/// Finds the PDFs owed (payments Phase 6). Like issuing, the durable facts are the queue: an issued document
/// with no PDF rendition in a language IS the work, so nothing records that a PDF is due and nothing can be
/// lost — a pass that stops half way leaves the rest for the next.
/// </summary>
public interface IFinancialDocumentRenditionWorkReader
{
    /// <summary>
    /// Documents with no PDF in English or in Arabic, oldest issue first, English before Arabic, at most
    /// <paramref name="limit"/> pairs — only documents whose snapshot schema this build can read, since any
    /// other would be refused whole, and none of <paramref name="excluded"/>.
    /// </summary>
    /// <param name="excluded">Pairs this process already failed to draw; they wait for the next start.</param>
    Task<IReadOnlyList<RenditionCandidate>> ListAsync(
        IReadOnlyCollection<int> schemaVersions,
        IReadOnlyCollection<RenditionCandidate> excluded,
        int limit,
        CancellationToken cancellationToken = default);
}
