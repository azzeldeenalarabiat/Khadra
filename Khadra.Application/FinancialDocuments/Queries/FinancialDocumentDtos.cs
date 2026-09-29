using System.Text.Json;
using Khadra.Application.Common.Dtos;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Application.FinancialDocuments.Rendering;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;

namespace Khadra.Application.FinancialDocuments.Queries;

/// <summary>A text in English and Arabic, as a document stored it.</summary>
public sealed record BilingualDto(string En, string Ar);

/// <summary>The one figure a list shows for a document, with its stored label.</summary>
public sealed record FinancialDocumentHeadlineDto(BilingualDto Label, MoneyDto Amount);

/// <summary>
/// One document in a list (payments Phase 5): the customer's Invoices &amp; Receipts area, a booking's
/// documents. Its title and headline label are read from the stored document, never worded by a client.
/// </summary>
/// <param name="Type"><c>PaymentReceipt</c>, <c>RefundReceipt</c> or <c>BookingStatement</c>.</param>
/// <param name="Status"><c>Current</c>, <c>Superseded</c> or <c>Voided</c>, worked out when read.</param>
/// <param name="Cause">What issued it: <c>PaymentCaptured</c>, <c>RefundSettled</c>, <c>DisputeResolved</c>, <c>BookingEnded</c>, <c>CashRecorded</c>, <c>Correction</c> or, for a statement, <c>ReceiptCorrected</c>.</param>
/// <param name="OccurredAt">When the money event it records happened.</param>
/// <param name="IssuedAt">When it was issued. A document issued late for older money shows both.</param>
public sealed record FinancialDocumentListItem(
    Guid DocumentId,
    string Type,
    string Number,
    int Version,
    string Status,
    Guid BookingId,
    string BookingReference,
    BilingualDto Title,
    FinancialDocumentHeadlineDto Headline,
    string Cause,
    DateTimeOffset OccurredAt,
    DateTimeOffset IssuedAt)
{
    internal static FinancialDocumentListItem From(FinancialDocumentRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var (title, label) = SnapshotReading.TitleAndHeadline(record);
        return new FinancialDocumentListItem(
            record.Id.Value,
            record.Type.Name,
            record.Number,
            record.Version,
            record.Status.Name,
            record.BookingId.Value,
            record.BookingReference,
            title,
            new FinancialDocumentHeadlineDto(label, MoneyDto.From(record.HeadlineAmount)),
            record.Cause.Name,
            record.OccurredAt,
            record.IssuedAt);
    }
}

/// <summary>Another document this one points at.</summary>
public sealed record FinancialDocumentLinkDto(Guid DocumentId, string Type, string Number, int Version, string Status)
{
    internal static FinancialDocumentLinkDto From(FinancialDocumentRecord record) =>
        new(record.Id.Value, record.Type.Name, record.Number, record.Version, record.Status.Name);
}

/// <summary>A document's place among the others: its versions, and the receipts it belongs with.</summary>
/// <param name="Versions">Every version of its family, oldest first, this one included.</param>
/// <param name="ReplacedBy">For a voided document, the correction issued in its place.</param>
/// <param name="PaymentReceipt">For a refund receipt, the payment receipt it belongs to — which it never altered.</param>
/// <param name="RefundReceipts">For a payment receipt, the current receipts of the refunds made from it.</param>
public sealed record FinancialDocumentLinksDto(
    IReadOnlyList<FinancialDocumentLinkDto> Versions,
    FinancialDocumentLinkDto? PreviousVersion,
    FinancialDocumentLinkDto? NextVersion,
    FinancialDocumentLinkDto? ReplacedBy,
    FinancialDocumentLinkDto? PaymentReceipt,
    IReadOnlyList<FinancialDocumentLinkDto> RefundReceipts);

/// <summary>That a document was voided, and what replaced it. The reason is the administrator's alone.</summary>
public sealed record FinancialDocumentVoidNoticeDto(DateTimeOffset VoidedAt, FinancialDocumentLinkDto? ReplacedBy);

