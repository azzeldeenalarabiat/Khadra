using System.Security.Cryptography;
using System.Text;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;

namespace Khadra.Domain.FinancialDocuments;

/// <summary>
/// What a document is about and why it is being issued, before it has a number (payments Phase 5).
/// Produced by the composer from the booking's facts; the number and the issue instant are added when
/// the document is issued, in the transaction that takes the number.
/// </summary>
/// <param name="SubjectId">The payment (a payment receipt), the refund (a refund receipt), or the booking (a statement).</param>
/// <param name="PreviousVersionId">The version this one supersedes, or the voided document it corrects. Null for version 1.</param>
/// <param name="RelatedDocumentId">A refund receipt's payment receipt.</param>
/// <param name="OccurredAt">When the money event it records happened.</param>
/// <param name="CoversThrough">Statements only: the latest checkpoint instant the statement saw.</param>
/// <param name="CheckpointFingerprint">Statements only: SHA-256 (hex) of the checkpoint facts it covers.</param>
/// <param name="HeadlineAmount">
/// The one figure a list row shows: the amount charged, the amount refunded, or the net paid online.
/// In the currency of the document — a receipt's is its payment's or refund's own.
/// </param>
/// <param name="Provider">A frozen copy of <c>payments.provider</c>: the one marker of test money, never a second flag.</param>
/// <param name="Snapshot">The canonical JSON of the whole document, exactly as issued.</param>
public sealed record FinancialDocumentDraft(
    FinancialDocumentType Type,
    Id SubjectId,
    int Version,
    Id? PreviousVersionId,
    Id? RelatedDocumentId,
    Id BookingId,
    string BookingReference,
    Id CustomerId,
    Id DealerId,
    Id? PaymentId,
    Id? RefundId,
    FinancialDocumentCause Cause,
    DateTimeOffset OccurredAt,
    DateTimeOffset? CoversThrough,
    string? CheckpointFingerprint,
    Money HeadlineAmount,
    string Provider,
    int CalculatorVersion,
    int SnapshotSchemaVersion,
    string Snapshot);

/// <summary>
/// One issued financial document — a payment receipt, a refund receipt, or one version of a booking
/// statement — and the official record of it (owner, 2026-09-27). A PDF (Phase 6) and an email (Phase 7)
/// are representations and deliveries derived from this row, never the record.
/// </summary>
/// <remarks>
/// <para>
/// <b>Append-only, enforced twice</b>: <c>KhadraDbContext</c> refuses to modify or delete one, and
/// database triggers refuse <c>UPDATE</c>, <c>DELETE</c> and <c>TRUNCATE</c> for anything that bypasses
/// the application. A wrong document is voided and corrected (<see cref="FinancialDocumentVoid"/>),
/// never edited. Its status — current, superseded or voided — is therefore derived when it is read,
/// never stored.
/// </para>
/// <para>
/// <b>Nothing is read live.</b> Every name, figure and word the document shows is inside
/// <see cref="Snapshot"/>, copied at issue; the scalar columns beside it exist for listing and access
/// control only, and are written from the same draft.
/// </para>
/// </remarks>
public sealed class FinancialDocument : AggregateRoot, IAppendOnly
{
    public const int BookingReferenceMaxLength = 20;
    public const int ProviderMaxLength = 30;
    public const int HashLength = 64;

    public FinancialDocumentType Type { get; private set; } = null!;
    public string Number { get; private set; } = null!;
    public Id SubjectId { get; private set; }
    public int Version { get; private set; }
    public Id? PreviousVersionId { get; private set; }
    public Id? RelatedDocumentId { get; private set; }
    public Id BookingId { get; private set; }
    public string BookingReference { get; private set; } = null!;
    public Id CustomerId { get; private set; }
    public Id DealerId { get; private set; }
    public Id? PaymentId { get; private set; }
    public Id? RefundId { get; private set; }
    public FinancialDocumentCause Cause { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset? CoversThrough { get; private set; }
    public string? CheckpointFingerprint { get; private set; }
    public Money HeadlineAmount { get; private set; } = null!;
    public string Provider { get; private set; } = null!;
    public int CalculatorVersion { get; private set; }
    public int SnapshotSchemaVersion { get; private set; }
    public string Snapshot { get; private set; } = null!;

    /// <summary>SHA-256 (lowercase hex) of the snapshot's UTF-8 bytes: proves what was issued, byte for byte.</summary>
    public string ContentSha256 { get; private set; } = null!;

    /// <summary>About sandbox money: read from the frozen provider, exactly as a payment reads its own.</summary>
    public bool IsTest => PaymentProviders.IsSandbox(Provider);

    private FinancialDocument()
    {
    }

    private FinancialDocument(Id id) : base(id)
    {
    }

    /// <summary>
    /// Issues a document from its draft under a number already taken for it. Every invariant here is
    /// a programming error if broken — the composer and the issuer build drafts — so it throws rather
    /// than returning a result a caller could ignore.
    /// </summary>
    public static FinancialDocument Issue(FinancialDocumentDraft draft, string number, DateTimeOffset issuedAt)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentNullException.ThrowIfNull(draft.Type);
        ArgumentNullException.ThrowIfNull(draft.Cause);
        ArgumentNullException.ThrowIfNull(draft.HeadlineAmount);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.BookingReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Snapshot);

