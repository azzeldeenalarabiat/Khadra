using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Documents;

namespace Khadra.Tests.Domain.FinancialDocuments;

/// <summary>
/// A stored rendering of an issued document (payments Phase 6): it vouches for exactly the bytes it names and
/// the record they were drawn from, and every argument is the system's own — so a wrong one is a defect.
/// </summary>
public sealed class FinancialDocumentRenditionRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Hash = new('a', 64);

    [Fact]
    public void A_rendition_names_its_document_the_snapshot_it_was_drawn_from_and_its_bytes()
    {
        var document = Receipt();
        var key = FinancialDocumentRendition.NewStorageKey(document.Id, Language.Arabic, RenditionFormat.Pdf, 1);

        var rendition = FinancialDocumentRendition.Record(document, Language.Arabic, RenditionFormat.Pdf, 1, "QuestPDF 2026.9.1", key, Hash, 4096, Now);

        Assert.Equal(document.Id, rendition.DocumentId);
        Assert.Equal(Language.Arabic, rendition.Language);
        Assert.Equal(RenditionFormat.Pdf, rendition.Format);
        Assert.Equal(1, rendition.TemplateVersion);
        Assert.Equal(key, rendition.StorageKey);
        Assert.Equal(Hash, rendition.ContentSha256);
        Assert.Equal(4096, rendition.SizeBytes);
        // Which record the bytes represent: the document's own hash, taken when they were drawn.
        Assert.Equal(document.ContentSha256, rendition.SnapshotSha256);
        Assert.Equal(Now, rendition.RenderedAt);
        Assert.IsAssignableFrom<IAppendOnly>(rendition);
    }

    [Theory]
    [InlineData(0, "QuestPDF 2026.9.1", "ok", 4096, "hash")]
    [InlineData(1, "", "ok", 4096, "hash")]
    [InlineData(1, "long", "ok", 4096, "hash")]
    [InlineData(1, "QuestPDF 2026.9.1", "", 4096, "hash")]
    [InlineData(1, "QuestPDF 2026.9.1", "long", 4096, "hash")]
    [InlineData(1, "QuestPDF 2026.9.1", "ok", 0, "hash")]
    [InlineData(1, "QuestPDF 2026.9.1", "ok", 4096, "upper")]
    [InlineData(1, "QuestPDF 2026.9.1", "ok", 4096, "short")]
    public void A_defective_rendition_is_a_programming_error(int templateVersion, string renderer, string key, long size, string hash)
    {
        var document = Receipt();
        var storageKey = key switch
        {
            "ok" => FinancialDocumentRendition.NewStorageKey(document.Id, Language.English, RenditionFormat.Pdf, 1),
            "long" => new string('k', FinancialDocumentRendition.MaxStorageKeyLength + 1),
            _ => key,
        };
        var rendererVersion = renderer == "long" ? new string('r', FinancialDocumentRendition.MaxRendererVersionLength + 1) : renderer;
        var contentHash = hash switch
        {
            "upper" => new string('A', 64),
            "short" => new string('a', 63),
            _ => Hash,
        };

        Assert.Throws<DomainException>(() =>
            FinancialDocumentRendition.Record(document, Language.English, RenditionFormat.Pdf, templateVersion, rendererVersion, storageKey, contentHash, size, Now));
    }

    [Fact]
    public void Each_attempt_gets_a_fresh_private_key_every_store_accepts()
    {
        var documentId = Id.New();

        var first = FinancialDocumentRendition.NewStorageKey(documentId, Language.English, RenditionFormat.Pdf, 1);
        var second = FinancialDocumentRendition.NewStorageKey(documentId, Language.English, RenditionFormat.Pdf, 1);

        // A crash between storing and recording leaves an orphan the next attempt steps around, never a key it
        // can no longer write.
        Assert.NotEqual(first, second);
        Assert.StartsWith($"financial-documents/{documentId.Value:D}/v1-en-", first, StringComparison.Ordinal);
        Assert.EndsWith(".pdf", first, StringComparison.Ordinal);
        Assert.Equal(first, DocumentKeys.Validate(first));
        Assert.Equal(
            "financial-documents",
            FinancialDocumentRendition.NewStorageKey(documentId, Language.Arabic, RenditionFormat.Pdf, 12).Split('/')[0]);
        Assert.Contains("/v12-ar-", FinancialDocumentRendition.NewStorageKey(documentId, Language.Arabic, RenditionFormat.Pdf, 12), StringComparison.Ordinal);
    }

    [Fact]
    public void A_pdf_is_served_as_a_pdf()
    {
        Assert.Equal("application/pdf", RenditionFormat.Pdf.ContentType);
        Assert.Equal("pdf", RenditionFormat.Pdf.Extension);
        Assert.Equal(
            "application/pdf",
            Khadra.Application.Common.Ports.DocumentContentTypes.ForStorageKey(FinancialDocumentRendition.NewStorageKey(Id.New(), Language.English, RenditionFormat.Pdf, 1)));
    }

    private static FinancialDocument Receipt()
    {
        var paymentId = Id.New();
        var draft = new FinancialDocumentDraft(
            FinancialDocumentType.PaymentReceipt,
            paymentId,
            Version: 1,
            PreviousVersionId: null,
            RelatedDocumentId: null,
            BookingId: Id.New(),
            BookingReference: "KH-RENDER01",
            CustomerId: Id.New(),
            DealerId: Id.New(),
            PaymentId: paymentId,
            RefundId: null,
            FinancialDocumentCause.PaymentCaptured,
            OccurredAt: Now,
            CoversThrough: null,
            CheckpointFingerprint: null,
            HeadlineAmount: Money.Jod(18m),
            Provider: PaymentProviders.Sandbox,
            CalculatorVersion: 1,
            SnapshotSchemaVersion: 1,
            Snapshot: "{\"schemaVersion\":1}");
        return FinancialDocument.Issue(draft, "TEST-PAY-2026-000001", Now);
    }
}
