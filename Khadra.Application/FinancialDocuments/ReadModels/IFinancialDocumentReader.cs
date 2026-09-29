using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;

namespace Khadra.Application.FinancialDocuments.ReadModels;

/// <summary>One issued document as stored, with its standing worked out beside it (never stored).</summary>
/// <param name="Snapshot">The canonical JSON, exactly as issued.</param>
public sealed record FinancialDocumentRecord(
    Id Id,
    FinancialDocumentType Type,
    string Number,
    int Version,
    FinancialDocumentStatus Status,
    Id SubjectId,
    Id BookingId,
    string BookingReference,
    Id CustomerId,
    Id DealerId,
    Id? PaymentId,
    Id? RefundId,
    Id? PreviousVersionId,
    Id? RelatedDocumentId,
    FinancialDocumentCause Cause,
    DateTimeOffset OccurredAt,
    DateTimeOffset IssuedAt,
    DateTimeOffset? CoversThrough,
    string? CheckpointFingerprint,
    Money HeadlineAmount,
    string Provider,
    int SnapshotSchemaVersion,
    string Snapshot,
    string ContentSha256);

/// <summary>An administrator's void of a document: when, by whom, and why.</summary>
public sealed record FinancialDocumentVoidRecord(Id DocumentId, DateTimeOffset VoidedAt, Id VoidedByAdminId, string? VoidedByName, string Reason);

/// <summary>A document owed and not issued yet — "being prepared", in the customer's words.</summary>
/// <param name="OccurredAt">When the money event it will record happened.</param>
public sealed record PendingFinancialDocumentRecord(FinancialDocumentType Type, Id SubjectId, DateTimeOffset OccurredAt);

/// <summary>A stored rendering of a document (payments Phase 6): its language, what drew it, and the proof of its bytes.</summary>
public sealed record FinancialDocumentRenditionRecord(
    Language Language,
    RenditionFormat Format,
    RenditionKind Kind,
    int TemplateVersion,
    string RendererVersion,
    string ContentSha256,
    long SizeBytes,
    DateTimeOffset RenderedAt,
    string SnapshotSha256);

/// <summary>One email of a document (payments Phase 7), as the administrator reads it: where it stands, and every attempt.</summary>
/// <param name="RequestedByName">The administrator who asked for it again, when their account still resolves; null for the email owed at issue.</param>
/// <param name="RecipientAddress">The verified address the last send used, or null before any.</param>
public sealed record FinancialDocumentDeliveryRecord(
    Id Id,
    FinancialDocumentDeliveryState State,
    FinancialDocumentDeliveryWait? WaitingReason,
    DateTimeOffset? WaitingSince,
    Id? RequestedByAdminId,
    string? RequestedByName,
    DateTimeOffset QueuedAt,
    DateTimeOffset? CompletedAt,
    string? RecipientAddress,
    string? Languages,
    int SendAttempts,
    string? LastError,
    IReadOnlyList<FinancialDocumentDeliveryAttemptRecord> Attempts);

/// <summary>One attempt at an email, with the hashes of the PDFs it carried: the proof of which bytes went.</summary>
public sealed record FinancialDocumentDeliveryAttemptRecord(
    int Number,
    FinancialDocumentDeliveryOutcome Outcome,
    DateTimeOffset AttemptedAt,
    string? Error,
    string? Provider,
    string? ProviderMessageId,
    string? EnglishPdfSha256,
    string? ArabicPdfSha256);

/// <summary>
/// What the work queue says about receipts whose email has not gone (payments Phase 7): those whose latest email
/// FAILED, and those queued longer than they should be — usually waiting for a PDF.
/// </summary>
/// <param name="Numbers">Up to three document numbers, the ones a human reads first.</param>
public sealed record FinancialDocumentEmailsSummary(
    int Count,
    IReadOnlyList<Id> DocumentIds,
    IReadOnlyList<string> Numbers,
    DateTimeOffset? OldestQueuedAt)
{
    public static readonly FinancialDocumentEmailsSummary None = new(0, [], [], null);
}

/// <summary>A family on hold, for the administrator.</summary>
public sealed record FinancialDocumentHoldRecord(
    Id Id,
    FinancialDocumentType DocumentType,
    Id SubjectId,
    Id BookingId,
    string? BookingReference,
    IssuanceHoldReason Reason,
    int Attempts,
    DateTimeOffset FirstFailedAt,
    DateTimeOffset LastFailedAt,
    DateTimeOffset NextAttemptAt,
    string? LastError);

