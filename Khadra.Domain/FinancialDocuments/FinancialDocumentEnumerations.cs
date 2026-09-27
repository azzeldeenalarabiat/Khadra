using Khadra.Domain.Common;

namespace Khadra.Domain.FinancialDocuments;

/// <summary>
/// The three documents the platform issues about a customer's money (payments Phase 5, owner
/// 2026-09-27). None of them is a tax invoice, and none may be called one until the legal and tax
/// requirements for that term are implemented.
/// </summary>
public sealed class FinancialDocumentType : Enumeration
{
    /// <summary>A payment was captured: applied to the booking, or captured and being refunded whole.</summary>
    public static readonly FinancialDocumentType PaymentReceipt = new(1, "PaymentReceipt", "PAY");

    /// <summary>A refund was settled: the provider confirmed the money is back with the customer.</summary>
    public static readonly FinancialDocumentType RefundReceipt = new(2, "RefundReceipt", "RFD");

    /// <summary>The customer's financial position on one booking, versioned each time money moves.</summary>
    public static readonly FinancialDocumentType BookingStatement = new(3, "BookingStatement", "STM");

    private FinancialDocumentType(int id, string name, string numberPrefix) : base(id, name)
    {
        NumberPrefix = numberPrefix;
    }

    /// <summary>The series prefix in a document number: <c>PAY</c>, <c>RFD</c>, <c>STM</c>.</summary>
    public string NumberPrefix { get; }

    /// <summary>A receipt: one per payment or refund, gaining a version only through a correction.</summary>
    public bool IsReceipt => this == PaymentReceipt || this == RefundReceipt;
}

/// <summary>
/// Why a document was issued: the money event it records (owner, 2026-09-27 — the closed list of
/// checkpoints), or the correction of a voided document.
/// </summary>
public sealed class FinancialDocumentCause : Enumeration
{
    /// <summary>A payment was captured, applied or orphaned.</summary>
    public static readonly FinancialDocumentCause PaymentCaptured = new(1, "PaymentCaptured");

    /// <summary>A refund was settled.</summary>
    public static readonly FinancialDocumentCause RefundSettled = new(2, "RefundSettled");

    /// <summary>A dispute on the booking was resolved.</summary>
    public static readonly FinancialDocumentCause DisputeResolved = new(3, "DisputeResolved");

    /// <summary>The booking ended: cancelled, no-show, completed, or closed by a dispute.</summary>
    public static readonly FinancialDocumentCause BookingEnded = new(4, "BookingEnded");

    /// <summary>The office recorded cash at a handover: a financial event (owner, 2026-09-27).</summary>
    public static readonly FinancialDocumentCause CashRecorded = new(5, "CashRecorded");

    /// <summary>An administrator voided the document this one replaces.</summary>
    public static readonly FinancialDocumentCause Correction = new(6, "Correction");

    private FinancialDocumentCause(int id, string name) : base(id, name)
    {
    }
}

/// <summary>Why a document that is owed has not been issued yet. Shown to administrators, never to customers.</summary>
public sealed class IssuanceHoldReason : Enumeration
{
    /// <summary>The booking's records contradict one another: no statement is frozen from them.</summary>
    public static readonly IssuanceHoldReason RecordsNeedReview = new(1, "RecordsNeedReview");

    /// <summary>
    /// Khadra's legal identity is not configured: no permanent document is issued with placeholder or
    /// incomplete issuer information (owner, 2026-09-27).
    /// </summary>
    public static readonly IssuanceHoldReason IssuerNotConfigured = new(2, "IssuerNotConfigured");

    /// <summary>Composing the document failed: a defect, logged at Error.</summary>
    public static readonly IssuanceHoldReason SnapshotFailed = new(3, "SnapshotFailed");

    private IssuanceHoldReason(int id, string name) : base(id, name)
    {
    }
}
