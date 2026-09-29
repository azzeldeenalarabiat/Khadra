using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

/// <summary>
/// Finds the PDFs owed (payments Phase 6): every issued document with no PDF rendition in a language, whatever
/// its standing — a voided document's PDF is still the record as issued, for the administrator — oldest issue
/// first. Any template version counts as drawn: a new template draws documents from then on, and never
/// re-draws what was already handed out.
/// </summary>
internal sealed class FinancialDocumentRenditionWorkReader(KhadraDbContext context) : IFinancialDocumentRenditionWorkReader
{
    public async Task<IReadOnlyList<RenditionCandidate>> ListAsync(
        IReadOnlyCollection<int> schemaVersions,
        IReadOnlyCollection<RenditionCandidate> excluded,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schemaVersions);
        ArgumentNullException.ThrowIfNull(excluded);
        if (limit <= 0 || schemaVersions.Count == 0)
            return [];

        // Exactly DocumentPrintLayout.Languages, English then Arabic — two EXISTS rather than a query over the list,
        // because this shape is what PostgreSQL is proven to run (`PostgresFinancialDocumentRenditionTests`); a test
        // fails if that list and this pair ever part.
        var (english, arabic, pdf) = (Language.English, Language.Arabic, RenditionFormat.Pdf);
        var readable = schemaVersions.ToList();

        // Each excluded pair can hide at most one pair, and each document owes at least one: reading that many
        // more documents than the limit is what guarantees a full pass whenever there is that much work.
        var rows = await context.FinancialDocuments
            .Where(document => readable.Contains(document.SnapshotSchemaVersion))
            .Select(document => new
            {
                document.Id,
                document.IssuedAt,
                HasEnglish = context.FinancialDocumentRenditions.Any(rendition =>
                    rendition.DocumentId == document.Id && rendition.Format == pdf && rendition.Language == english),
                HasArabic = context.FinancialDocumentRenditions.Any(rendition =>
                    rendition.DocumentId == document.Id && rendition.Format == pdf && rendition.Language == arabic),
            })
            .Where(row => !row.HasEnglish || !row.HasArabic)
            .OrderBy(row => row.IssuedAt)
            .ThenBy(row => row.Id)
            .Take(limit + excluded.Count)
            .ToListAsync(cancellationToken);

        var skip = excluded.ToHashSet();
        return
        [
            .. rows
                .SelectMany(row => new[]
                {
                    row.HasEnglish ? null : new RenditionCandidate(row.Id, english),
                    row.HasArabic ? null : new RenditionCandidate(row.Id, arabic),
                })
                .OfType<RenditionCandidate>()
                .Where(candidate => !skip.Contains(candidate))
                .Take(limit),
        ];
    }
}
