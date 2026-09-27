using CSharpFunctionalExtensions;
using Khadra.Domain.Common;

namespace Khadra.Domain.FinancialDocuments;

/// <summary>
/// An administrator's record that an issued document was wrong (payments Phase 5). The voided document
/// is never edited or deleted: it stays readable, marked void, and its correction — issued in the SAME
/// transaction as this row — replaces it under a new number.
/// </summary>
/// <remarks>
/// Its id IS the voided document's id, so a document can be voided at most once: two administrators
/// voiding the same document at once collide on the key, and the second is told so. Append-only, like
/// the document itself.
/// </remarks>
public sealed class FinancialDocumentVoid : AggregateRoot, IAppendOnly
{
    public const int MaxReasonLength = 500;

    /// <summary>The voided document.</summary>
    public Id DocumentId => Id;

    public DateTimeOffset VoidedAt { get; private set; }
    public Id VoidedByAdminId { get; private set; }

    /// <summary>Why, in the administrator's words. Administrators see it; customers never do.</summary>
    public string Reason { get; private set; } = null!;

    private FinancialDocumentVoid()
    {
    }

    private FinancialDocumentVoid(Id documentId) : base(documentId)
    {
    }

    public static Result<FinancialDocumentVoid, Error> Record(Id documentId, Id adminUserId, string? reason, DateTimeOffset now)
    {
        if (documentId.IsEmpty || adminUserId.IsEmpty)
            throw new DomainException("A void names the document and the administrator.");

        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return FinancialDocumentErrors.VoidReasonRequired;
        if (trimmed.Length > MaxReasonLength)
            return FinancialDocumentErrors.VoidReasonTooLong;

        return new FinancialDocumentVoid(documentId)
        {
            VoidedAt = now,
            VoidedByAdminId = adminUserId,
            Reason = trimmed
        };
    }
}