/// <summary>
/// One document as the customer reads it (payments Phase 5): the stored snapshot EXACTLY as issued —
/// header, issuer, parties, facts and the laid-out content in English and Arabic — plus its standing and
/// its links, which are worked out when it is read.
/// </summary>
/// <param name="Snapshot">The document itself. Every figure and word a screen shows comes from here.</param>
public sealed record FinancialDocumentDto(
    Guid DocumentId,
    string Type,
    string Number,
    int Version,
    string Status,
    Guid BookingId,
    string BookingReference,
    BilingualDto Title,
    FinancialDocumentHeadlineDto Headline,
    string Cause,
    DateTimeOffset OccurredAt,
    DateTimeOffset IssuedAt,
    int SnapshotSchemaVersion,
    JsonElement Snapshot,
    FinancialDocumentLinksDto Links,
    FinancialDocumentVoidNoticeDto? Voided,
    FinancialDocumentPdfDto Pdf);

/// <summary>
/// The PDFs the customer can download a document as (payments Phase 6).
/// </summary>
/// <param name="Languages">
/// The languages whose PDF has been drawn, <c>en</c> before <c>ar</c> — one download each. Empty for a voided
/// document: its page stays, marked void, but no new copy of a file that does not say so is handed out.
/// </param>
/// <param name="Preparing">
/// True while a PDF the document will have is not drawn yet — the settlement pass draws them within minutes of
/// issue. Never true for a voided document.
/// </param>
public sealed record FinancialDocumentPdfDto(IReadOnlyList<string> Languages, bool Preparing)
{
    internal static FinancialDocumentPdfDto For(bool voided, IEnumerable<FinancialDocumentRenditionRecord> renditions)
    {
        if (voided)
            return new FinancialDocumentPdfDto([], false);

        // The languages a PDF is drawn in, in their own order — never the platform's whole list of languages, which
        // another feature may grow: a third one there must not leave every document "being prepared" for good.
        var printed = DocumentPrintLayout.Languages;
        var stored = renditions
            .Where(rendition => rendition.Format == RenditionFormat.Pdf)
            .Select(rendition => rendition.Language)
            .ToHashSet();
        var drawn = printed.Where(stored.Contains).ToList();
        return new FinancialDocumentPdfDto([.. drawn.Select(language => language.Name)], drawn.Count < printed.Count);
    }
}

/// <summary>A stored rendering as the administrator sees it: what drew it and the proof of its bytes.</summary>
/// <param name="ContentSha256">SHA-256 of the stored PDF: the proof of which bytes were handed out.</param>
/// <param name="SnapshotSha256">The document's own hash when it was drawn: the proof it pictures the record as issued.</param>
public sealed record FinancialDocumentRenditionDto(
    string Language,
    string Format,
    int TemplateVersion,
    string RendererVersion,
    string ContentSha256,
    long SizeBytes,
    DateTimeOffset RenderedAt,
    string SnapshotSha256)
{
    internal static FinancialDocumentRenditionDto From(FinancialDocumentRenditionRecord record) =>
        new(
            record.Language.Name,
            record.Format.Name,
            record.TemplateVersion,
            record.RendererVersion,
            record.ContentSha256,
            record.SizeBytes,
            record.RenderedAt,
            record.SnapshotSha256);
}

/// <summary>A document owed and not issued yet: "your receipt is being prepared".</summary>
/// <param name="SubjectId">The payment, refund or booking it will be about.</param>
public sealed record PendingFinancialDocumentDto(string Type, Guid SubjectId, DateTimeOffset OccurredAt)
{
    internal static PendingFinancialDocumentDto From(PendingFinancialDocumentRecord record) =>
        new(record.Type.Name, record.SubjectId.Value, record.OccurredAt);
}