/// <summary>What the work queue says about documents on hold.</summary>
/// <param name="BookingReferences">Up to three references, the ones a human reads first.</param>
public sealed record FinancialDocumentHoldsSummary(
    int Count,
    IReadOnlyList<Id> HoldIds,
    IReadOnlyList<string> BookingReferences,
    DateTimeOffset? OldestFailedAt)
{
    public static readonly FinancialDocumentHoldsSummary None = new(0, [], [], null);
}

/// <summary>Which documents the administrator's list shows. Dates are instants, converted from Amman days by the handler.</summary>
/// <param name="Number">A document number, matched exactly.</param>
/// <param name="Reference">A booking reference, matched exactly.</param>
public sealed record AdminFinancialDocumentFilter(
    FinancialDocumentType? Type,
    FinancialDocumentStatus? Status,
    string? Number,
    string? Reference,
    DateTimeOffset? IssuedFrom,
    DateTimeOffset? IssuedBefore);

/// <summary>
/// The customer's and the administrator's readings of issued documents (payments Phase 5). Every list is
/// ordered (issued at DESC, id DESC): a non-total order lets a page boundary drop a document.
/// </summary>
public interface IFinancialDocumentReader
{
    Task<PagedResult<FinancialDocumentRecord>> ListForCustomerAsync(
        Id customerId,
        FinancialDocumentType? type,
        PageRequest page,
        CancellationToken cancellationToken = default);

    Task<PagedResult<FinancialDocumentRecord>> ListAsync(
        AdminFinancialDocumentFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default);

    /// <summary>Every version of every document on a booking, newest first.</summary>
    Task<IReadOnlyList<FinancialDocumentRecord>> ListForBookingAsync(Id bookingId, CancellationToken cancellationToken = default);

    /// <summary>Every document about one payment: its receipt family and its refunds' receipts.</summary>
    Task<IReadOnlyList<FinancialDocumentRecord>> ListForPaymentAsync(Id paymentId, CancellationToken cancellationToken = default);

    Task<FinancialDocumentRecord?> GetAsync(Id documentId, CancellationToken cancellationToken = default);

    /// <summary>Every version of a family, oldest first.</summary>
    Task<IReadOnlyList<FinancialDocumentRecord>> FamilyAsync(
        FinancialDocumentType type,
        Id subjectId,
        CancellationToken cancellationToken = default);

    Task<FinancialDocumentVoidRecord?> VoidOfAsync(Id documentId, CancellationToken cancellationToken = default);

    /// <summary>Every stored rendering of a document, oldest first (payments Phase 6).</summary>
    Task<IReadOnlyList<FinancialDocumentRenditionRecord>> RenditionsOfAsync(Id documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What a booking is owed and has not been issued yet: a captured payment with no receipt, a settled
    /// refund with none, a statement behind its checkpoints. On hold or not — a customer is told only that
    /// it is being prepared.
    /// </summary>
    Task<IReadOnlyList<PendingFinancialDocumentRecord>> PendingForBookingAsync(Id bookingId, CancellationToken cancellationToken = default);

    /// <summary>Unresolved holds, oldest failure first.</summary>
    Task<PagedResult<FinancialDocumentHoldRecord>> ListOpenHoldsAsync(PageRequest page, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinancialDocumentHoldRecord>> OpenHoldsForBookingAsync(Id bookingId, CancellationToken cancellationToken = default);

    Task<FinancialDocumentHoldsSummary> OpenHoldsSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>Every email of a document, newest first, each with its attempts (payments Phase 7).</summary>
    Task<IReadOnlyList<FinancialDocumentDeliveryRecord>> DeliveriesOfAsync(Id documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Documents whose LATEST email failed, or is still queued from before <paramref name="staleBefore"/> — a later
    /// email that went clears the one that failed, and a voided receipt is never listed: it is not emailed again, its
    /// correction is. Oldest first.
    /// </summary>
    Task<FinancialDocumentEmailsSummary> EmailsNotSentSummaryAsync(DateTimeOffset staleBefore, CancellationToken cancellationToken = default);
}
