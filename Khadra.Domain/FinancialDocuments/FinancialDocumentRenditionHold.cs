using Khadra.Domain.Common;

namespace Khadra.Domain.FinancialDocuments;

/// <summary>Why a document's PDF could not be drawn (pre-launch item 197). Stored by name: add-only.</summary>
public sealed class RenditionHoldReason : Enumeration
{
    /// <summary>The stored snapshot no longer hashes to what was issued. A PDF shows the record as issued or nothing.</summary>
    public static readonly RenditionHoldReason SnapshotAltered = new(1, "SnapshotAltered");

    /// <summary>The print layout could not read the snapshot (an unsupported schema, or one it failed on).</summary>
    public static readonly RenditionHoldReason SnapshotUnreadable = new(2, "SnapshotUnreadable");

    /// <summary>The PDF library failed while drawing this document.</summary>
    public static readonly RenditionHoldReason DrawingFailed = new(3, "DrawingFailed");

    private RenditionHoldReason(int id, string name) : base(id, name)
    {
    }
}

/// <summary>
/// A document's PDF, in one language and kind, that could not be drawn — and why (pre-launch item 197).
/// </summary>
/// <remarks>
/// <para>
/// Drawing it again in the same process would fail the same way, so the pass leaves it until the next start and the
/// customer's page goes on saying the PDF is being prepared. That used to be visible only in the log. This is the
/// durable record an administrator reads: one row per document, language and kind, upserted on each failed attempt,
/// as issuing's holds are.
/// </para>
/// <para>
/// There is no "resolved" state to keep in step: a hold is open exactly while no PDF of that document, language and
/// kind exists, which the readers ask. A later fix that lets it draw closes it by drawing it.
/// </para>
/// </remarks>
public sealed class FinancialDocumentRenditionHold : AggregateRoot
{
    public Id DocumentId { get; private set; }
    public Language Language { get; private set; } = null!;
    public RenditionKind Kind { get; private set; } = null!;
    public RenditionHoldReason Reason { get; private set; } = null!;
    public int Attempts { get; private set; }
    public DateTimeOffset FirstFailedAt { get; private set; }
    public DateTimeOffset LastFailedAt { get; private set; }

    private FinancialDocumentRenditionHold()
    {
    }

    private FinancialDocumentRenditionHold(Id id) : base(id)
    {
    }

    public static FinancialDocumentRenditionHold Open(
        Id documentId, Language language, RenditionKind kind, RenditionHoldReason reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(reason);
        if (documentId.IsEmpty)
            throw new DomainException("A rendition hold names its document.");

        return new FinancialDocumentRenditionHold(Id.New())
        {
            DocumentId = documentId,
            Language = language,
            Kind = kind,
            Reason = reason,
            Attempts = 1,
            FirstFailedAt = now,
            LastFailedAt = now,
        };
    }

    /// <summary>It failed again (the next start tried it): the latest reason, and one more attempt.</summary>
    public void Fail(RenditionHoldReason reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reason);
        Reason = reason;
        Attempts++;
        LastFailedAt = now;
    }
}