/// <summary>
/// A booking's documents for its Booking Details (payments Phase 5), and what is still being prepared, so no
/// client guesses. The rental office gets both lists empty: it sees no customer document in Phase 5.
/// </summary>
public sealed record BookingFinancialDocumentsDto(
    Guid BookingId,
    IReadOnlyList<FinancialDocumentListItem> Documents,
    IReadOnlyList<PendingFinancialDocumentDto> BeingPrepared);

// ── The administrator ─────────────────────────────────────────────────────────────────────────────

/// <summary>A document in the administrator's lists: the customer's row, plus the test marker and the parties' ids.</summary>
public sealed record AdminFinancialDocumentListItem(
    Guid DocumentId,
    string Type,
    string Number,
    int Version,
    string Status,
    Guid BookingId,
    string BookingReference,
    Guid CustomerId,
    Guid DealerId,
    BilingualDto Title,
    FinancialDocumentHeadlineDto Headline,
    string Cause,
    DateTimeOffset OccurredAt,
    DateTimeOffset IssuedAt,
    bool IsTest)
{
    internal static AdminFinancialDocumentListItem From(FinancialDocumentRecord record)
    {
        var row = FinancialDocumentListItem.From(record);
        return new AdminFinancialDocumentListItem(
            row.DocumentId,
            row.Type,
            row.Number,
            row.Version,
            row.Status,
            row.BookingId,
            row.BookingReference,
            record.CustomerId.Value,
            record.DealerId.Value,
            row.Title,
            row.Headline,
            row.Cause,
            row.OccurredAt,
            row.IssuedAt,
            PaymentProviders.IsSandbox(record.Provider));
    }
}

/// <summary>An administrator's void, reason and all.</summary>
public sealed record FinancialDocumentVoidDto(
    DateTimeOffset VoidedAt,
    Guid VoidedByAdminId,
    string? VoidedByName,
    string Reason,
    FinancialDocumentLinkDto? ReplacedBy);

/// <summary>
/// One document as the administrator reads it: the customer's page, plus the provider it froze and the test
/// marker read from it, the proof of what was issued (<c>contentSha256</c>), what a statement covered, the
/// void with its reason, and every PDF drawn of it (payments Phase 6).
/// </summary>
/// <remarks>
/// For a VOIDED document <c>Document.Pdf.Languages</c> is empty — the customer is no longer handed its PDF —
/// while <see cref="Renditions"/> is not: the administrator can still download the record as issued. A console
/// keys its downloads off <see cref="Renditions"/>.
/// </remarks>
public sealed record AdminFinancialDocumentDto(
    FinancialDocumentDto Document,
    Guid CustomerId,
    Guid DealerId,
    string Provider,
    bool IsTest,
    string ContentSha256,
    DateTimeOffset? CoversThrough,
    string? CheckpointFingerprint,
    FinancialDocumentVoidDto? Void,
    IReadOnlyList<FinancialDocumentRenditionDto> Renditions);

/// <summary>A document family on hold: owed, not issued, and why.</summary>
/// <param name="Reason"><c>RecordsNeedReview</c>, <c>IssuerNotConfigured</c> or <c>SnapshotFailed</c>.</param>
public sealed record FinancialDocumentHoldDto(
    Guid HoldId,
    string DocumentType,
    Guid SubjectId,
    Guid BookingId,
    string? BookingReference,
    string Reason,
    int Attempts,
    DateTimeOffset FirstFailedAt,
    DateTimeOffset LastFailedAt,
    DateTimeOffset NextAttemptAt,
    string? LastError)
{
    internal static FinancialDocumentHoldDto From(FinancialDocumentHoldRecord record) =>
        new(
            record.Id.Value,
            record.DocumentType.Name,
            record.SubjectId.Value,
            record.BookingId.Value,
            record.BookingReference,
            record.Reason.Name,
            record.Attempts,
            record.FirstFailedAt,
            record.LastFailedAt,
            record.NextAttemptAt,
            record.LastError);
}

