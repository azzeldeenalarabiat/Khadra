using Khadra.Application.Payables.ReadModels;
using Khadra.Application.Payments;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Payables;
using Khadra.Domain.Payments;
using Khadra.Tests.Support;

namespace Khadra.Tests.Application.Payments;

/// <summary>
/// What a final booking comes to for its rental office (payments Phase 8; owner, 2026-09-24 and 2026-09-29): the one
/// calculator's office position, which the payables ledger records and checks again. One test per outcome, and the
/// owner's rules for each — the office's money, the commission capped at it, every dispute charge taken from it.
/// </summary>
/// <remarks>
/// The default booking is 3 days at 30 JOD: 90 for the booking, an 18 deposit (20%), a 6 commission (20% of one day).
/// Paid in full, 72 of the 90 is above the deposit.
/// </remarks>
public sealed class OfficePositionTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    private static BookingFinancials Calculate(
        Booking booking,
        IEnumerable<Payment> payments,
        DateTimeOffset now,
        IEnumerable<DisputeTicket>? resolved = null,
        bool live = false,
        RecordedPayable? recorded = null) =>
        BookingFinancialsCalculator.Calculate(booking, payments, resolved ?? [], live, now, recorded);

    private static DateTimeOffset AfterFreeWindow(Booking booking) => booking.FreeCancellationDeadline!.Value.AddMinutes(1);

    private static DisputeTicket Resolved(Booking booking, decimal refund, decimal platform, decimal dealer, DateTimeOffset at, decimal? dealerCharge = null)
    {
        var ticket = DisputeTicket.Open(booking.Id, booking.CustomerId, BookingParty.Customer, "Something went wrong.", TimeSpan.FromHours(48), at).Value;
        var basis = refund + platform + dealer;
        Assert.True(ticket.Resolve(DisputeResolution.Create(
            DepositDisposition.Create(Money.Jod(basis), Money.Jod(refund), Money.Jod(platform), Money.Jod(dealer)).Value,
            dealerCharge is { } charge ? Money.Jod(charge) : null,
            dealerCharge is null ? null : booking.Penalty,
            "Decided.",
            Id.New(),
            at.AddHours(1)).Value).IsSuccess);
        return ticket;
    }

    private static (Booking Booking, Payment Payment) Completed(bool inFull = false)
    {
        var (booking, payment) = Build.PaidBooking(inFull: inFull);
        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start).IsSuccess);
        Assert.True(booking.RecordReturn(BookingParty.Dealer, Id.New(), booking.Period.End).IsSuccess);
        Assert.True(booking.Settle(booking.DisputeWindowEndsAt!.Value, hasOpenDispute: false).IsSuccess);
        return (booking, payment);
    }

    private static void AssertLines(OfficePosition office, params (PayableLineKind Kind, decimal Amount)[] expected) =>
        Assert.Equal(expected, office.Lines.Select(line => (line.Kind, line.Amount)).ToArray());

    // ── Not final ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Nothing_paid_never_reaches_the_office()
    {
        var booking = Build.Booking();

        var office = Calculate(booking, [], Now).Office;

        Assert.Equal(OfficeStates.NotApplicable, office.State);
        Assert.Null(office.Outcome);
        Assert.Empty(office.Lines);
    }

    [Fact]
    public void A_booking_still_running_or_inside_its_window_is_open()
    {
        var (confirmed, confirmedPayment) = Build.PaidBooking();
        var (returned, returnedPayment) = Build.PaidBooking();
        returned.RecordPickup(BookingParty.Dealer, Id.New(), returned.Period.Start);
        returned.RecordReturn(BookingParty.Dealer, Id.New(), returned.Period.End);
        var (cancelled, cancelledPayment) = Build.PaidBooking();
        Assert.True(cancelled.Cancel(BookingParty.Customer, cancelled.CustomerId, null, AfterFreeWindow(cancelled)).IsSuccess);

        Assert.Equal(OfficeStates.Open, Calculate(confirmed, [confirmedPayment], Now).Office.State);
        // Returned: the window has passed but the booking has not completed — the sweep completes it first.
        Assert.Equal(OfficeStates.Open, Calculate(returned, [returnedPayment], returned.DisputeWindowEndsAt!.Value.AddDays(1)).Office.State);
        Assert.Equal(OfficeStates.Open, Calculate(cancelled, [cancelledPayment], cancelled.DisputeWindowEndsAt!.Value.AddMinutes(-1)).Office.State);
    }

    [Fact]
    public void A_live_dispute_keeps_the_outcome_open_after_the_window()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);

        var office = Calculate(booking, [payment], booking.DisputeWindowEndsAt!.Value.AddHours(1), live: true).Office;

        Assert.Equal(OfficeStates.Open, office.State);
    }

    // ── Rentals ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_rental_that_completed_cleanly_is_everything_paid_online_less_the_commission()
    {
        var (deposit, depositPayment) = Completed();
        var (full, fullPayment) = Completed(inFull: true);

        var fromDeposit = Calculate(deposit, [depositPayment], deposit.FinishedAt!.Value).Office;
        var fromFull = Calculate(full, [fullPayment], full.FinishedAt!.Value).Office;

        Assert.Equal(OfficeStates.Final, fromDeposit.State);
        Assert.Same(PayableOutcome.Rental, fromDeposit.Outcome);
        Assert.Equal(deposit.FinishedAt, fromDeposit.FinalAt);
        AssertLines(fromDeposit, (PayableLineKind.RentalRevenue, 18m), (PayableLineKind.Commission, 6m));
        Assert.Equal(12m, fromDeposit.Net);
        // Paid in full: the whole booking online is the office's — the balance never passed through the office's hands.
        AssertLines(fromFull, (PayableLineKind.RentalRevenue, 90m), (PayableLineKind.Commission, 6m));
        Assert.Equal(Money.Jod(90m), fromFull.OfficeMoney);
        Assert.Equal(84m, fromFull.Net);
    }

    [Fact]
    public void A_rental_decided_by_a_dispute_is_what_was_paid_above_the_deposit_and_the_offices_share()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true);
        booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start);
        booking.RecordReturn(BookingParty.Dealer, Id.New(), booking.Period.End);
        var ticket = Resolved(booking, refund: 9m, platform: 2m, dealer: 7m, booking.Period.End.AddHours(1));
        Assert.True(payment.RequestRefund(Money.Jod(9m), ticket.Id, booking.Period.End.AddHours(2)).IsSuccess);
        Assert.True(booking.CloseAfterDisputeResolved(Id.New(), booking.Period.End.AddHours(2)).IsSuccess);

        var financials = Calculate(booking, [payment], booking.FinishedAt!.Value, [ticket]);

        Assert.Empty(financials.Issues);
        Assert.Same(PayableOutcome.RentalAfterDispute, financials.Office.Outcome);
        // 72 above the deposit, the office's 7 of it; the customer's 9 and the platform's 2 are nobody's here.
        AssertLines(financials.Office, (PayableLineKind.RentalRevenue, 72m), (PayableLineKind.DisputeShare, 7m), (PayableLineKind.Commission, 6m));
        Assert.Equal(ticket.Id, financials.Office.Lines[1].SourceId);
        Assert.Equal(73m, financials.Office.Net);
    }

    [Fact]
    public void The_commission_on_a_rental_decided_by_a_dispute_is_never_more_than_the_offices_money()
    {
        var (booking, payment) = Build.PaidBooking();
        booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start);
        booking.RecordReturn(BookingParty.Dealer, Id.New(), booking.Period.End);
        var ticket = Resolved(booking, refund: 14m, platform: 0m, dealer: 4m, booking.Period.End.AddHours(1));
        Assert.True(payment.RequestRefund(Money.Jod(14m), ticket.Id, booking.Period.End.AddHours(2)).IsSuccess);
        Assert.True(booking.CloseAfterDisputeResolved(Id.New(), booking.Period.End.AddHours(2)).IsSuccess);

        var office = Calculate(booking, [payment], booking.FinishedAt!.Value, [ticket]).Office;

        // Owner, 2026-09-29: earned, capped at the office's money — 4 here, not the frozen 6.
        AssertLines(office, (PayableLineKind.DisputeShare, 4m), (PayableLineKind.Commission, 4m));
        Assert.Equal(Money.Jod(4m), office.Commission);
        Assert.Equal(0m, office.Net);
    }

    // ── Endings before pickup ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_customers_penalty_is_kept_from_the_deposit_when_the_window_closes_with_no_dispute()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);
        var aboveDeposit = BookingEndingRefunds.Record(booking, payment, AfterFreeWindow(booking))!;

        var financials = Calculate(booking, [payment], booking.DisputeWindowEndsAt!.Value);

        Assert.Same(RefundReason.EndedBeforePickup, aboveDeposit.Reason);
        Assert.Equal(OfficeStates.Final, financials.Office.State);
        Assert.Same(PayableOutcome.PenaltyKept, financials.Office.Outcome);
        Assert.Equal(booking.DisputeWindowEndsAt, financials.Office.FinalAt);
        // The 72 above the deposit went back to the customer; the 18 deposit is the office's, less 6.
        AssertLines(financials.Office, (PayableLineKind.PenaltyKept, 18m), (PayableLineKind.Commission, 6m));
        Assert.Equal(12m, financials.Office.Net);
    }

    [Fact]
    public void A_self_pickup_no_show_keeps_the_deposit_the_same_way()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.MarkNoShow(booking.Period.Start.Add(booking.Terms.NoShowTimeout)).IsSuccess);

        var office = Calculate(booking, [payment], booking.DisputeWindowEndsAt!.Value.AddMinutes(1)).Office;

        Assert.Same(PayableOutcome.PenaltyKept, office.Outcome);
        Assert.Equal(12m, office.Net);
    }

    [Fact]
    public void A_penalty_that_is_not_the_whole_deposit_is_final_and_undetermined()
    {
        var (booking, payment) = Build.PaidBooking(terms: Build.Terms(customerPenaltyPercent: 50m));
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);

        var office = Calculate(booking, [payment], booking.DisputeWindowEndsAt!.Value.AddMinutes(1)).Office;

        // Half the deposit would be owed back to the customer, and nothing can send it: never recorded as kept.
        Assert.Equal(OfficeStates.Undetermined, office.State);
        Assert.Equal(PayableHoldReason.PenaltyNotWholeDeposit.Name, office.UndeterminedBecause);
        Assert.Empty(office.Lines);
        Assert.False(office.IsFinal);
    }

    [Fact]
    public void A_deposit_released_or_a_payment_returned_owes_the_office_nothing()
    {
        var (released, releasedPayment) = Build.PaidBooking();
        Assert.True(released.Cancel(BookingParty.Dealer, Id.New(), "No car.", Now.AddHours(3)).IsSuccess);
        var (returned, returnedPayment) = Build.PaidBooking();
        Assert.True(returned.Cancel(BookingParty.Customer, returned.CustomerId, null, Now).IsSuccess);
        BookingEndingRefunds.Record(returned, returnedPayment, Now);

        var fromReleased = Calculate(released, [releasedPayment], released.DisputeWindowEndsAt!.Value).Office;
        var fromReturned = Calculate(returned, [returnedPayment], returned.DisputeWindowEndsAt!.Value).Office;

        Assert.Same(PayableOutcome.DepositReleased, fromReleased.Outcome);
        Assert.Same(PayableOutcome.PaymentReturned, fromReturned.Outcome);
        Assert.All([fromReleased, fromReturned], office =>
        {
            Assert.Equal(OfficeStates.Final, office.State);
            Assert.Empty(office.Lines);
            Assert.Equal(0m, office.Net);
        });
    }

    [Fact]
    public void An_ending_a_dispute_decided_charges_the_office_and_can_leave_it_owing()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", AfterFreeWindow(booking)).IsSuccess);
        var charge = booking.Penalty!.MaxAmount.Amount;
        var ticket = Resolved(booking, refund: 18m, platform: 0m, dealer: 0m, AfterFreeWindow(booking).AddHours(1), dealerCharge: charge);
        Assert.True(payment.RequestRefund(Money.Jod(18m), ticket.Id, AfterFreeWindow(booking).AddHours(2)).IsSuccess);

        var office = Calculate(booking, [payment], booking.DisputeWindowEndsAt!.Value, [ticket]).Office;

        Assert.Same(PayableOutcome.DisputeDecided, office.Outcome);
        // No money of the office's on the booking, so no commission; its charge netted, so it owes (owner, 2026-09-29).
        AssertLines(office, (PayableLineKind.DisputeCharge, charge));
        Assert.Equal(ticket.Id, office.Lines[0].SourceId);
        Assert.Equal(-charge, office.Net);
    }

    [Fact]
    public void An_ending_a_dispute_gave_the_office_a_share_of_earns_the_commission_capped_at_it()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);
        var ticket = Resolved(booking, refund: 10m, platform: 0m, dealer: 8m, AfterFreeWindow(booking).AddHours(1));
        Assert.True(payment.RequestRefund(Money.Jod(10m), ticket.Id, AfterFreeWindow(booking).AddHours(2)).IsSuccess);

        var office = Calculate(booking, [payment], booking.DisputeWindowEndsAt!.Value, [ticket]).Office;

        // Interpretation I (to the owner): every final booking's commission is the frozen figure capped at the office's money.
        Assert.Same(PayableOutcome.DisputeDecided, office.Outcome);
        AssertLines(office, (PayableLineKind.DisputeShare, 8m), (PayableLineKind.Commission, 6m));
        Assert.Equal(2m, office.Net);
    }

    // ── What the ledger's record decides ────────────────────────────────────────────────────────────

    [Fact]
    public void A_deposit_is_kept_as_the_penalty_only_once_the_ledger_recorded_it()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);
        var after = booking.DisputeWindowEndsAt!.Value.AddHours(1);

        var unrecorded = Calculate(booking, [payment], after);
        var recorded = Calculate(booking, [payment], after, recorded: new RecordedPayable(PayableOutcome.PenaltyKept, Money.Jod(6m)));

        Assert.Equal(DepositStates.HeldUnresolved, unrecorded.Deposit.State);
        Assert.Equal(CommissionStates.Undecided, unrecorded.Commission.State);
        Assert.Null(unrecorded.Commission.Earned);
        Assert.Equal(DepositStates.KeptAsPenalty, recorded.Deposit.State);
        Assert.Null(recorded.Deposit.WindowEndsAt);
        Assert.Equal(CommissionStates.Earned, recorded.Commission.State);
        Assert.Equal(Money.Jod(6m), recorded.Commission.Earned);
    }

    [Fact]
    public void A_recorded_payable_that_carries_no_commission_earned_none()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", Now.AddHours(3)).IsSuccess);

        var financials = Calculate(
            booking, [payment], booking.DisputeWindowEndsAt!.Value, recorded: new RecordedPayable(PayableOutcome.DepositReleased, Money.ZeroIn("JOD")));

        Assert.Equal(CommissionStates.NotEarned, financials.Commission.State);
        Assert.Null(financials.Commission.Earned);
    }

    [Fact]
    public void A_clean_rental_earns_the_whole_frozen_commission_before_and_after_it_is_recorded()
    {
        var (booking, payment) = Completed();

        var before = Calculate(booking, [payment], booking.FinishedAt!.Value);
        var after = Calculate(booking, [payment], booking.FinishedAt!.Value, recorded: new RecordedPayable(PayableOutcome.Rental, Money.Jod(6m)));

        Assert.Equal(CommissionStates.Earned, before.Commission.State);
        Assert.Equal(Money.Jod(6m), before.Commission.Earned);
        Assert.Equal(before.Commission, after.Commission);
    }

    [Fact]
    public void A_refund_no_dispute_explains_on_a_completed_rental_needs_review()
    {
        var (booking, payment) = Completed();
        Assert.True(payment.RequestRefund(Money.Jod(3m), null, booking.FinishedAt!.Value).IsSuccess);

        var financials = Calculate(booking, [payment], booking.FinishedAt!.Value);

        Assert.Contains(FinancialIssues.RefundWithoutCause, financials.Issues);
        Assert.True(financials.NeedsReview);
    }

    [Fact]
    public void A_deposit_released_beside_a_penalty_the_ledger_would_keep_needs_review()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);
        // The sweep never releases a deposit a customer penalty holds; a row that says it did contradicts the booking.
        Assert.True(payment.RefundHeldDeposit(Money.Jod(18m), booking.DisputeWindowEndsAt!.Value).IsSuccess);

        var financials = Calculate(booking, [payment], booking.DisputeWindowEndsAt.Value);

        Assert.Contains(FinancialIssues.RefundsConflict, financials.Issues);
        Assert.True(financials.NeedsReview);
    }

    [Fact]
    public void A_whole_payment_refund_on_an_ending_that_keeps_the_deposit_needs_review()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);
        Assert.True(payment.RefundWholePayment(RefundReason.FreeCancellation, AfterFreeWindow(booking)).IsSuccess);

        var financials = Calculate(booking, [payment], booking.DisputeWindowEndsAt!.Value);

        Assert.Contains(FinancialIssues.RefundsConflict, financials.Issues);
    }

    [Fact]
    public void What_the_booking_says_was_paid_online_must_be_what_its_payment_applied()
    {
        var booking = Build.ApprovedBooking();
        var payment = Payment.Open(booking.Id, booking.CustomerId, Money.Jod(90m), "TestProvider", Now.AddMinutes(30), Now, PaymentPurpose.FullPayment, Money.Jod(0m), true);
        Assert.True(payment.AttachProviderSession("sess_x", "https://provider.test/checkout").IsSuccess);
        Assert.True(payment.Apply(Money.Jod(90m), Now, Now).IsSuccess);
        // The payment applied 90; the booking was told 18.
        Assert.True(booking.ConfirmPayment(payment.Id, Money.Jod(18m), Now).IsSuccess);

        var financials = Calculate(booking, [payment], Now);

        Assert.Contains(FinancialIssues.PaidOnlineDisagrees, financials.Issues);
    }

    [Fact]
    public void The_rules_that_computed_an_answer_are_version_2()
    {
        Assert.Equal(2, BookingFinancials.CalculatorVersion);
    }

    // ── The office block, by reader ─────────────────────────────────────────────────────────────────

    [Fact]
    public void The_office_block_is_the_calculators_until_recorded_and_never_the_customers()
    {
        var (booking, payment) = Completed(inFull: true);
        var financials = Calculate(booking, [payment], booking.FinishedAt!.Value);
        var nothingRecorded = new BookingLedger(null, [], []);

        var customer = BookingFinancialsDto.For(financials, BookingParty.Customer, nothingRecorded);
        var office = BookingFinancialsDto.For(financials, BookingParty.Dealer, nothingRecorded);
        var admin = BookingFinancialsDto.For(financials, BookingParty.Admin, nothingRecorded);

        Assert.Null(customer.Office);
        Assert.Equal(FinancialOfficeStates.AwaitingRecord, office.Office!.State);
        Assert.Equal("Rental", office.Office.Outcome);
        Assert.Equal(84m, office.Office.Net!.Amount);
        Assert.Equal(["RentalRevenue", "Commission"], office.Office.Lines.Select(line => line.Kind));
        // The ledger's own workings are the administrator's alone.
        Assert.Null(office.Office.Holds);
        Assert.Null(office.Office.Blocks);
        Assert.NotNull(admin.Office!.Holds);
        Assert.Equal(Money.Jod(6m).Amount, admin.Commission!.Earned!.Amount);
    }

    [Fact]
    public void Once_recorded_the_office_block_is_the_ledgers_frozen_figures()
    {
        var (booking, payment) = Completed();
        var financials = Calculate(booking, [payment], booking.FinishedAt!.Value);
        var payableId = Id.New();
        var ledger = new BookingLedger(
            new LedgerPayable(
                payableId, booking.Id, booking.Reference.Value, booking.DealerId, "Petra Wheels", PayableOutcome.Rental, "JOD", false,
                18m, 6m, 0m, 12m, booking.FinishedAt.Value, booking.FinishedAt.Value.AddMinutes(11), 2, PayableStates.Due,
                [new LedgerLine(PayableLineKind.RentalRevenue, 18m, null), new LedgerLine(PayableLineKind.Commission, 6m, null)],
                null, [], []),
            [],
            []);

        var admin = BookingFinancialsDto.For(financials, BookingParty.Admin, ledger).Office!;
        var office = BookingFinancialsDto.For(financials, BookingParty.Dealer, ledger).Office!;

        Assert.Equal(PayableStates.Due, admin.State);
        Assert.Equal(payableId.Value, admin.PayableId);
        Assert.Equal(booking.FinishedAt.Value.AddMinutes(11), admin.RecordedAt);
        Assert.Null(office.PayableId);
        Assert.Equal(12m, office.Net!.Amount);
    }
}
