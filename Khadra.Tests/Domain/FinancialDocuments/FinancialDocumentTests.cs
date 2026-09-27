using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.FinancialDocuments;

/// <summary>
/// An issued financial document's own rules (payments Phase 5): how numbers read, what a document may be
/// about, how versions chain, and what a void and a hold record.
/// </summary>
public sealed class FinancialDocumentTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    // ── Numbers ────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("PaymentReceipt", false, "PAY-2026")]
    [InlineData("RefundReceipt", false, "RFD-2026")]
    [InlineData("BookingStatement", false, "STM-2026")]
    [InlineData("PaymentReceipt", true, "TEST-PAY-2026")]
    [InlineData("RefundReceipt", true, "TEST-RFD-2026")]
    [InlineData("BookingStatement", true, "TEST-STM-2026")]
    public void A_series_is_one_per_type_kind_of_money_and_issue_year(string type, bool isTest, string expected) =>
        Assert.Equal(expected, FinancialDocumentNumbers.SeriesKey(Enumeration.FromName<FinancialDocumentType>(type), 2026, isTest));

    [Theory]
    [InlineData(1L, "PAY-2026-000001")]
    [InlineData(42L, "PAY-2026-000042")]
    [InlineData(999_999L, "PAY-2026-999999")]
    // Six digits, growing to seven if a year ever needs it — never truncated, never wrapped.
    [InlineData(1_000_000L, "PAY-2026-1000000")]
    public void A_number_has_six_digits_and_grows_rather_than_wraps(long sequence, string expected) =>
        Assert.Equal(expected, FinancialDocumentNumbers.Format("PAY-2026", sequence));

    [Fact]
    public void The_longest_number_fits_its_column()
    {
        var key = FinancialDocumentNumbers.SeriesKey(FinancialDocumentType.BookingStatement, 9999, isTest: true);

        Assert.True(key.Length <= FinancialDocumentNumbers.MaxSeriesKeyLength);
        Assert.True(FinancialDocumentNumbers.Format(key, 9_999_999).Length <= FinancialDocumentNumbers.MaxLength);
    }

    [Fact]
    public void A_series_starts_at_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FinancialDocumentNumbers.Format("PAY-2026", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FinancialDocumentNumbers.SeriesKey(FinancialDocumentType.PaymentReceipt, 999, false));
    }

    // ── What a document is about ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Issuing_freezes_every_column_and_hashes_the_snapshot_exactly()
    {
        var draft = Receipt();

        var document = FinancialDocument.Issue(draft, "PAY-2026-000001", Now);

        Assert.Equal("PAY-2026-000001", document.Number);
        Assert.Equal(FinancialDocumentType.PaymentReceipt, document.Type);
        Assert.Equal(draft.SubjectId, document.SubjectId);
        Assert.Equal(1, document.Version);
        Assert.Null(document.PreviousVersionId);
        Assert.Equal(Now, document.IssuedAt);
        Assert.Equal(draft.Snapshot, document.Snapshot);
        Assert.Equal(FinancialDocument.Sha256(draft.Snapshot), document.ContentSha256);
        Assert.Equal(64, document.ContentSha256.Length);
        Assert.Equal(draft.HeadlineAmount, document.HeadlineAmount);
        // A copy, never the draft's instance: EF tracks an owned value by reference.
        Assert.NotSame(draft.HeadlineAmount, document.HeadlineAmount);
    }

    [Fact]
    public void Test_money_is_read_from_the_frozen_provider_and_nothing_else()
    {
        Assert.True(FinancialDocument.Issue(Receipt() with { Provider = PaymentProviders.Sandbox }, "TEST-PAY-2026-000001", Now).IsTest);
        Assert.False(FinancialDocument.Issue(Receipt() with { Provider = "Stripe" }, "PAY-2026-000001", Now).IsTest);
    }

    [Fact]
    public void Version_one_supersedes_nothing_and_every_later_version_names_the_one_before()
    {
        Assert.Throws<DomainException>(() => FinancialDocument.Issue(Receipt() with { PreviousVersionId = Id.New() }, "PAY-2026-000001", Now));
        Assert.Throws<DomainException>(() => FinancialDocument.Issue(Statement() with { Version = 2 }, "STM-2026-000002", Now));
        Assert.Throws<DomainException>(() => FinancialDocument.Issue(Receipt() with { Version = 0 }, "PAY-2026-000001", Now));
    }

    [Fact]
    public void A_receipt_gains_a_version_only_through_a_correction()
    {
        var second = Receipt() with { Version = 2, PreviousVersionId = Id.New() };

        Assert.Throws<DomainException>(() => FinancialDocument.Issue(second, "PAY-2026-000002", Now));
        var correction = FinancialDocument.Issue(second with { Cause = FinancialDocumentCause.Correction }, "PAY-2026-000002", Now);
        Assert.Equal(2, correction.Version);
    }

    [Fact]
    public void A_correction_is_never_a_first_version() =>
        Assert.Throws<DomainException>(() =>
            FinancialDocument.Issue(Receipt() with { Cause = FinancialDocumentCause.Correction }, "PAY-2026-000001", Now));

    [Fact]
    public void A_statement_records_what_it_covered_and_a_receipt_covers_nothing()
    {
        Assert.Throws<DomainException>(() => FinancialDocument.Issue(Statement() with { CoversThrough = null }, "STM-2026-000001", Now));
        Assert.Throws<DomainException>(() => FinancialDocument.Issue(Statement() with { CheckpointFingerprint = "short" }, "STM-2026-000001", Now));
        Assert.Throws<DomainException>(() =>
            FinancialDocument.Issue(Receipt() with { CoversThrough = Now, CheckpointFingerprint = new string('a', 64) }, "PAY-2026-000001", Now));
    }

    [Fact]
    public void Each_type_is_about_its_own_subject()
    {
        var receipt = Receipt();
        Assert.Throws<DomainException>(() => FinancialDocument.Issue(receipt with { SubjectId = Id.New() }, "PAY-2026-000001", Now));
        Assert.Throws<DomainException>(() => FinancialDocument.Issue(receipt with { RefundId = Id.New() }, "PAY-2026-000001", Now));

        var refund = Id.New();
        var refundReceipt = receipt with { Type = FinancialDocumentType.RefundReceipt, SubjectId = refund, RefundId = refund, Cause = FinancialDocumentCause.RefundSettled };
        Assert.Equal(refund, FinancialDocument.Issue(refundReceipt, "RFD-2026-000001", Now).SubjectId);
        Assert.Throws<DomainException>(() => FinancialDocument.Issue(refundReceipt with { PaymentId = null }, "RFD-2026-000001", Now));

        Assert.Throws<DomainException>(() => FinancialDocument.Issue(Statement() with { SubjectId = Id.New() }, "STM-2026-000001", Now));
    }

    [Fact]
    public void A_number_or_a_reference_longer_than_its_column_is_a_defect()
    {
        Assert.Throws<DomainException>(() => FinancialDocument.Issue(Receipt(), new string('9', FinancialDocumentNumbers.MaxLength + 1), Now));
        Assert.Throws<DomainException>(() => FinancialDocument.Issue(Receipt() with { BookingReference = new string('K', 21) }, "PAY-2026-000001", Now));
    }

    // ── Standing ───────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, false, "Current")]
    [InlineData(false, true, "Superseded")]
    [InlineData(true, false, "Voided")]
    // A voided document is always superseded too — its correction is the next version — and "voided" is what a reader must be told.
    [InlineData(true, true, "Voided")]
    public void Standing_is_derived_and_a_void_wins(bool voided, bool superseded, string expected) =>
        Assert.Equal(expected, FinancialDocumentStatus.Of(voided, superseded).Name);

    // ── Voids ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_void_is_keyed_by_its_document_and_keeps_the_reason_trimmed()
    {
        var document = Id.New();
        var admin = Id.New();

        var voided = FinancialDocumentVoid.Record(document, admin, "  Wrong office name  ", Now).Value;

        Assert.Equal(document, voided.DocumentId);
        Assert.Equal(document, voided.Id);
        Assert.Equal(admin, voided.VoidedByAdminId);
        Assert.Equal("Wrong office name", voided.Reason);
        Assert.Equal(Now, voided.VoidedAt);
    }

    [Theory]
    [InlineData(null, "financial_documents.void_reason_required")]
    [InlineData("   ", "financial_documents.void_reason_required")]
    public void A_void_says_why(string? reason, string code) =>
        Assert.Equal(code, FinancialDocumentVoid.Record(Id.New(), Id.New(), reason, Now).Error.Code);

    [Fact]
    public void A_void_reason_has_a_limit() =>
        Assert.Equal(
            "financial_documents.void_reason_too_long",
            FinancialDocumentVoid.Record(Id.New(), Id.New(), new string('x', FinancialDocumentVoid.MaxReasonLength + 1), Now).Error.Code);

    // ── Holds ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_hold_counts_its_attempts_and_a_resolved_hold_that_fails_again_counts_afresh()
    {
        var hold = FinancialDocumentIssuanceHold.Open(
            FinancialDocumentType.BookingStatement, Id.New(), Id.New(), IssuanceHoldReason.RecordsNeedReview, "EndingRefundMissing", Now, Now.AddMinutes(1));
        hold.Fail(IssuanceHoldReason.RecordsNeedReview, "EndingRefundMissing", Now.AddMinutes(1), Now.AddMinutes(3));

        Assert.Equal(2, hold.Attempts);
        Assert.Equal(Now, hold.FirstFailedAt);
        Assert.Equal(Now.AddMinutes(3), hold.NextAttemptAt);
        Assert.False(hold.IsResolved);

        hold.Resolve(Now.AddMinutes(5));
        Assert.True(hold.IsResolved);

        hold.Fail(IssuanceHoldReason.SnapshotFailed, "boom", Now.AddMinutes(9), Now.AddMinutes(10));
        Assert.False(hold.IsResolved);
        Assert.Equal(1, hold.Attempts);
        Assert.Equal(Now.AddMinutes(9), hold.FirstFailedAt);
        Assert.Equal(IssuanceHoldReason.SnapshotFailed, hold.Reason);
    }

    [Fact]
    public void A_holds_error_is_kept_to_its_column() =>
        Assert.Equal(
            FinancialDocumentIssuanceHold.MaxErrorLength,
            FinancialDocumentIssuanceHold.Open(
                FinancialDocumentType.PaymentReceipt, Id.New(), Id.New(), IssuanceHoldReason.SnapshotFailed, new string('e', 1000), Now, Now)
            .LastError!.Length);

    // ── Drafts ─────────────────────────────────────────────────────────────────────────────────────

    private static FinancialDocumentDraft Receipt()
    {
        var payment = Id.New();
        return new FinancialDocumentDraft(
            FinancialDocumentType.PaymentReceipt,
            payment,
            1,
            null,
            null,
            Id.New(),
            "KH-TESTREF1",
            Id.New(),
            Id.New(),
            payment,
            null,
            FinancialDocumentCause.PaymentCaptured,
            Now,
            null,
            null,
            Money.Jod(102.75m),
            "TestProvider",
            1,
            1,
            """{"schemaVersion":1}""");
    }

    private static FinancialDocumentDraft Statement()
    {
        var booking = Id.New();
        return new FinancialDocumentDraft(
            FinancialDocumentType.BookingStatement,
            booking,
            1,
            null,
            null,
            booking,
            "KH-TESTREF1",
            Id.New(),
            Id.New(),
            null,
            null,
            FinancialDocumentCause.PaymentCaptured,
            Now,
            Now,
            new string('a', 64),
            Money.Jod(102.75m),
            "TestProvider",
            1,
            1,
            """{"schemaVersion":1}""");
    }
}
