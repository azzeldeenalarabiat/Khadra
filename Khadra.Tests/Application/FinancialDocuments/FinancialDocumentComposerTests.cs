using System.Text.Json;
using Khadra.Application.FinancialDocuments.Composition;
using Khadra.Application.Payments;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;
using Khadra.Tests.Support;
using static Khadra.Tests.Support.DocumentFixtures;

namespace Khadra.Tests.Application.FinancialDocuments;

/// <summary>
/// What each document says (payments Phase 5): every figure from immutable facts or the customer's
/// projection, every word frozen in English and Arabic, and nothing a customer may not see.
/// </summary>
public sealed class FinancialDocumentComposerTests
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly FinancialDocumentComposer Composer = DocumentFixtures.Composer();

    // ── Payment receipts ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_deposit_receipt_states_the_charge_the_fee_and_the_balance_due_at_pickup()
    {
        var (booking, payment) = Build.PaidBooking(fee: 1.5m);

        var draft = PaymentReceipt(booking, payment);
        var snapshot = Parse(draft.Snapshot);
        var facts = snapshot.GetProperty("facts");

        var deposit = booking.Pricing.DepositAmount.Amount;
        Assert.Equal(Fixed3(deposit + 1.5m), Amount(facts.GetProperty("amountCharged")));
        Assert.Equal("1.500", Amount(facts.GetProperty("processingFee")));
        Assert.Equal(Fixed3(deposit), Amount(facts.GetProperty("appliedToBooking")));
        var position = facts.GetProperty("bookingPosition");
        Assert.Equal(Fixed3(booking.Pricing.TotalPrice.Amount), Amount(position.GetProperty("bookingTotal")));
        Assert.Equal(Fixed3(deposit), Amount(position.GetProperty("paidOnlineToDate")));
        // The ONE rule: frozen total less the applied money to date — for a deposit, the frozen balance due.
        Assert.Equal(Fixed3(booking.Pricing.BalanceDue.Amount), Amount(position.GetProperty("balanceAfter")));
        Assert.Equal(BalanceStates.DueAtHandover, position.GetProperty("balanceState").GetString());
        Assert.Equal(JsonValueKind.Null, facts.GetProperty("toBeRefunded").ValueKind);

        Assert.Equal(FinancialDocumentType.PaymentReceipt, draft.Type);
        Assert.Equal(payment.Id, draft.SubjectId);
        Assert.Equal(payment.Id, draft.PaymentId);
        Assert.Equal(deposit + 1.5m, draft.HeadlineAmount.Amount);
        Assert.Equal(FinancialDocumentCause.PaymentCaptured, draft.Cause);
        Assert.Equal(payment.AppliedAt, draft.OccurredAt);
        Assert.Equal(
            "Balance due to the rental office at pickup",
            Line(snapshot, "bookingPosition", "balanceAfter").GetProperty("label").GetProperty("en").GetString());
    }

    [Fact]
    public void A_full_payment_receipt_leaves_nothing_to_pay()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m);

        var facts = Parse(PaymentReceipt(booking, payment).Snapshot).GetProperty("facts");

        var position = facts.GetProperty("bookingPosition");
        Assert.Equal("0.000", Amount(position.GetProperty("balanceAfter")));
        Assert.Equal(BalanceStates.PaidInFull, position.GetProperty("balanceState").GetString());
        Assert.True(facts.GetProperty("feeRefundable").GetBoolean());
    }

    [Fact]
    public void A_capture_that_never_applied_is_being_refunded_whole_and_never_set_against_the_booking()
    {
        var booking = Build.ApprovedBooking();
        var payment = Orphaned(booking, Money.Jod(18m));

        var draft = PaymentReceipt(booking, payment);
        var snapshot = Parse(draft.Snapshot);
        var facts = snapshot.GetProperty("facts");

        Assert.Equal("Orphaned", facts.GetProperty("status").GetString());
        Assert.Equal("0.000", Amount(facts.GetProperty("appliedToBooking")));
        Assert.Equal(JsonValueKind.Null, facts.GetProperty("bookingPosition").ValueKind);
        Assert.Equal("18.000", Amount(facts.GetProperty("toBeRefunded")));
        Assert.Equal(
            "This payment could not be applied to your booking, so all of it is refunded to you.",
            Line(snapshot, "payment", "notApplied").GetProperty("text").GetProperty("en").GetString());
        Assert.DoesNotContain("bookingPosition", SectionKeys(snapshot));
    }

    [Fact]
    public void A_capture_in_another_currency_is_described_in_its_own_currency()
    {
        var booking = Build.ApprovedBooking();
        var payment = Orphaned(booking, Money.Create(50m, "USD"));

        var draft = PaymentReceipt(booking, payment);
        var snapshot = Parse(draft.Snapshot);

        Assert.Equal("USD", snapshot.GetProperty("document").GetProperty("currency").GetProperty("code").GetString());
        Assert.Equal("USD", draft.HeadlineAmount.CurrencyCode);
        Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("facts").GetProperty("bookingPosition").ValueKind);
    }

    [Fact]
    public void A_receipt_reads_the_same_whenever_it_is_issued()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m);

        var now = Parse(PaymentReceipt(booking, payment, Now.AddMinutes(1)).Snapshot);
        var aYearLater = Parse(PaymentReceipt(booking, payment, Now.AddYears(1)).Snapshot);

        Assert.Equal(now.GetProperty("facts").GetRawText(), aYearLater.GetProperty("facts").GetRawText());
    }

    [Fact]
    public void The_same_facts_give_the_same_bytes()
    {
        var (booking, payment) = Build.PaidBooking(fee: 1.5m);

        var first = PaymentReceipt(booking, payment);
        var second = PaymentReceipt(booking, payment);

        Assert.Equal(first.Snapshot, second.Snapshot);
        Assert.Equal(FinancialDocument.Sha256(first.Snapshot), FinancialDocument.Sha256(second.Snapshot));
    }

    [Fact]
    public void Every_document_is_worded_in_both_languages_and_says_it_is_not_a_tax_invoice()
    {
        var (booking, payment) = Build.PaidBooking(fee: 1.5m);

        var snapshot = Parse(PaymentReceipt(booking, payment).Snapshot);
        var content = snapshot.GetProperty("content");

        Assert.Equal("Payment receipt", content.GetProperty("title").GetProperty("en").GetString());
        Assert.Equal("إيصال دفع", content.GetProperty("title").GetProperty("ar").GetString());
        Assert.Equal("This document is not a tax invoice.", content.GetProperty("notice").GetProperty("en").GetString());
        Assert.Equal("هذا المستند ليس فاتورة ضريبية.", content.GetProperty("notice").GetProperty("ar").GetString());
        foreach (var section in content.GetProperty("sections").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(section.GetProperty("heading").GetProperty("ar").GetString()));
            foreach (var line in section.GetProperty("lines").EnumerateArray())
            {
                if (line.GetProperty("label").ValueKind == JsonValueKind.Object)
                {
                    Assert.False(string.IsNullOrWhiteSpace(line.GetProperty("label").GetProperty("en").GetString()));
                    Assert.False(string.IsNullOrWhiteSpace(line.GetProperty("label").GetProperty("ar").GetString()));
                }
            }
        }

        // Latin digits only, in both languages (the platform's rule), and never called a tax invoice.
        Assert.DoesNotContain(snapshot.GetRawText(), character => character is >= '٠' and <= '٩');
        Assert.DoesNotContain("Tax invoice", snapshot.GetRawText().Replace("not a tax invoice", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_customer_is_named_by_name_alone()
    {
        var (booking, payment) = Build.PaidBooking();

        var customer = Parse(PaymentReceipt(booking, payment).Snapshot).GetProperty("customer");

        Assert.Equal(["customerId", "name"], customer.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("Rana Sharif", customer.GetProperty("name").GetString());
    }

    [Fact]
    public void The_parties_are_the_bookings_own()
    {
        var (booking, payment) = Build.PaidBooking();
        var strangers = PartiesOf(Build.Booking());

        Assert.Throws<InvalidOperationException>(() => Composer.PaymentReceipt(
            new PaymentReceiptFacts(Issuer, strangers, booking, payment, [payment]),
            DocumentStamp.First("PAY-2026-000001", Now)));
    }

    [Fact]
    public void The_balance_after_counts_every_payment_applied_up_to_this_one_in_order()
    {
        // Two applied payments on one booking — the deposit, then the rest — as a later "remaining balance"
        // payment would make them. Each receipt counts the money applied at or before its own.
        var booking = Build.ApprovedBooking();
        var deposit = Applied(booking, booking.Pricing.DepositAmount, PaymentPurpose.Deposit, Now);
        var rest = Applied(booking, booking.Pricing.BalanceDue, PaymentPurpose.RemainingBalance, Now.AddDays(1));
        IReadOnlyList<Payment> both = [rest, deposit];

        var first = Parse(Composer.PaymentReceipt(new PaymentReceiptFacts(Issuer, PartiesOf(booking), booking, deposit, both), DocumentStamp.First("PAY-2026-000001", Now)).Snapshot);
        var second = Parse(Composer.PaymentReceipt(new PaymentReceiptFacts(Issuer, PartiesOf(booking), booking, rest, both), DocumentStamp.First("PAY-2026-000002", Now.AddDays(1))).Snapshot);

        var firstPosition = first.GetProperty("facts").GetProperty("bookingPosition");
        Assert.Equal(Fixed3(booking.Pricing.DepositAmount.Amount), Amount(firstPosition.GetProperty("paidOnlineToDate")));
        Assert.Equal(BalanceStates.DueAtHandover, firstPosition.GetProperty("balanceState").GetString());
        var secondPosition = second.GetProperty("facts").GetProperty("bookingPosition");
        Assert.Equal(Fixed3(booking.Pricing.TotalPrice.Amount), Amount(secondPosition.GetProperty("paidOnlineToDate")));
        Assert.Equal("0.000", Amount(secondPosition.GetProperty("balanceAfter")));
        Assert.Equal(BalanceStates.PaidInFull, secondPosition.GetProperty("balanceState").GetString());
    }

    [Fact]
    public void A_captures_time_reads_the_same_on_its_receipt_its_statement_and_the_live_page()
    {
        // Captured by the provider at one instant, recorded (and not applied) thirty seconds later: every
        // reader states the instant it was recorded.
        var booking = Build.ApprovedBooking();
        var payment = Payment.Open(booking.Id, booking.CustomerId, Money.Jod(18m), "TestProvider", Now.AddMinutes(30), Now, PaymentPurpose.Deposit, Money.Jod(0m), true);
        Assert.True(payment.AttachProviderSession("sess_late", "https://provider.test/checkout").IsSuccess);
        Assert.True(payment.Orphan(Money.Jod(18m), Now, "booking.not_awaiting_payment", Now.AddSeconds(30)).IsSuccess);
        var financials = BookingFinancialsCalculator.Calculate(booking, [payment], [], false, Now.AddMinutes(1));

        var receipt = Parse(PaymentReceipt(booking, payment).Snapshot);
        var statement = Parse(Composer.Statement(
            new StatementFacts(Issuer, PartiesOf(booking), booking, financials, StatementCheckpoints.Of(booking, [payment], []), false, []),
            DocumentStamp.First("STM-2026-000001", Now.AddMinutes(1)),
            payment.Provider).Snapshot);

        var recorded = payment.OrphanedAt!.Value;
        Assert.Equal(recorded, Assert.Single(financials.Payments).OccurredAt);
        Assert.Equal(
            receipt.GetProperty("facts").GetProperty("capturedAt").GetProperty("utc").GetString(),
            statement.GetProperty("facts").GetProperty("payments")[0].GetProperty("occurredAt").GetProperty("utc").GetString());
        Assert.Equal(
            receipt.GetProperty("facts").GetProperty("capturedAt").GetRawText(),
            receipt.GetProperty("document").GetProperty("occurredAt").GetRawText());
    }

    [Fact]
    public void Every_amount_is_written_at_the_platforms_scale_and_says_so()
    {
        var booking = Build.ApprovedBooking();
        var payment = Orphaned(booking, Money.Create(50m, "USD"));

        var document = Parse(PaymentReceipt(booking, payment).Snapshot).GetProperty("document");

        Assert.Equal(3, document.GetProperty("amountScale").GetInt32());
        Assert.Equal(["code"], document.GetProperty("currency").EnumerateObject().Select(property => property.Name).ToArray());
    }

    // ── Refund receipts ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_dispute_refunds_receipt_says_when_the_dispute_was_decided()
    {
        var (booking, payment, ticket) = DisputedBooking();
        var share = payment.Refunds.Single(refund => refund.Reason == RefundReason.DisputeResolution);
        Settle(share, Now.AddHours(7));

        var snapshot = Parse(Composer.RefundReceipt(
            new RefundReceiptFacts(Issuer, PartiesOf(booking), booking, payment, share, new DocumentReference(Id.New(), "PAY-2026-000001"), ticket.ClosedAt),
            DocumentStamp.First("RFD-2026-000002", Now.AddHours(8))).Snapshot);

        Assert.Equal(
            DocumentFixtures.Amman.DayOf(ticket.ClosedAt!.Value).ToString("yyyy'-'MM'-'dd", System.Globalization.CultureInfo.InvariantCulture),
            snapshot.GetProperty("facts").GetProperty("disputeDecidedAt").GetProperty("local").GetString()![..10]);
        Assert.Equal(JsonValueKind.Object, Line(snapshot, "refund", "disputeDecidedAt").GetProperty("instant").ValueKind);
        Assert.Equal(RefundReason.DisputeResolution.Name, snapshot.GetProperty("facts").GetProperty("reason").GetString());
    }

    [Fact]
    public void A_refund_receipt_carries_the_stored_split_and_belongs_to_the_payments_receipt()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now).IsSuccess);
        var refund = BookingEndingRefunds.Record(booking, payment, Now)!;
        Settle(refund, Now.AddMinutes(2));
        var receipt = new DocumentReference(Id.New(), "PAY-2026-000007");

        var draft = Composer.RefundReceipt(
            new RefundReceiptFacts(Issuer, PartiesOf(booking), booking, payment, refund, receipt, null),
            DocumentStamp.First("RFD-2026-000001", Now.AddMinutes(3)));
        var facts = Parse(draft.Snapshot).GetProperty("facts");

        Assert.Equal(RefundReason.FreeCancellation.Name, facts.GetProperty("reason").GetString());
        Assert.Equal(Fixed3(refund.Amount.Amount), Amount(facts.GetProperty("amount")));
        Assert.Equal(Fixed3(refund.BookingPart.Amount), Amount(facts.GetProperty("bookingPart")));
        Assert.Equal("4.500", Amount(facts.GetProperty("feePart")));
        Assert.Equal(Fixed3(refund.Amount.Amount), Amount(facts.GetProperty("refundedToDateOnPayment")));
        Assert.Equal("PAY-2026-000007", facts.GetProperty("payment").GetProperty("receipt").GetProperty("number").GetString());
        Assert.Equal(receipt.DocumentId, draft.RelatedDocumentId);
        Assert.Equal(refund.Id, draft.SubjectId);
        Assert.Equal(refund.Amount, draft.HeadlineAmount);
        Assert.Equal(refund.SettledAt, draft.OccurredAt);
        Assert.Equal(FinancialDocumentCause.RefundSettled, draft.Cause);
    }

    [Fact]
    public void A_refund_that_has_not_settled_gets_no_receipt()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now).IsSuccess);
        var refund = BookingEndingRefunds.Record(booking, payment, Now)!;

        Assert.Throws<InvalidOperationException>(() => Composer.RefundReceipt(
            new RefundReceiptFacts(Issuer, PartiesOf(booking), booking, payment, refund, new DocumentReference(Id.New(), "PAY-2026-000001"), null),
            DocumentStamp.First("RFD-2026-000001", Now)));
    }

    // ── Statements ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_statement_is_the_customers_projection_and_carries_nothing_wider()
    {
        // The KH-NY8AHLNK shape: paid in full with a fee, cancelled late, the ending's refund settled,
        // a dispute splitting the deposit 13 to the customer and 5 to the office.
        var (booking, payment, ticket) = DisputedBooking();
        var financials = BookingFinancialsCalculator.Calculate(booking, [payment], [ticket], false, Now.AddHours(7));
        Assert.False(financials.NeedsReview, string.Join(", ", financials.Issues));

        var draft = Statement(booking, payment, ticket, financials);
        var snapshot = Parse(draft.Snapshot);
        var names = PropertyNames(snapshot);

        // Plan §3.10: no leak can be retracted from an append-only record.
        foreach (var forbidden in new[]
                 {
                     "commission", "toOffice", "keptByPlatform", "chargedToOffice", "providerReference", "failureCode",
                     "orphanReason", "isSandbox", "issues", "needsReview", "createdAt", "ticketIds",
                 })
        {
            Assert.DoesNotContain(forbidden, names);
        }

        var text = snapshot.GetRawText();
        Assert.DoesNotContain(payment.Provider, text, StringComparison.Ordinal);
        Assert.DoesNotContain("sess_", text, StringComparison.Ordinal);
        Assert.DoesNotContain(PaymentProviders.Sandbox, text, StringComparison.Ordinal);

        var decision = snapshot.GetProperty("facts").GetProperty("deposit").GetProperty("decision");
        Assert.Equal("13.000", Amount(decision.GetProperty("toCustomer")));
        Assert.Equal(["decidedAt", "toCustomer", "refundStatus"], decision.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public void A_statement_totals_what_was_charged_refunded_and_is_on_its_way()
    {
        var (booking, payment, ticket) = DisputedBooking();
        var financials = BookingFinancialsCalculator.Calculate(booking, [payment], [ticket], false, Now.AddHours(7));

        var draft = Statement(booking, payment, ticket, financials);
        var summary = Parse(draft.Snapshot).GetProperty("facts").GetProperty("summary");

        Assert.Equal("94.500", Amount(summary.GetProperty("chargedOnline")));
        Assert.Equal("76.500", Amount(summary.GetProperty("refunded")));
        Assert.Equal("13.000", Amount(summary.GetProperty("refundInProgress")));
        // Net paid online: charged less what is back — a figure of the server's, frozen, never a client's sum.
        Assert.Equal("18.000", Amount(summary.GetProperty("netPaidOnline")));
        Assert.Equal(18m, draft.HeadlineAmount.Amount);
    }

    [Fact]
    public void A_statement_covers_its_checkpoints_and_is_caused_by_the_newest()
    {
        var (booking, payment, ticket) = DisputedBooking();
        var financials = BookingFinancialsCalculator.Calculate(booking, [payment], [ticket], false, Now.AddHours(7));
        var checkpoints = StatementCheckpoints.Of(booking, [payment], [ticket]);

        var draft = Statement(booking, payment, ticket, financials);

        Assert.Equal(checkpoints.CoversThrough, draft.CoversThrough);
        Assert.Equal(checkpoints.Fingerprint, draft.CheckpointFingerprint);
        Assert.Equal(FinancialDocumentCause.DisputeResolved, draft.Cause);
        Assert.Equal(booking.Id, draft.SubjectId);
    }

    [Fact]
    public void A_penalty_against_the_office_is_not_on_the_customers_statement()
    {
        // The office cancels after the free window: the penalty is the office's, a charge the customer's
        // statement never shows (decision 3; plan §3.10).
        var (booking, payment) = Build.PaidBooking(inFull: true);
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "Car unavailable.", booking.FreeCancellationDeadline!.Value.AddMinutes(1)).IsSuccess);
        Assert.Equal(BookingParty.Dealer, booking.Penalty!.AttributedTo);
        BookingEndingRefunds.Record(booking, payment, Now);
        var financials = BookingFinancialsCalculator.Calculate(booking, [payment], [], false, Now.AddHours(1));
        Assert.False(financials.NeedsReview, string.Join(", ", financials.Issues));

        var snapshot = Parse(Composer.Statement(
            new StatementFacts(Issuer, PartiesOf(booking), booking, financials, StatementCheckpoints.Of(booking, [payment], []), false, []),
            DocumentStamp.First("STM-2026-000001", Now.AddHours(1)),
            payment.Provider).Snapshot);

        Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("facts").GetProperty("penalty").ValueKind);
        Assert.DoesNotContain("penalty", SectionKeys(snapshot));
    }

    [Fact]
    public void A_statement_never_freezes_records_that_contradict_one_another()
    {
        // Confirmed on a payment id no payment carries: ConfirmingPaymentMissing.
        var booking = Build.ConfirmedBooking();
        var stray = Orphaned(booking, Money.Jod(18m));
        var financials = BookingFinancialsCalculator.Calculate(booking, [stray], [], false, Now);
        Assert.True(financials.NeedsReview);

        Assert.Throws<InvalidOperationException>(() => Composer.Statement(
            new StatementFacts(Issuer, PartiesOf(booking), booking, financials, StatementCheckpoints.Of(booking, [stray], []), false, []),
            DocumentStamp.First("STM-2026-000001", Now),
            stray.Provider));
    }

    [Fact]
    public void Arabic_sentences_isolate_every_left_to_right_run()
    {
        var (booking, payment, ticket) = DisputedBooking();
        var financials = BookingFinancialsCalculator.Calculate(booking, [payment], [ticket], false, Now.AddHours(7));

        var snapshot = Parse(Statement(booking, payment, ticket, financials).Snapshot);
        var sentence = Line(snapshot, "deposit", "state").GetProperty("text").GetProperty("ar").GetString()!;

        Assert.Contains("⁨13.000 JOD⁩", sentence, StringComparison.Ordinal);
        Assert.Contains("⁨18.000 JOD⁩", sentence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "1 day", "يوم واحد")]
    [InlineData(2, "2 days", "يومان")]
    [InlineData(3, "3 days", "3 أيام")]
    [InlineData(10, "10 days", "10 أيام")]
    [InlineData(11, "11 days", "11 يومًا")]
    [InlineData(100, "100 days", "100 يوم")]
    public void Days_are_counted_in_each_languages_own_plural(int days, string en, string ar)
    {
        var text = DocumentWording.Days(days);

        Assert.Equal(en, text.En);
        Assert.Equal(ar, text.Ar);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────

    private static FinancialDocumentDraft PaymentReceipt(Booking booking, Payment payment, DateTimeOffset? issuedAt = null) =>
        Composer.PaymentReceipt(
            new PaymentReceiptFacts(Issuer, PartiesOf(booking), booking, payment, [payment]),
            DocumentStamp.First("PAY-2026-000001", issuedAt ?? Now.AddMinutes(1)));

    private static FinancialDocumentDraft Statement(Booking booking, Payment payment, DisputeTicket ticket, BookingFinancials financials) =>
        Composer.Statement(
            new StatementFacts(
                Issuer,
                PartiesOf(booking),
                booking,
                financials,
                StatementCheckpoints.Of(booking, [payment], [ticket]),
                PenaltyResolvedByDispute: true,
                [new ReceiptReference(FinancialDocumentType.PaymentReceipt, Id.New(), "PAY-2026-000001")]),
            DocumentStamp.First("STM-2026-000001", Now.AddHours(7)),
            payment.Provider);

    private static (Booking Booking, Payment Payment, DisputeTicket Ticket) DisputedBooking()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, booking.FreeCancellationDeadline!.Value.AddMinutes(1)).IsSuccess);
        Settle(BookingEndingRefunds.Record(booking, payment, Now)!, Now.AddMinutes(5));
        var ticket = DisputeTicket.Open(booking.Id, booking.CustomerId, BookingParty.Customer, "Charged twice.", TimeSpan.FromHours(48), Now.AddHours(5)).Value;
        Assert.True(ticket.Resolve(DisputeResolution.Create(
            DepositDisposition.Create(Money.Jod(18m), Money.Jod(13m), Money.Jod(0m), Money.Jod(5m)).Value,
            null, null, "Split.", Id.New(), Now.AddHours(6)).Value).IsSuccess);
        Assert.True(payment.RequestRefund(Money.Jod(13m), ticket.Id, Now.AddHours(6)).IsSuccess);
        return (booking, payment, ticket);
    }

    private static Payment Applied(Booking booking, Money part, PaymentPurpose purpose, DateTimeOffset at)
    {
        var payment = Payment.Open(booking.Id, booking.CustomerId, Money.Create(part.Amount, part.CurrencyCode), "TestProvider", at.AddMinutes(30), at, purpose, Money.ZeroIn(part.CurrencyCode), true);
        Assert.True(payment.AttachProviderSession("sess_" + payment.Id.Value.ToString("N"), "https://provider.test/checkout").IsSuccess);
        Assert.True(payment.Apply(Money.Create(part.Amount, part.CurrencyCode), at, at).IsSuccess);
        return payment;
    }

    private static Payment Orphaned(Booking booking, Money captured)
    {
        var payment = Payment.Open(
            booking.Id,
            booking.CustomerId,
            Money.Create(captured.Amount, captured.CurrencyCode),
            "TestProvider",
            Now.AddMinutes(30),
            Now,
            PaymentPurpose.Deposit,
            Money.ZeroIn(captured.CurrencyCode),
            true);
        Assert.True(payment.AttachProviderSession("sess_" + payment.Id.Value.ToString("N"), "https://provider.test/checkout").IsSuccess);
        Assert.True(payment.Orphan(captured, Now, "booking.not_awaiting_payment", Now).IsSuccess);
        return payment;
    }

    private static void Settle(Refund refund, DateTimeOffset at)
    {
        refund.MarkSent("rf_" + refund.Id.Value.ToString("N"), at);
        refund.MarkSettled(at);
    }

    private static string Fixed3(decimal amount) =>
        amount.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
}
