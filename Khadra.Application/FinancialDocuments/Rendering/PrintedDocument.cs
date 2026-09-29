using Khadra.Domain.Common;

namespace Khadra.Application.FinancialDocuments.Rendering;

/// <summary>A line of a printed document: its label, when it has one, and its value as printed.</summary>
public sealed record PrintedLine(string? Label, string Value);

/// <summary>A section of a printed document, in the order the snapshot stores it.</summary>
public sealed record PrintedSection(string Heading, IReadOnlyList<PrintedLine> Lines);

/// <summary>What the PDF says about itself outside the page: plain text, with no direction marks.</summary>
/// <param name="IssuedAt">The document's issue instant, which is also the file's creation date.</param>
public sealed record PrintedMetadata(string Title, string? Author, string Language, DateTimeOffset? IssuedAt);

/// <summary>
/// One issued document laid out for print in ONE language (payments Phase 6): the words and figures its stored
/// snapshot holds, picked for the language and formatted as the website formats them — all of them but the
/// commercial registrations, which the record keeps and the body leaves out (<c>DocumentPrintLayout.IsPrintedInBody</c>).
/// </summary>
/// <remarks>
/// <para>
/// Every text carries its own direction as Unicode isolates, exactly as the website renders it: the
/// composer's own isolates around a run inside a sentence are kept as stored, an amount is isolated left to
/// right, and a literal as registered is isolated by its first strong character when it holds an Arabic
/// letter and left to right otherwise. The page itself is mirrored for Arabic.
/// </para>
/// <para>
/// The only words that are the renderer's own are the page chrome ("Page 1 of 2") and a TEST document's
/// watermark. Everything else — every label, sentence and figure — is the issued record's.
/// </para>
/// </remarks>
public sealed record PrintedDocument(
    Language Language,
    string Number,
    string Title,
    string HeadlineLabel,
    string HeadlineAmount,
    IReadOnlyList<PrintedSection> Sections,
    string? TimeNote,
    string? Notice,
    string? Watermark,
    string PageWord,
    string OfWord,
    PrintedMetadata Metadata)
{
    /// <summary>Whether the page is laid out mirrored: Arabic reads right to left.</summary>
    public bool RightToLeft => Language == Language.Arabic;
}
