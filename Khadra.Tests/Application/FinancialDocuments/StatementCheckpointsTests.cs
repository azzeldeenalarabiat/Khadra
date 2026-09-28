using Khadra.Application.FinancialDocuments.Composition;
using Khadra.Application.Payments;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;
using Khadra.Tests.Support;

namespace Khadra.Tests.Application.FinancialDocuments;

/// <summary>
/// When a statement gets a new version (owner, 2026-09-27): only when money moved — a closed list of
/// checkpoints, fingerprinted on the facts themselves — and never because the clock moved or a refund was
/// merely recorded, sent or refused.
/// </summary>
public sealed class StatementCheckpointsTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    [Fact]
    public void A_capture_is_the_first_checkpoint()
    {
        var (booking, payment) = Build.PaidBooking();

        var checkpoints = StatementCheckpoints.Of(booking, [payment], [], []);

        var only = Assert.Single(checkpoints.All);
        Assert.Equal(FinancialDocumentCause.PaymentCaptured, only.Kind);
        Assert.Equal(payment.AppliedAt, only.At);
        Assert.Equal(payment.AppliedAt, checkpoints.CoversThrough);
        Assert.Equal(64, checkpoints.Fingerprint.Length);
    }

    [Fact]
    public void An_unpaid_booking_has_nothing_to_state() =>
        Assert.True(StatementCheckpoints.Of(Build.ApprovedBooking(), [], [], []).IsEmpty);

    [Fact]
    public void The_clock_alone_never_changes_the_fingerprint()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now).IsSuccess);
        var before = StatementCheckpoints.Of(booking, [payment], [], []).Fingerprint;

        // Days pass, the dispute window closes: nothing stored changed, so nothing is new.
        var after = StatementCheckpoints.Of(booking, [payment], [], []).Fingerprint;

        Assert.Equal(before, after);
    }

    [Fact]
    public void A_refund_being_recorded_sent_or_refused_is_not_a_checkpoint_and_its_settlement_is()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now).IsSuccess);
        var ended = StatementCheckpoints.Of(booking, [payment], [], []).Fingerprint;

        var refund = BookingEndingRefunds.Record(booking, payment, Now)!;
        Assert.Equal(ended, StatementCheckpoints.Of(booking, [payment], [], []).Fingerprint);

        refund.MarkSent("rf_1", Now.AddMinutes(1));
        Assert.Equal(ended, StatementCheckpoints.Of(booking, [payment], [], []).Fingerprint);

        refund.MarkFailed("card_expired", Now.AddMinutes(2));
        Assert.Equal(ended, StatementCheckpoints.Of(booking, [payment], [], []).Fingerprint);

        refund.MarkSent("rf_2", Now.AddMinutes(3));
        refund.MarkSettled(Now.AddMinutes(4));
        var settled = StatementCheckpoints.Of(booking, [payment], [], []);
        Assert.NotEqual(ended, settled.Fingerprint);
        Assert.Equal(Now.AddMinutes(4), settled.CoversThrough);
        Assert.Equal(FinancialDocumentCause.RefundSettled, settled.Latest.Kind);
    }

    [Fact]
    public void The_booking_ending_is_a_checkpoint()
    {
        var (booking, payment) = Build.PaidBooking();
        var paid = StatementCheckpoints.Of(booking, [payment], [], []).Fingerprint;

        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now.AddMinutes(5)).IsSuccess);
        var ended = StatementCheckpoints.Of(booking, [payment], [], []);

        Assert.NotEqual(paid, ended.Fingerprint);
        Assert.Equal(FinancialDocumentCause.BookingEnded, ended.Latest.Kind);
    }

    [Fact]
    public void Cash_recorded_at_a_handover_is_a_checkpoint()
    {
        var (booking, payment) = Build.PaidBooking();
        var paid = StatementCheckpoints.Of(booking, [payment], [], []).Fingerprint;

        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start, cashCollected: Money.Jod(72m)).IsSuccess);
        var cash = StatementCheckpoints.Of(booking, [payment], [], []);

        Assert.NotEqual(paid, cash.Fingerprint);
        Assert.Equal(FinancialDocumentCause.CashRecorded, cash.Latest.Kind);
    }

    [Fact]
    public void A_pickup_with_no_cash_is_not_one()
    {
        var (booking, payment) = Build.PaidBooking();
        var paid = StatementCheckpoints.Of(booking, [payment], [], []).Fingerprint;

        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start).IsSuccess);

        Assert.Equal(paid, StatementCheckpoints.Of(booking, [payment], [], []).Fingerprint);
    }

    [Fact]
    public void A_resolved_dispute_is_a_checkpoint_and_wins_a_tie_with_the_ending_it_brings_about()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, booking.FreeCancellationDeadline!.Value.AddMinutes(1)).IsSuccess);
        var ended = StatementCheckpoints.Of(booking, [payment], [], []).Fingerprint;
        var open = DisputeTicket.Open(booking.Id, booking.CustomerId, BookingParty.Customer, "Charged.", TimeSpan.FromHours(48), Now.AddHours(1)).Value;

        // An open ticket decides nothing: not a checkpoint.
        Assert.Equal(ended, StatementCheckpoints.Of(booking, [payment], [open], []).Fingerprint);

        var decidedAt = booking.FinishedAt!.Value;
        Assert.True(open.Resolve(DisputeResolution.Create(
            DepositDisposition.Create(Money.Jod(18m), Money.Jod(18m), Money.Jod(0m), Money.Jod(0m)).Value,
            null, null, "Refund.", Id.New(), decidedAt).Value).IsSuccess);
        var resolved = StatementCheckpoints.Of(booking, [payment], [open], []);

        Assert.NotEqual(ended, resolved.Fingerprint);
        Assert.Equal(booking.FinishedAt, open.ClosedAt);
        Assert.Equal(FinancialDocumentCause.DisputeResolved, resolved.Latest.Kind);
    }

    [Fact]
    public void A_capture_that_never_applied_still_moved_money()
    {
        var booking = Build.ApprovedBooking();
        var payment = Payment.Open(booking.Id, booking.CustomerId, Money.Jod(18m), "TestProvider", Now.AddMinutes(30), Now, PaymentPurpose.Deposit, Money.Jod(0m), true);
        Assert.True(payment.AttachProviderSession("sess_x", "https://provider.test/checkout").IsSuccess);
        Assert.True(payment.Orphan(Money.Jod(18m), Now, "booking.not_awaiting_payment", Now).IsSuccess);

        var checkpoints = StatementCheckpoints.Of(booking, [payment], [], []);

        Assert.Equal(FinancialDocumentCause.PaymentCaptured, Assert.Single(checkpoints.All).Kind);
    }

    [Fact]
    public void Another_bookings_money_is_not_this_ones()
    {
        var (booking, _) = Build.PaidBooking();
        var (_, other) = Build.PaidBooking();

        Assert.True(StatementCheckpoints.Of(booking, [other], [], []).IsEmpty);
    }

    [Fact]
    public void The_fingerprint_does_not_encode_the_offices_or_the_platforms_share()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, booking.FreeCancellationDeadline!.Value.AddMinutes(1)).IsSuccess);
        var ticket = DisputeTicket.Open(booking.Id, booking.CustomerId, BookingParty.Customer, "Charged.", TimeSpan.FromHours(48), Now.AddHours(1)).Value;
        Assert.True(ticket.Resolve(DisputeResolution.Create(
            DepositDisposition.Create(Money.Jod(18m), Money.Jod(13m), Money.Jod(0m), Money.Jod(5m)).Value,
            null, null, "Split.", Id.New(), Now.AddHours(2)).Value).IsSuccess);

        var dispute = StatementCheckpoints.Of(booking, [payment], [ticket], []).All.Single(checkpoint => checkpoint.Kind == FinancialDocumentCause.DisputeResolved);

        Assert.Equal(string.Empty, dispute.Facts);
    }

    // ── A receipt's correction (owner, 2026-09-28; pre-launch item 181) ─────────────────────────────

    [Fact]
    public void A_receipts_correction_is_the_newest_fact_and_the_money_keeps_its_own_instant()
    {
        var (booking, payment) = Build.PaidBooking();
        var receipt = Receipt(booking, payment, Now.AddMinutes(1));
        var correction = Receipt(booking, payment, Now.AddDays(1), corrects: receipt);

        var checkpoints = StatementCheckpoints.Of(booking, [payment], [], [receipt, correction]);

        var corrected = Assert.Single(checkpoints.All, checkpoint => checkpoint.Kind == FinancialDocumentCause.ReceiptCorrected);
        Assert.Equal(correction.Id.Value, corrected.Key);
        Assert.Equal(correction.IssuedAt, corrected.At);
        Assert.Equal(string.Empty, corrected.Facts);
        // Why the version exists; when its money last moved; how far its facts run.
        Assert.Equal(FinancialDocumentCause.ReceiptCorrected, checkpoints.Latest.Kind);
        Assert.Equal(payment.AppliedAt, checkpoints.MoneyMovedAt);
        Assert.Equal(correction.IssuedAt, checkpoints.CoversThrough);
    }

    [Fact]
    public void A_receipt_never_corrected_adds_nothing_so_the_fingerprint_is_what_it_always_was()
    {
        var (booking, payment) = Build.PaidBooking();
        var before = StatementCheckpoints.Of(booking, [payment], [], []);

        var after = StatementCheckpoints.Of(booking, [payment], [], [Receipt(booking, payment, Now.AddMinutes(1))]);

        Assert.Equal(before.Fingerprint, after.Fingerprint);
        Assert.Equal(before.All, after.All);
        Assert.Equal(after.Latest.At, after.MoneyMovedAt);
    }

    [Fact]
    public void Only_a_receipts_latest_version_counts_so_each_correction_changes_the_fingerprint_once()
    {
        var (booking, payment) = Build.PaidBooking();
        var receipt = Receipt(booking, payment, Now.AddMinutes(1));
        var correction = Receipt(booking, payment, Now.AddDays(1), corrects: receipt);
        var again = Receipt(booking, payment, Now.AddDays(2), corrects: correction);

        var once = StatementCheckpoints.Of(booking, [payment], [], [receipt, correction]);
        var twice = StatementCheckpoints.Of(booking, [payment], [], [receipt, correction, again]);

        Assert.NotEqual(once.Fingerprint, twice.Fingerprint);
        Assert.Equal(again.Id.Value, Assert.Single(twice.All, checkpoint => checkpoint.Kind == FinancialDocumentCause.ReceiptCorrected).Key);
        // The latest alone, however the versions arrive.
        Assert.Equal(twice.Fingerprint, StatementCheckpoints.Of(booking, [payment], [], [again]).Fingerprint);
    }

    [Fact]
    public void A_statements_own_correction_and_another_bookings_receipts_are_never_checkpoints()
    {
        var (booking, payment) = Build.PaidBooking();
        var (other, otherPayment) = Build.PaidBooking();
        var othersCorrection = Receipt(other, otherPayment, Now.AddDays(1), corrects: Receipt(other, otherPayment, Now.AddMinutes(1)));
        var statementsCorrection = StatementCorrection(booking, payment, Now.AddDays(1));
        var plain = StatementCheckpoints.Of(booking, [payment], [], []);

        var read = StatementCheckpoints.Of(booking, [payment], [], [othersCorrection, statementsCorrection]);

        Assert.Equal(FinancialDocumentCause.Correction, statementsCorrection.Cause);
        Assert.Equal(plain.Fingerprint, read.Fingerprint);
    }

    [Fact]
    public void Money_at_the_same_instant_as_a_correction_names_the_statement()
    {
        var (booking, payment) = Build.PaidBooking();
        var receipt = Receipt(booking, payment, Now.AddMinutes(1));
        var pickup = booking.Period.Start;
        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), pickup, cashCollected: Money.Jod(booking.Pricing.BalanceDue.Amount)).IsSuccess);

        var checkpoints = StatementCheckpoints.Of(booking, [payment], [], [Receipt(booking, payment, pickup, corrects: receipt)]);

        Assert.Equal(FinancialDocumentCause.CashRecorded, checkpoints.Latest.Kind);
        Assert.Equal(pickup, checkpoints.MoneyMovedAt);
        Assert.Equal(pickup, checkpoints.CoversThrough);
    }

    [Fact]
    public void A_correction_with_no_captured_money_beside_it_is_a_defect_not_a_money_instant()
    {
        var (booking, payment) = Build.PaidBooking();
        var correction = Receipt(booking, payment, Now.AddDays(1), corrects: Receipt(booking, payment, Now.AddMinutes(1)));

        var checkpoints = StatementCheckpoints.Of(booking, [], [], [correction]);

        Assert.Equal(FinancialDocumentCause.ReceiptCorrected, Assert.Single(checkpoints.All).Kind);
        Assert.Throws<InvalidOperationException>(() => checkpoints.MoneyMovedAt);
    }

    private static readonly FinancialDocumentComposer Composer = DocumentFixtures.Composer();

    /// <summary>The booking's payment receipt as issued — or, given the version it corrects, that version's correction.</summary>
    private static FinancialDocument Receipt(Booking booking, Payment payment, DateTimeOffset issuedAt, FinancialDocument? corrects = null)
    {
        var number = $"TEST-PAY-2026-{(corrects?.Version ?? 0) + 1:D6}";
        var stamp = corrects is null
            ? DocumentStamp.First(number, issuedAt)
            : new DocumentStamp(number, issuedAt, corrects.Version + 1, new DocumentReference(corrects.Id, corrects.Number), true);
        var draft = Composer.PaymentReceipt(
            new PaymentReceiptFacts(DocumentFixtures.Issuer, DocumentFixtures.PartiesOf(booking), booking, payment, [payment]), stamp);
        return FinancialDocument.Issue(draft, number, issuedAt);
    }

    /// <summary>The correction of the booking's first statement: a document whose cause is Correction, and no receipt.</summary>
    private static FinancialDocument StatementCorrection(Booking booking, Payment payment, DateTimeOffset issuedAt)
    {
        const string number = "TEST-STM-2026-000002";
        var stamp = new DocumentStamp(number, issuedAt, 2, new DocumentReference(Id.New(), "TEST-STM-2026-000001"), true);
        var draft = Composer.Statement(
            new StatementFacts(
                DocumentFixtures.Issuer, DocumentFixtures.PartiesOf(booking), booking,
                BookingFinancialsCalculator.Calculate(booking, [payment], [], false, issuedAt),
                StatementCheckpoints.Of(booking, [payment], [], []), false, []),
            stamp,
            payment.Provider);
        return FinancialDocument.Issue(draft, number, issuedAt);
    }
}