        if (number.Length > FinancialDocumentNumbers.MaxLength)
            throw new DomainException($"A document number is at most {FinancialDocumentNumbers.MaxLength} characters.");
        if (draft.BookingReference.Length > BookingReferenceMaxLength || draft.Provider.Length > ProviderMaxLength)
            throw new DomainException("A booking reference or a provider is longer than its column.");
        if (draft.SubjectId.IsEmpty || draft.BookingId.IsEmpty || draft.CustomerId.IsEmpty || draft.DealerId.IsEmpty)
            throw new DomainException("A document names its subject, its booking, its customer and its office.");
        if (draft.CalculatorVersion < 1 || draft.SnapshotSchemaVersion < 1)
            throw new DomainException("A document records the calculator and the snapshot schema it was built by.");

        RequireVersioning(draft);
        RequireSubject(draft);

        return new FinancialDocument(Id.New())
        {
            Type = draft.Type,
            Number = number,
            SubjectId = draft.SubjectId,
            Version = draft.Version,
            PreviousVersionId = draft.PreviousVersionId,
            RelatedDocumentId = draft.RelatedDocumentId,
            BookingId = draft.BookingId,
            BookingReference = draft.BookingReference,
            CustomerId = draft.CustomerId,
            DealerId = draft.DealerId,
            PaymentId = draft.PaymentId,
            RefundId = draft.RefundId,
            Cause = draft.Cause,
            OccurredAt = draft.OccurredAt,
            IssuedAt = issuedAt,
            CoversThrough = draft.CoversThrough,
            CheckpointFingerprint = draft.CheckpointFingerprint,
            // A fresh Money: EF tracks an owned value by reference, and one instance owned by two rows
            // is the pattern the architecture rules forbid.
            HeadlineAmount = Money.Create(draft.HeadlineAmount.Amount, draft.HeadlineAmount.CurrencyCode),
            Provider = draft.Provider,
            CalculatorVersion = draft.CalculatorVersion,
            SnapshotSchemaVersion = draft.SnapshotSchemaVersion,
            Snapshot = draft.Snapshot,
            ContentSha256 = Sha256(draft.Snapshot)
        };
    }

    /// <summary>Lowercase hex SHA-256 of a string's UTF-8 bytes — the one hash every document field uses.</summary>
    public static string Sha256(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static void RequireVersioning(FinancialDocumentDraft draft)
    {
        if (draft.Version < 1)
            throw new DomainException("A document's first version is 1.");
        if ((draft.Version == 1) != draft.PreviousVersionId is null)
            throw new DomainException("Version 1 supersedes nothing; every later version names the one before it.");
        if (draft.Cause == FinancialDocumentCause.Correction && draft.Version == 1)
            throw new DomainException("A correction replaces a voided document, so it is never a first version.");
        if (draft.Type.IsReceipt && draft.Version > 1 && draft.Cause != FinancialDocumentCause.Correction)
            throw new DomainException("A receipt gains a version only through a correction.");
    }

    private static void RequireSubject(FinancialDocumentDraft draft)
    {
        var isStatement = draft.Type == FinancialDocumentType.BookingStatement;
        var coverage = draft.CoversThrough is not null && draft.CheckpointFingerprint is not null;
        if (isStatement != coverage)
            throw new DomainException("A statement records what it covered; a receipt covers nothing but its own money.");
        if (draft.CheckpointFingerprint is { Length: not HashLength })
            throw new DomainException("A checkpoint fingerprint is a SHA-256 in hex.");

        var consistent =
            draft.Type == FinancialDocumentType.PaymentReceipt
                ? draft.PaymentId is { } payment && draft.RefundId is null && draft.SubjectId == payment
            : draft.Type == FinancialDocumentType.RefundReceipt
                ? draft.PaymentId is not null && draft.RefundId is { } refund && draft.SubjectId == refund
            : draft.RefundId is null && draft.SubjectId == draft.BookingId;
        if (!consistent)
            throw new DomainException($"A {draft.Type.Name} is about the wrong subject.");
    }
}
