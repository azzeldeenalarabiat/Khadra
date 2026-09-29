using Khadra.Application.FinancialDocuments.Rendering;
using Khadra.Domain.Common;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Khadra.Infrastructure.FinancialDocuments;

/// <summary>
/// Draws an issued document's printed layout as a PDF with QuestPDF (payments Phase 6): one A4 page flow per
/// language, mirrored for Arabic, set in the platform's own faces, with a TEST document watermarked and a voided
/// copy stamped VOID on every page.
/// </summary>
/// <remarks>
/// <para>
/// <b>Licence.</b> QuestPDF's Community licence, which the owner confirmed on 2026-09-29 that Khadra
/// qualifies for: consolidated annual gross revenue under USD 1,000,000 across entities under common
/// control, and neither public sector nor publicly traded. Setting it below is that statement written in
/// code; it is re-checked as revenue grows (docs/pre-launch-checklist.md), with 90 days to change licence
/// if eligibility lapses.
/// </para>
/// <para>
/// <b>Fonts.</b> Manrope, then Noto Kufi Arabic, chosen glyph by glyph — the app's own rule
/// (<c>khadra_theme.dart</c>): Latin and figures in Manrope and Arabic in Kufi, in both languages, because
/// Kufi carries Latin too and first would re-set every figure. They are embedded in this assembly (the API's
/// image copies no font and its base image has none) and are the ONLY faces: a host's own fonts would make a
/// PDF depend on where it was rendered.
/// </para>
/// <para>
/// <b>Direction.</b> Decided by the layout and carried by Unicode isolates in the text itself, which QuestPDF
/// resolves as the bidi algorithm does (a span's own direction does not isolate it); the page is mirrored for
/// Arabic.
/// </para>
/// </remarks>
internal sealed class QuestPdfFinancialDocumentRenderer : IFinancialDocumentPdfRenderer
{
    private const string FontResourcePrefix = "Khadra.FinancialDocuments.Fonts.";

    private static readonly string[] Faces = ["Manrope", "Noto Kufi Arabic"];

    // The customer design's colours (khadra_theme.dart): text, the accent green, the price green, muted text,
    // rules, the accent's lightest tint, and a translucent red for the TEST watermark. A voided copy is marked in
    // the design's reds for a document that is not valid — bad, badStrong and badTint — and its diagonal stamp is
    // the same red, translucent, so the record under it stays readable.
    private const string Ink = "#111827";
    private const string Accent = "#15803D";
    private const string Price = "#14532D";
    private const string Muted = "#6B7280";
    private const string Rule = "#E2E5E3";
    private const string Band = "#F0FDF4";
    private const string WatermarkInk = "#40DC2626";
    private const string VoidRed = "#DC2626";
    private const string VoidInk = "#991B1B";
    private const string VoidTint = "#FEE2E2";
    private const string VoidStampInk = "#4DDC2626";

    private static readonly string Version =
        "QuestPDF " + (typeof(Document).Assembly.GetName().Version?.ToString(3) ?? "unknown");

