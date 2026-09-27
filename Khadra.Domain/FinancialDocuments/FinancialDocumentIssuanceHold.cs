using Khadra.Domain.Common;

namespace Khadra.Domain.FinancialDocuments;

/// <summary>
/// A document family that is owed but cannot be issued yet, and why (payments Phase 5). One row per
/// family — a payment's receipt, a refund's receipt, or a booking's statement — upserted on each failed
/// attempt rather than one row per attempt, and resolved when the document is issued. Administrators see
/// the unresolved ones on the work queue: a document is never silently missing.
/// </summary>
public sealed class FinancialDocumentIssuanceHold : AggregateRoot
{
    public const int MaxErrorLength = 300;

    public FinancialDocumentType DocumentType { get; private set; } = null!;
    public Id SubjectId { get; private set; }
    public Id BookingId { get; private set; }
    public IssuanceHoldReason Reason { get; private set; } = null!;
    public int Attempts { get; private set; }
    public DateTimeOffset FirstFailedAt { get; private set; }
    public DateTimeOffset LastFailedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>A short, technical description for the administrator. Never shown to a customer.</summary>
    public string? LastError { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public bool IsResolved => ResolvedAt is not null;

    private FinancialDocumentIssuanceHold()
    {
    }

    private FinancialDocumentIssuanceHold(Id id) : base(id)
    {
    }

    public static FinancialDocumentIssuanceHold Open(
        FinancialDocumentType documentType,
        Id subjectId,
        Id bookingId,
        IssuanceHoldReason reason,
        string? error,
        DateTimeOffset now,
        DateTimeOffset nextAttemptAt)
    {
        ArgumentNullException.ThrowIfNull(documentType);
        ArgumentNullException.ThrowIfNull(reason);
        if (subjectId.IsEmpty || bookingId.IsEmpty)
            throw new DomainException("A hold names the document family and its booking.");

        return new FinancialDocumentIssuanceHold(Id.New())
        {
            DocumentType = documentType,
            SubjectId = subjectId,
            BookingId = bookingId,
            Reason = reason,
            Attempts = 1,
            FirstFailedAt = now,
            LastFailedAt = now,
            NextAttemptAt = nextAttemptAt,
            LastError = Shorten(error)
        };
    }

    /// <summary>Another failed attempt. A resolved hold that fails again is open again, counting afresh.</summary>
    public void Fail(IssuanceHoldReason reason, string? error, DateTimeOffset now, DateTimeOffset nextAttemptAt)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (ResolvedAt is not null)
        {
            ResolvedAt = null;
            Attempts = 0;
            FirstFailedAt = now;
        }

        Reason = reason;
        Attempts++;
        LastFailedAt = now;
        NextAttemptAt = nextAttemptAt;
        LastError = Shorten(error);
    }

    /// <summary>The document was issued.</summary>
    public void Resolve(DateTimeOffset now) => ResolvedAt ??= now;

    private static string? Shorten(string? error) =>
        error is null ? null : error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
}
