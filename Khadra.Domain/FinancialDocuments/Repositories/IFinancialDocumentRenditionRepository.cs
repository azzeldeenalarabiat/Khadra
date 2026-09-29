using Khadra.Domain.Common;

namespace Khadra.Domain.FinancialDocuments.Repositories;

/// <summary>
/// The stored renderings of issued documents (payments Phase 6). Append-only: there is no update and no
/// removal here, and the database refuses them too.
/// </summary>
public interface IFinancialDocumentRenditionRepository
{
    /// <summary>
    /// The rendition a download serves: the one drawn with the newest template in this language, or null
    /// when the document has none yet.
    /// </summary>
    Task<FinancialDocumentRendition?> CurrentAsync(
        Id documentId,
        Language language,
        RenditionFormat format,
        CancellationToken cancellationToken = default);

    /// <summary>Every rendition of a document, oldest first: what the administrator's page lists.</summary>
    Task<IReadOnlyList<FinancialDocumentRendition>> ListForDocumentAsync(Id documentId, CancellationToken cancellationToken = default);

    void Add(FinancialDocumentRendition rendition);
}