    private static readonly Lazy<bool> Configured = new(Configure, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<PdfRendererStatus> Status = new(ProbeOnce, LazyThreadSafetyMode.ExecutionAndPublication);

    public string RendererVersion => Version;

    public byte[] Render(PrintedDocument document) => Compose(document).GeneratePdf();

    public PdfRendererStatus Probe() => Status.Value;

    /// <summary>The document as QuestPDF composes it — the PDF is generated from this, and tests draw it as images.</summary>
    internal static Document Compose(PrintedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        EnsureConfigured();

        var metadata = new DocumentMetadata
        {
            Title = document.Metadata.Title,
            Author = document.Metadata.Author ?? string.Empty,
            Subject = DocumentPrintLayout.WithoutIsolates(document.Title),
            Keywords = document.Number,
            Language = document.Metadata.Language,
            Creator = "Khadra",
        };
        // The file is dated by the document it renders, not by the moment it was drawn — and a voided copy, which
        // pictures the document as of its void, is modified then.
        if (document.Metadata.IssuedAt is { } issuedAt)
            (metadata.CreationDate, metadata.ModifiedDate) = (issuedAt, document.Metadata.ModifiedAt ?? issuedAt);

        return Document
            .Create(container => container.Page(page => Page(page, document)))
            .WithMetadata(metadata)
            .WithSettings(new DocumentSettings { CompressDocument = true });
    }

    /// <summary>Sets the library up once per process: the licence, the platform's faces and nothing else.</summary>
    internal static void EnsureConfigured() => _ = Configured.Value;

    /// <summary>
    /// Draws one throwaway page in both scripts, right to left — never stored and never shown — to prove on
    /// THIS host that the faces load and the native library runs. A Linux image missing what the library needs
    /// fails here, once, rather than on every document owed.
    /// </summary>
    private static PdfRendererStatus ProbeOnce()
    {
        try
        {
            var probe = new PrintedDocument(
                Language.Arabic,
                "PROBE",
                "Probe اختبار",
                "Probe اختبار",
                DocumentPrintLayout.LeftToRight("0123456789"),
                [new PrintedSection("اختبار", [new PrintedLine("Probe", "اختبار 0123456789")])],
                TimeNote: null,
                Notice: null,
                DocumentPrintLayout.Watermark(Language.Arabic),
                "صفحة",
                "من",
                new PrintedMetadata("PROBE", null, Language.Arabic.Name, null));
            return Compose(probe).GeneratePdf().Length > 0
                ? new PdfRendererStatus(true, $"drawn with {Version} under the QuestPDF Community licence")
                : new PdfRendererStatus(false, "The probe page came out empty.");
        }
#pragma warning disable CA1031 // Whatever stops the probe page stops every page: it is reported, not thrown.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            return new PdfRendererStatus(false, $"{failure.GetType().Name}: {failure.Message}");
        }
    }

    private static bool Configure()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = false;
        QuestPDF.Settings.ThrowOnMissingFontFamilies = true;
        // A customer's own name may hold a script neither face covers; the PDF is still drawn. Tests turn this
        // on, so a character the platform's own words need and the faces lack fails a test instead of a record.
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;

