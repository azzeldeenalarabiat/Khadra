namespace Khadra.Application.FinancialDocuments.Rendering;

/// <summary>
/// Draws a document laid out for print as a PDF (payments Phase 6). The adapter owns the library, the page
/// and the fonts; what the page SAYS is decided before it, by <see cref="DocumentPrintLayout"/>, from the
/// stored snapshot alone.
/// </summary>
public interface IFinancialDocumentPdfRenderer
{
    /// <summary>What drew the bytes — the library and its version — recorded on every rendition.</summary>
    string RendererVersion { get; }

    /// <summary>The PDF's bytes. Throws only on a defect: a layout that could not be drawn.</summary>
    byte[] Render(PrintedDocument document);

    /// <summary>
    /// Whether this process can draw at all: its fonts load and its native library runs. Asked once and
    /// remembered — neither changes while the process lives — so the boot log can say so, and a host that
    /// cannot draw is not asked to draw every document owed and log each failure.
    /// </summary>
    PdfRendererStatus Probe();
}

/// <param name="Description">What the boot log says: the renderer and its licence, or why it cannot draw.</param>
public sealed record PdfRendererStatus(bool IsReady, string Description);
