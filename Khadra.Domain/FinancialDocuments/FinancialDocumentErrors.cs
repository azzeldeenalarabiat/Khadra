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
}
