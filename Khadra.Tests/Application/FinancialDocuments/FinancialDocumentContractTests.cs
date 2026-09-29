using System.Text.Json;
using Khadra.Application.Common.Dtos;
using Khadra.Application.FinancialDocuments.Queries;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Application.FinancialDocuments.Rendering;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;

namespace Khadra.Tests.Application.FinancialDocuments;

/// <summary>
/// The wire shape of the documents endpoints (payments Phase 5): new endpoints only, read by the website, the
/// app from 1.3.0 and the console. Once an installed app reads these names they are a contract, so the names
/// are pinned here the day they are born.
/// </summary>
public sealed class FinancialDocumentContractTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_page_offers_the_pdfs_drawn_in_the_languages_a_pdf_is_drawn_in_and_counts_only_those()
    {
        static FinancialDocumentRenditionRecord Pdf(Language language, int template = 1, RenditionKind? kind = null) =>
            new(language, RenditionFormat.Pdf, kind ?? RenditionKind.AsIssued, template, "QuestPDF 2026.9.1", new string('a', 64), 1, DateTimeOffset.UnixEpoch, new string('b', 64));

        // In the order a page offers them, whatever order they were drawn in, and one entry per language.
        var both = FinancialDocumentPdfDto.For(false, [Pdf(Language.Arabic), Pdf(Language.English), Pdf(Language.English, template: 2)]);
        Assert.Equal(["en", "ar"], both.Languages);
        Assert.False(both.Preparing);

        var one = FinancialDocumentPdfDto.For(false, [Pdf(Language.Arabic)]);
        Assert.Equal(["ar"], one.Languages);
        Assert.True(one.Preparing);

        Assert.True(FinancialDocumentPdfDto.For(false, []).Preparing);

        // A voided document offers its VOIDED copies and nothing else (owner, 2026-09-29): its PDFs as issued stay the
        // administrator's, and until the copies are drawn the page says they are being prepared.
        var voidedUndrawn = FinancialDocumentPdfDto.For(true, [Pdf(Language.English), Pdf(Language.Arabic)]);
        Assert.Empty(voidedUndrawn.Languages);
        Assert.True(voidedUndrawn.Preparing);
        var voidedHalf = FinancialDocumentPdfDto.For(true, [Pdf(Language.English), Pdf(Language.Arabic), Pdf(Language.Arabic, kind: RenditionKind.Voided)]);
        Assert.Equal(["ar"], voidedHalf.Languages);
        Assert.True(voidedHalf.Preparing);
        var voided = FinancialDocumentPdfDto.For(
            true,
            [Pdf(Language.English), Pdf(Language.Arabic), Pdf(Language.Arabic, kind: RenditionKind.Voided), Pdf(Language.English, kind: RenditionKind.Voided)]);
        Assert.Equal(["en", "ar"], voided.Languages);
        Assert.False(voided.Preparing);

        // And a current document never offers a voided copy, whatever is stored.
        var current = FinancialDocumentPdfDto.For(false, [Pdf(Language.English, kind: RenditionKind.Voided), Pdf(Language.Arabic, kind: RenditionKind.Voided)]);
        Assert.Empty(current.Languages);
        Assert.True(current.Preparing);

        // The list a page counts is the renderer's own, never the platform's whole list of languages.
        Assert.Equal(new[] { Language.English, Language.Arabic }, DocumentPrintLayout.Languages);
    }

    [Fact]
    public void A_rendition_names_its_kind_for_the_administrator()
    {
        var rendition = new FinancialDocumentRenditionDto(
            "ar", "Pdf", "Voided", 1, "QuestPDF 2026.9.1", new string('a', 64), 2048, DateTimeOffset.UnixEpoch, new string('b', 64));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(rendition, Wire));

        // Payments Phase 6 follow-up, additive: which PDF it is — the document as issued, or its voided copy.
        Assert.Equal(
            ["language", "format", "kind", "templateVersion", "rendererVersion", "contentSha256", "sizeBytes", "renderedAt", "snapshotSha256"],
            json.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("Voided", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal(["AsIssued", "Voided"], Enumeration.GetAll<RenditionKind>().OrderBy(kind => kind.Id).Select(kind => kind.Name));
    }

    [Fact]
    public void A_list_row_names_its_fields_for_good()
    {
        var row = new FinancialDocumentListItem(
            Guid.NewGuid(), "PaymentReceipt", "PAY-2026-000001", 1, "Current", Guid.NewGuid(), "KH-ABCD1234",
            new BilingualDto("Payment receipt", "إيصال دفع"),
            new FinancialDocumentHeadlineDto(new BilingualDto("Amount paid", "المبلغ المدفوع"), new MoneyDto(102.75m, "JOD")),
            "PaymentCaptured", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(row, Wire));

        Assert.Equal(
            ["documentId", "type", "number", "version", "status", "bookingId", "bookingReference", "title", "headline", "cause", "occurredAt", "issuedAt"],
            json.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(["label", "amount"], json.RootElement.GetProperty("headline").EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(["en", "ar"], json.RootElement.GetProperty("title").EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public void A_bookings_documents_carry_what_is_being_prepared_beside_them()
    {
        var dto = new BookingFinancialDocumentsDto(Guid.NewGuid(), [], [new PendingFinancialDocumentDto("PaymentReceipt", Guid.NewGuid(), DateTimeOffset.UnixEpoch)]);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, Wire));

        Assert.Equal(["bookingId", "documents", "beingPrepared"], json.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(
            ["type", "subjectId", "occurredAt"],
            json.RootElement.GetProperty("beingPrepared")[0].EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public void A_documents_page_carries_the_stored_snapshot_as_json_not_as_a_string()
    {
        using var snapshot = JsonDocument.Parse("""{"schemaVersion":1,"content":{"title":{"en":"x","ar":"y"}}}""");
        var page = new FinancialDocumentDto(
            Guid.NewGuid(), "PaymentReceipt", "PAY-2026-000001", 1, "Current", Guid.NewGuid(), "KH-ABCD1234",
            new BilingualDto("Payment receipt", "إيصال دفع"),
            new FinancialDocumentHeadlineDto(new BilingualDto("Amount paid", "المبلغ المدفوع"), new MoneyDto(1m, "JOD")),
            "PaymentCaptured", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, snapshot.RootElement.Clone(),
            new FinancialDocumentLinksDto([], null, null, null, null, []),
            null,
            new FinancialDocumentPdfDto(["en"], true));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(page, Wire));

        Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("snapshot").ValueKind);
        // Payments Phase 6, additive: the languages whose PDF is drawn, and whether one is still being drawn.
        Assert.Equal(["languages", "preparing"], json.RootElement.GetProperty("pdf").EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("en", json.RootElement.GetProperty("pdf").GetProperty("languages")[0].GetString());
        Assert.True(json.RootElement.GetProperty("pdf").GetProperty("preparing").GetBoolean());
        Assert.Equal(1, json.RootElement.GetProperty("snapshot").GetProperty("schemaVersion").GetInt32());
        // A customer's page says a document was voided and what replaced it — never why, and never the test marker.
        Assert.False(json.RootElement.TryGetProperty("isTest", out _));
        Assert.False(json.RootElement.TryGetProperty("provider", out _));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("voided").ValueKind);
    }
}
