using Khadra.Domain.Common;

namespace Khadra.Domain.FinancialDocuments;

public static class FinancialDocumentErrors
{
    public static readonly Error NotFound =
        Error.NotFound("financial_documents.not_found", "That document was not found.");

    public static readonly Error VoidReasonRequired =
        Error.Validation("financial_documents.void_reason_required", "Say why the document is being voided.");

    public static readonly Error VoidReasonTooLong = Error.Validation(
        "financial_documents.void_reason_too_long",
        $"A void reason is at most {FinancialDocumentVoid.MaxReasonLength} characters.");

    /// <summary>Only the current version of a document can be voided; earlier versions are history.</summary>
    public static readonly Error NotCurrent =
        Error.Conflict("financial_documents.not_current", "Only the current version of a document can be voided.");

    public static readonly Error AlreadyVoided =
        Error.Conflict("financial_documents.already_voided", "That document has already been voided.");

    // A void always carries its correction, in one transaction (owner, 2026-09-27). When the correction
    // cannot be issued, nothing is voided: the document stays current, and the reason says why.

    public static readonly Error CorrectionNeedsReview = Error.Conflict(
        "financial_documents.correction_records_need_review",
        "The booking's records contradict one another, so no corrected document can be issued. Nothing was voided.");

    public static readonly Error CorrectionIssuerNotConfigured = Error.Conflict(
        "financial_documents.correction_issuer_not_configured",
        "Khadra's legal identity is not configured for documents, so no corrected document can be issued. Nothing was voided.");

    public static readonly Error CorrectionFailed = Error.Failure(
        "financial_documents.correction_failed",
        "The corrected document could not be composed. Nothing was voided; the failure has been logged.");

    public static readonly Error UnknownType =
        Error.Validation("financial_documents.unknown_type", "Unknown document type.");

    public static readonly Error UnknownStatus =
        Error.Validation("financial_documents.unknown_status", "Unknown document status.");

    /// <summary>The document exists and is the caller's, but its PDF in that language is not drawn yet (payments Phase 6).</summary>
    public static readonly Error PdfNotReady =
        Error.Conflict("financial_documents.pdf_not_ready", "The PDF of this document is being prepared.");

    /// <summary>Only a voided document has a voided copy (payments Phase 6 follow-up): the administrator asked for one of a document that is not voided.</summary>
    public static readonly Error NotVoided = Error.Conflict(
        "financial_documents.not_voided",
        "This document is not voided, so it has no voided copy.");

    /// <summary>Only receipts are emailed to the customer (payments Phase 7; owner, 2026-09-29): a statement is not.</summary>
    public static readonly Error NotEmailed = Error.Conflict(
        "financial_documents.not_emailed",
        "Only receipts are emailed to the customer.");

    /// <summary>A voided receipt is not emailed again: its correction is the receipt that stands.</summary>
    public static readonly Error VoidedNotEmailed = Error.Conflict(
        "financial_documents.voided_not_emailed",
        "This receipt was voided. Its correction is the one to email.");

    /// <summary>An email of this document is already on its way: one at a time.</summary>
    public static readonly Error EmailAlreadyQueued = Error.Conflict(
        "financial_documents.email_already_queued",
        "An email of this document is already queued.");

    /// <summary>
    /// This host sends no financial-document email at all (owner, 2026-09-29): Production on Brevo, whose single-send
    /// idempotency has not been verified (pre-launch item 202).
    /// </summary>
    public static readonly Error EmailDeliveryDisabled = Error.Conflict(
        "financial_documents.email_delivery_disabled",
        "Financial-document emails are switched off on this server: its mail provider, Brevo, has not had its single-send idempotency verified.");
}
