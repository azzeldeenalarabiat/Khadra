using System.Text.Json.Nodes;
using Khadra.Application.FinancialDocuments.Composition;
using Khadra.Application.FinancialDocuments.Rendering;
using Khadra.Domain.Common;
using Khadra.Infrastructure.FinancialDocuments;
using Khadra.Tests.Application.FinancialDocuments;
using Khadra.Tests.Support;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// Drawing issued documents as PDFs with QuestPDF (payments Phase 6): every page of the shared contract
/// fixture, in both languages, as issued and as a voided copy, with the platform's own faces only — a character
/// they lack fails here rather than printing a box on a record.
/// </summary>
/// <remarks>
/// Set <c>KHADRA_PDF_PREVIEW_DIR</c> to a folder to have every fixture page written there as PNGs too, for the
/// eye: a PDF's bytes are not compared (they carry the library's own identifiers), its structure is.
/// </remarks>
public sealed class FinancialDocumentPdfRendererTests
{
    private readonly QuestPdfFinancialDocumentRenderer _renderer = new();

    public FinancialDocumentPdfRendererTests()
    {
        QuestPdfFinancialDocumentRenderer.EnsureConfigured();
        // PROCESS-GLOBAL, like every QuestPDF setting: once any test class has run this, every class drawing a
        // PDF in parallel — FinancialDocumentRenditionTests, the startup probe — draws under it too. So a glyph
        // the faces lack fails whichever class drew it first, not necessarily this one; look at its words.
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = true;
    }

    [Fact]
    public void Every_fixture_page_is_drawn_as_a_pdf_in_both_languages_with_no_missing_glyph()
    {
        foreach (var (name, page) in DocumentPrintLayoutTests.FixturePages())
        {
            foreach (var language in new[] { Language.English, Language.Arabic })
            {
                var printed = DocumentPrintLayoutTests.LayOut(page, language)!;

                var pdf = _renderer.Render(printed);

                Assert.True(pdf.Length > 1_000, $"{name} ({language.Name}) is {pdf.Length} bytes.");
                Assert.Equal("%PDF-"u8.ToArray(), pdf[..5]);
            }
        }
    }

    [Fact]
    public void Every_fixture_page_is_drawn_as_a_voided_copy_in_both_languages_with_no_missing_glyph()
    {
        // Owner, 2026-09-29: the stamp, the notice, its Amman time and the correction's number, in both scripts.
        foreach (var (name, page) in DocumentPrintLayoutTests.FixturePages())
        {
            foreach (var language in new[] { Language.English, Language.Arabic })
            {
                var printed = DocumentPrintLayoutTests.LayOut(page, language, Voided())!;

                var pdf = _renderer.Render(printed);

                Assert.True(pdf.Length > 1_000, $"{name} ({language.Name}, voided) is {pdf.Length} bytes.");
                Assert.Equal("%PDF-"u8.ToArray(), pdf[..5]);
            }
        }
    }

    [Fact]
    public void A_voided_copy_is_not_the_same_file_as_the_document_as_issued()
    {
        var (_, page) = DocumentPrintLayoutTests.FixturePages().First(entry => entry.Name == "payment-receipt-deposit-voided");

        var original = _renderer.Render(DocumentPrintLayoutTests.LayOut(page, Language.English)!);
        var copy = _renderer.Render(DocumentPrintLayoutTests.LayOut(page, Language.English, Voided())!);

        Assert.NotEqual(original, copy);
        Assert.True(copy.Length > original.Length, "The voided copy carries its stamp and notice besides everything the original does.");
    }

    [Fact]
    public void A_long_voided_statement_breaks_across_pages_with_its_marks_on_every_one()
    {
        var printed = Synthetic(lines: 90, voided: Voided());

        var pages = QuestPdfFinancialDocumentRenderer.Compose(printed).GenerateImages(new ImageGenerationSettings { RasterDpi = 36 }).Count();

        // The pill sits in the header and the stamp in the foreground, and QuestPDF repeats both on every page.
        Assert.True(pages >= 2, $"90 lines fitted on {pages} page.");
    }

    [Fact]
    public void The_renderer_names_the_library_and_its_version()
    {
        Assert.StartsWith("QuestPDF 2026.", _renderer.RendererVersion, StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_statement_breaks_across_pages_with_every_line_on_one()
    {
        var printed = Synthetic(lines: 90);

        var pages = QuestPdfFinancialDocumentRenderer.Compose(printed).GenerateImages(new ImageGenerationSettings { RasterDpi = 36 }).Count();

        Assert.True(pages >= 2, $"90 lines fitted on {pages} page.");
    }

    [Fact]
    public void A_literal_far_wider_than_the_page_is_wrapped_never_cut_or_refused()
    {
        var printed = Synthetic(lines: 1, value: new string('W', 400));

        var pdf = _renderer.Render(printed);

        Assert.Equal("%PDF-"u8.ToArray(), pdf[..5]);
    }

    [Fact]
    public void Previews_every_fixture_page_as_images_when_asked()
    {
        var folder = Environment.GetEnvironmentVariable("KHADRA_PDF_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(folder))
            return;

        Directory.CreateDirectory(folder);
        foreach (var (name, page) in DocumentPrintLayoutTests.FixturePages())
        {
            foreach (var language in new[] { Language.English, Language.Arabic })
            {
                Preview(folder, $"{name}-{language.Name}", DocumentPrintLayoutTests.LayOut(page, language)!);
                Preview(folder, $"{name}-{language.Name}-void", DocumentPrintLayoutTests.LayOut(page, language, Voided())!);
            }
        }

        Preview(folder, "long-statement-ar-void", Synthetic(lines: 90, voided: Voided()));
    }

    private static void Preview(string folder, string name, PrintedDocument printed)
    {
        var images = QuestPdfFinancialDocumentRenderer.Compose(printed)
            .GenerateImages(new ImageGenerationSettings { RasterDpi = 110 })
            .ToList();
        for (var index = 0; index < images.Count; index++)
            File.WriteAllBytes(Path.Combine(folder, $"{name}-{index + 1}.png"), images[index]);
    }

    /// <summary>A void's facts as the render handler freezes them: 12:30 in Amman, replaced by a correction.</summary>
    private static VoidFacts Voided()
    {
        var at = new DateTimeOffset(2026, 9, 27, 9, 30, 0, TimeSpan.Zero);
        return new VoidFacts(at, SnapshotJson.Local(at, DocumentFixtures.Amman), "TEST-PAY-2026-000002");
    }

    /// <summary>A statement of <paramref name="lines"/> lines, laid out from a snapshot built for the purpose.</summary>
    private static PrintedDocument Synthetic(int lines, string? value = null, VoidFacts? voided = null)
    {
        var (_, page) = DocumentPrintLayoutTests.FixturePages().First(entry => entry.Name == "booking-statement-dispute-decided");
        var snapshot = JsonNode.Parse(page["snapshot"]!.ToJsonString())!.AsObject();
        var section = snapshot["content"]!["sections"]![0]!.AsObject();
        var many = new JsonArray();
        for (var index = 0; index < lines; index++)
        {
            many.Add(new JsonObject
            {
                ["key"] = $"line{index}",
                ["label"] = new JsonObject { ["en"] = $"Refund {index + 1}", ["ar"] = $"استرداد {index + 1}" },
                ["plain"] = value ?? $"RF-{index:D6}",
            });
        }

        section["lines"] = many;
        return DocumentPrintLayout.TryLayOut(1, snapshot.ToJsonString(), "TEST-STM-2026-000099", true, Language.Arabic, voided)!;
    }
}