        var assembly = typeof(QuestPdfFinancialDocumentRenderer).Assembly;
        var fonts = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(FontResourcePrefix, StringComparison.Ordinal))
            .ToList();
        if (fonts.Count == 0)
            throw new InvalidOperationException("The PDF renderer's fonts are not embedded in the assembly.");
        foreach (var name in fonts)
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"The embedded font {name} could not be read.");
            FontManager.RegisterFontFromStream(stream);
        }

        return true;
    }

    private static void Page(PageDescriptor page, PrintedDocument document)
    {
        page.Size(PageSizes.A4);
        page.MarginHorizontal(44);
        page.MarginVertical(40);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(style => style.FontFamily(Faces).FontSize(9.5f).FontColor(Ink).LineHeight(1.35f));
        if (document.RightToLeft)
            page.ContentFromRightToLeft();

        page.Header().Element(header => Header(header, document));
        page.Content().PaddingTop(18).Element(content => Body(content, document));
        page.Footer().Element(footer => Footer(footer, document));
        if (document.Void is { } voided)
        {
            // Across every page, and above TEST when the document is a test one: both words stay on the page.
            page.Foreground()
                .AlignCenter()
                .AlignMiddle()
                .Rotate(-32)
                .Column(stamp =>
                {
                    stamp.Item().AlignCenter().Text(voided.Stamp).FontSize(120).Bold().FontColor(VoidStampInk);
                    if (document.Watermark is { } test)
                        stamp.Item().AlignCenter().Text(test).FontSize(56).Bold().FontColor(WatermarkInk);
                });
        }
        else if (document.Watermark is { } watermark)
        {
            page.Foreground()
                .AlignCenter()
                .AlignMiddle()
                .Rotate(-32)
                .Text(watermark)
                .FontSize(110)
                .Bold()
                .FontColor(WatermarkInk);
        }
    }

    private static void Header(IContainer container, PrintedDocument document)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem(3).Column(title =>
                {
                    title.Item().Element(item => Write(item, document.Title, style => style.FontSize(20).Bold()));
                    title.Item().PaddingTop(2).Element(item => Write(
                        item, DocumentPrintLayout.LeftToRight(document.Number), style => style.FontSize(10.5f).SemiBold().FontColor(Accent)));
                    // A voided copy says so at the top of every page, at the start edge in either direction.
                    if (document.Void is { } voided)
                    {
                        title.Item().PaddingTop(6).Row(stamp =>
                        {
                            stamp.AutoItem().Border(1.2f).BorderColor(VoidRed).Background(VoidTint).PaddingHorizontal(10).PaddingVertical(2)
                                .Element(pill => Write(pill, voided.Stamp, style => style.FontSize(11).Bold().FontColor(VoidInk)));
                            stamp.RelativeItem();
                        });
                    }
                });
                row.ConstantItem(14);
                row.RelativeItem(2).Background(Band).Padding(10).Column(headline =>
                {
                    headline.Item().Element(item => Write(item, document.HeadlineLabel, style => style.FontSize(8.5f).FontColor(Muted)));
                    headline.Item().PaddingTop(2).Element(item => Write(
                        item, document.HeadlineAmount, style => style.FontSize(17).Bold().FontColor(Price)));
                });
            });
            column.Item().PaddingTop(12).LineHorizontal(1).LineColor(Accent);
        });
    }

    private static void Body(IContainer container, PrintedDocument document)
    {
        container.Column(column =>
        {
            column.Spacing(16);

            // Before anything else the document says: it is void, since when, and what replaced it.
            if (document.Void is { } voided)
            {
                column.Item().Background(VoidTint).Border(1.2f).BorderColor(VoidRed).PaddingVertical(8).PaddingHorizontal(10).Column(banner =>
                {
                    banner.Item().Element(item => Write(item, voided.Stamp, style => style.FontSize(12).Bold().FontColor(VoidInk)));
                    banner.Item().PaddingTop(2).Element(item => Write(item, voided.Notice, style => style.SemiBold().FontColor(VoidInk)));
                    if (voided.Replacement is { } replacement)
                        banner.Item().Element(item => Write(item, replacement, style => style.SemiBold().FontColor(VoidInk)));
                });
            }

            foreach (var section in document.Sections)
            {
                // A heading never ends a page alone: with too little room left, the section starts the next one.
                column.Item().EnsureSpace(70).Column(block =>
                {
                    block.Item().Element(item => Write(item, section.Heading, style => style.FontSize(10.5f).SemiBold().FontColor(Accent)));
                    block.Item().PaddingTop(3).PaddingBottom(2).LineHorizontal(0.6f).LineColor(Rule);
                    block.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(3);
                        });
                        foreach (var line in section.Lines)
                        {
                            if (line.Label is null)
                            {
                                table.Cell().ColumnSpan(2).PaddingVertical(3).Element(cell => Write(cell, line.Value));
                                continue;
                            }

                            table.Cell().PaddingVertical(3).PaddingHorizontal(2)
                                .Element(cell => Write(cell, line.Label, style => style.FontColor(Muted)));
                            table.Cell().PaddingVertical(3).PaddingHorizontal(2).Element(cell => Write(cell, line.Value));
                        }
                    });
                });
            }

            if (document.Notice is { } notice)
                column.Item().PaddingTop(4).Element(item => Write(item, notice, style => style.FontSize(8.5f).SemiBold().FontColor(Muted)));
            if (document.TimeNote is { } timeNote)
                column.Item().Element(item => Write(item, timeNote, style => style.FontSize(8.5f).FontColor(Muted)));
        });
    }

    private static void Footer(IContainer container, PrintedDocument document)
    {
        container.PaddingTop(10).BorderTop(0.6f).BorderColor(Rule).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Element(item => Write(item, DocumentPrintLayout.LeftToRight(document.Number), style => style.FontSize(8).FontColor(Muted)));
            row.RelativeItem().Text(text =>
            {
                text.AlignEnd();
                text.DefaultTextStyle(style => style.FontSize(8).FontColor(Muted));
                text.Span(document.PageWord + " ");
                text.CurrentPageNumber();
                text.Span(" " + document.OfWord + " ");
                text.TotalPages();
            });
        });
    }

    /// <summary>A text as the layout gave it — its direction already carried by its own isolates.</summary>
    private static void Write(IContainer container, string text, Func<TextStyle, TextStyle>? style = null)
    {
        container.Text(block =>
        {
            block.AlignStart();
            if (style is not null)
                block.DefaultTextStyle(style);
            block.Span(text);
        });
    }
}