/// <summary>A booking's documents, what is being prepared, and what is on hold — for the Money section.</summary>
public sealed record AdminBookingFinancialDocumentsDto(
    Guid BookingId,
    IReadOnlyList<AdminFinancialDocumentListItem> Documents,
    IReadOnlyList<PendingFinancialDocumentDto> BeingPrepared,
    IReadOnlyList<FinancialDocumentHoldDto> Holds);

/// <summary>The words the documents screens filter on, from the domain's own enumerations.</summary>
public sealed record FinancialDocumentVocabularyDto(
    IReadOnlyList<string> Types,
    IReadOnlyList<string> Statuses,
    IReadOnlyList<string> Causes,
    IReadOnlyList<string> HoldReasons);

/// <summary>What a void did: the voided document, and the correction that replaced it.</summary>
public sealed record VoidedFinancialDocumentDto(
    Guid VoidedDocumentId,
    string VoidedNumber,
    Guid ReplacementDocumentId,
    string ReplacementNumber);

/// <summary>
/// Reads what a list row shows from the stored document — its title and headline label as issued. Readers
/// support every snapshot schema version ever issued; version 1 is the only one so far.
/// </summary>
internal static class SnapshotReading
{
    public static (BilingualDto Title, BilingualDto HeadlineLabel) TitleAndHeadline(FinancialDocumentRecord record)
    {
        using var document = JsonDocument.Parse(record.Snapshot);
        var content = document.RootElement.GetProperty("content");
        return (Bilingual(content.GetProperty("title")), Bilingual(content.GetProperty("headline").GetProperty("label")));
    }

    public static JsonElement Parse(string snapshot)
    {
        using var document = JsonDocument.Parse(snapshot);
        return document.RootElement.Clone();
    }

    private static BilingualDto Bilingual(JsonElement text) =>
        new(text.GetProperty("en").GetString()!, text.GetProperty("ar").GetString()!);
}

/// <summary>Builds a document's page from its record, its family and its related receipts.</summary>
internal static class FinancialDocumentPages
{
    public static FinancialDocumentDto Build(
        FinancialDocumentRecord record,
        IReadOnlyList<FinancialDocumentRecord> family,
        FinancialDocumentRecord? paymentReceipt,
        IReadOnlyList<FinancialDocumentRecord> refundReceipts,
        FinancialDocumentVoidRecord? voided,
        IReadOnlyList<FinancialDocumentRenditionRecord> renditions)
    {
        var row = FinancialDocumentListItem.From(record);
        var versions = family.OrderBy(member => member.Version).ToList();
        var previous = versions.LastOrDefault(member => member.Version < record.Version);
        var next = versions.FirstOrDefault(member => member.Version > record.Version);
        // A void always carries its correction: the next version of the family, issued in the same transaction.
        var replacedBy = voided is not null && next is { Cause: var cause } && cause == FinancialDocumentCause.Correction ? next : null;

        return new FinancialDocumentDto(
            row.DocumentId,
            row.Type,
            row.Number,
            row.Version,
            row.Status,
            row.BookingId,
            row.BookingReference,
            row.Title,
            row.Headline,
            row.Cause,
            row.OccurredAt,
            row.IssuedAt,
            record.SnapshotSchemaVersion,
            SnapshotReading.Parse(record.Snapshot),
            new FinancialDocumentLinksDto(
                [.. versions.Select(FinancialDocumentLinkDto.From)],
                previous is null ? null : FinancialDocumentLinkDto.From(previous),
                next is null ? null : FinancialDocumentLinkDto.From(next),
                replacedBy is null ? null : FinancialDocumentLinkDto.From(replacedBy),
                paymentReceipt is null ? null : FinancialDocumentLinkDto.From(paymentReceipt),
                [.. refundReceipts.Select(FinancialDocumentLinkDto.From)]),
            voided is null
                ? null
                : new FinancialDocumentVoidNoticeDto(voided.VoidedAt, replacedBy is null ? null : FinancialDocumentLinkDto.From(replacedBy)),
            FinancialDocumentPdfDto.For(voided is not null, renditions));
    }
}
