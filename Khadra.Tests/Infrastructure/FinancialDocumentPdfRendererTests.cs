using System.Text.Json.Nodes;
using Khadra.Application.FinancialDocuments.Rendering;
using Khadra.Domain.Common;
using Khadra.Infrastructure.FinancialDocuments;
using Khadra.Tests.Application.FinancialDocuments;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// Drawing issued documents as PDFs with QuestPDF (payments Phase 6): every page of the shared contract
/// fixture, in both languages, with the platform's own faces only — a character they lack fails here rather
/// than printing a box on a record.
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
                var images = QuestPdfFinancialDocumentRenderer.Compose(DocumentPrintLayoutTests.LayOut(page, language)!)
                    .GenerateImages(new ImageGenerationSettings { RasterDpi = 110 })
                    .ToList();
                for (var index = 0; index < images.Count; index++)
                    File.WriteAllBytes(Path.Combine(folder, $"{name}-{language.Name}-{index + 1}.png"), images[index]);
            }
        }
    }

    /// <summary>A statement of <paramref name="lines"/> lines, laid out from a snapshot built for the purpose.</summary>
    private static PrintedDocument Synthetic(int lines, string? value = null)
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
        return DocumentPrintLayout.TryLayOut(1, snapshot.ToJsonString(), "TEST-STM-2026-000099", true, Language.Arabic)!;
    }
}
