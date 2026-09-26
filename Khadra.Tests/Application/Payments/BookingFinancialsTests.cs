using System.Text.Json;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Payments;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Payments;
using Khadra.Tests.Support;

namespace Khadra.Tests.Application.Payments;

/// <summary>
/// The booking's financial state (payments Phase 4, owner 2026-09-26): one calculator, three readers.
/// Every scenario is also checked for reconciliation — the calculator's figures against each other and
/// against the totals the booking response already serves — so a rule that moves shows up here.
/// </summary>
/// <remarks>
/// The default booking is 3 days at 30 JOD: 90 for the booking, an 18 deposit (20%), a 6 commission
/// (20% of one day). "Paid in full" with a 4.5 fee charged the card 94.5.
/// </remarks>
public sealed class BookingFinancialsTests
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static DateTimeOffset AfterFreeWindow(Booking booking) => booking.FreeCancellationDeadline!.Value.AddMinutes(1);

    private static BookingFinancials Calculate(
        Booking booking,
        IEnumerable<Payment>? payments = null,
        IEnumerable<DisputeTicket>? resolved = null,
        bool live = false,
        DateTimeOffset? now = null)
    {
        var paymentList = (payments ?? []).ToList();
        var financials = BookingFinancialsCalculator.Calculate(booking, paymentList, resolved ?? [], live, now ?? Now);
        AssertReconciles(booking, paymentList, financials);
        return financials;
    }

    private static void Settle(Refund refund)
    {
        refund.MarkSent("rf_" + refund.Id.Value.ToString("N"), Now);
        refund.MarkSettled(Now);
    }

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

    /// <summary>
    /// The identities that hold for every consistent booking — and the booking response's own totals,
    /// which the calculator must equal until they are retired (the advisor's condition for leaving
    /// <c>BookingDto.From</c> untouched).
    /// </summary>
    private static void AssertReconciles(Booking booking, List<Payment> payments, BookingFinancials financials)
    {
        foreach (var payment in financials.Payments)
        {
            if (payment.Status == PaymentStatus.Applied)
                Assert.Equal(payment.AmountCharged, payment.AppliedToBooking.Add(payment.ProcessingFee));
            if (payment.Status == PaymentStatus.Orphaned)
                Assert.Equal(payment.AmountCharged.Amount, payment.Refunds.Sum(refund => refund.Amount.Amount));
            foreach (var refund in payment.Refunds)
                Assert.Equal(refund.Amount, refund.BookingPart.Add(refund.FeePart));
        }

        if (financials.Balance.State == BalanceStates.DueAtHandover)
            Assert.Equal(financials.Summary.BookingTotal, financials.Balance.Amount.Add(financials.Summary.PaidOnline));

        // Everything the card was charged is booking money, a processing fee, or a capture that never
        // applied — counted in the booking's currency only, as every total is.
        var orphanCharges = financials.Payments
            .Where(payment => payment.Status == PaymentStatus.Orphaned &&
                string.Equals(payment.AmountCharged.CurrencyCode, financials.Currency, StringComparison.Ordinal))
            .Sum(payment => payment.AmountCharged.Amount);
        Assert.Equal(
            financials.Summary.ChargedOnline.Amount,
            financials.Summary.PaidOnline.Amount + financials.Summary.ProcessingFees.Amount + orphanCharges);

        // The booking response composes the same refunds on its own; its totals must match.
        var refundDtos = payments
            .SelectMany(payment => payment.Refunds.Select(refund => new RefundDto(
                refund.Id.Value, payment.Id.Value, refund.Reason.Name, MoneyDto.From(refund.Amount), refund.Status.Name,
                refund.RequestedAt, refund.SentAt, refund.SettledAt, refund.FailedAt, refund.DisputeTicketId?.Value)))
            .ToList();
        var dto = BookingDto.From(
            booking,
            new BookingContext(null, "Office", false, null, "Customer", false, null, null, Refunds: refundDtos),
            Now);
        var summary = financials.Summary;
        Assert.Equal(dto.RefundedAmount!.Amount, summary.Refunds.Settled.Amount);
        Assert.Equal(dto.RefundOutstandingAmount!.Amount, summary.Refunds.InProgress.Amount + summary.Refunds.Delayed.Amount);
        Assert.Equal(dto.OnlinePaid!.Amount, summary.PaidOnline.Amount);
    }

    // ── Before and at payment ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_requested_booking_has_nothing_paid_and_a_projected_commission()
    {
        var booking = Build.Booking();

        var financials = Calculate(booking);

        Assert.Equal(BalanceStates.NotYetDue, financials.Balance.State);
        Assert.Equal(DepositStates.NotPaid, financials.Deposit.State);
        Assert.Equal(CommissionStates.Projected, financials.Commission.State);
        Assert.Equal(Money.Jod(6m), financials.Commission.Amount);
        Assert.Equal(Money.Jod(90m), financials.Summary.BookingTotal);
        Assert.True(financials.Summary.PaidOnline.IsZero);
        Assert.Empty(financials.Payments);
        Assert.False(financials.NeedsReview);
    }

    [Fact]
    public void A_failed_attempt_is_shown_only_to_the_administrator()
    {
        var booking = Build.ApprovedBooking();
        var attempt = Payment.Open(booking.Id, booking.CustomerId, Money.Jod(18m), "TestProvider", Now.AddMinutes(30), Now);
        Assert.True(attempt.AttachProviderSession("sess_failed", "https://provider.test/checkout").IsSuccess);
        Assert.True(attempt.Fail("card_declined", Now.AddMinutes(1)).IsSuccess);

        var financials = Calculate(booking, [attempt]);
        var customer = BookingFinancialsDto.For(financials, BookingParty.Customer);
        var office = BookingFinancialsDto.For(financials, BookingParty.Dealer);
        var admin = BookingFinancialsDto.For(financials, BookingParty.Admin);

        Assert.Empty(customer.Payments);
        Assert.Empty(office.Payments);
        var failed = Assert.Single(admin.Payments);
        Assert.Equal("Failed", failed.Status);
        Assert.Equal("card_declined", failed.FailureCode);
        Assert.True(financials.Summary.ChargedOnline.IsZero);
    }

    [Fact]
    public void A_deposit_confirms_the_booking_and_leaves_the_rest_due_at_handover()
    {
        var (booking, payment) = Build.PaidBooking();

        var financials = Calculate(booking, [payment]);

        Assert.Equal(BalanceStates.DueAtHandover, financials.Balance.State);
        Assert.Equal(Money.Jod(72m), financials.Balance.Amount);
        Assert.Equal(DepositStates.Held, financials.Deposit.State);
        Assert.Equal(Money.Jod(18m), financials.Deposit.Amount);
        Assert.Equal(CommissionStates.Expected, financials.Commission.State);
        var record = Assert.Single(financials.Payments);
        Assert.Same(PaymentPurpose.Deposit, record.Purpose);
        Assert.Equal(RefundProgresses.None, record.RefundProgress);
        Assert.Equal(Money.Jod(18m), financials.Summary.PaidOnline);
        Assert.Equal(Money.Jod(18m), financials.Summary.ChargedOnline);
    }

    [Fact]
    public void A_full_payment_is_paid_in_full_and_its_fee_is_hidden_from_the_office()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m);

        var financials = Calculate(booking, [payment]);
        var customer = BookingFinancialsDto.For(financials, BookingParty.Customer);
        var office = BookingFinancialsDto.For(financials, BookingParty.Dealer);

        Assert.Equal(BalanceStates.PaidInFull, financials.Balance.State);
        Assert.True(financials.Balance.Amount.IsZero);
        Assert.Equal(Money.Jod(4.5m), financials.Summary.ProcessingFees);
        Assert.Equal(Money.Jod(94.5m), financials.Summary.ChargedOnline);
        Assert.Equal(Money.Jod(90m), financials.Summary.PaidOnline);

        Assert.Equal(94.5m, customer.Summary.ChargedOnline!.Amount);
        Assert.Equal(4.5m, Assert.Single(customer.Payments).ProcessingFee!.Amount);
        // Owner, 2026-09-26: rental offices never see processing fees.
        Assert.Null(office.Summary.ProcessingFees);
        Assert.Null(office.Summary.ChargedOnline);
        var officePayment = Assert.Single(office.Payments);
        Assert.Null(officePayment.AmountCharged);
        Assert.Null(officePayment.ProcessingFee);
        Assert.Equal(90m, officePayment.AppliedToBooking.Amount);
    }

    // ── The rental ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void After_pickup_the_balance_was_due_at_handover_and_recorded_cash_is_shown_beside_it()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start, cashCollected: Money.Jod(222m)).IsSuccess);

        var financials = Calculate(booking, [payment]);

        Assert.Equal(BalanceStates.CashAtHandover, financials.Balance.State);
        Assert.Equal(Money.Jod(72m), financials.Balance.Amount);
        // The office's own figure, never compared with the balance: it may include a cash security deposit.
        var cash = Assert.Single(financials.Balance.CashRecorded);
        Assert.Same(HandoverType.Pickup, cash.Handover);
        Assert.Equal(Money.Jod(222m), cash.Amount);
        Assert.Equal(DepositStates.AppliedToRental, financials.Deposit.State);
    }

    [Fact]
    public void A_handover_with_no_cash_recorded_says_so_rather_than_calling_it_paid()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start).IsSuccess);

        var financials = Calculate(booking, [payment]);

        Assert.Equal(BalanceStates.CashAtHandover, financials.Balance.State);
        Assert.Empty(financials.Balance.CashRecorded);
    }

    [Fact]
    public void A_returned_booking_holds_the_deposit_in_its_window_or_under_a_dispute()
    {
        var (booking, payment) = Build.PaidBooking();
        booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start);
        booking.RecordReturn(BookingParty.Dealer, Id.New(), booking.Period.End);

        var waiting = Calculate(booking, [payment]);
        var disputed = Calculate(booking, [payment], live: true);

        Assert.Equal(DepositStates.InSettlementWindow, waiting.Deposit.State);
        Assert.Equal(booking.DisputeWindowEndsAt, waiting.Deposit.WindowEndsAt);
        Assert.Equal(DepositStates.UnderDispute, disputed.Deposit.State);
        Assert.Equal(CommissionStates.Expected, waiting.Commission.State);
    }

    [Fact]
    public void A_completed_booking_settled_the_deposit_with_the_rental_and_earned_the_commission()
    {
        var (booking, payment) = Build.PaidBooking();
        booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start);
        booking.RecordReturn(BookingParty.Dealer, Id.New(), booking.Period.End);
        Assert.True(booking.Settle(booking.DisputeWindowEndsAt!.Value, hasOpenDispute: false).IsSuccess);

        var financials = Calculate(booking, [payment]);

        Assert.Equal(DepositStates.SettledWithRental, financials.Deposit.State);
        Assert.Equal(CommissionStates.Earned, financials.Commission.State);
        Assert.Equal(BalanceStates.CashAtHandover, financials.Balance.State);
    }

    [Fact]
    public void A_booking_completed_through_a_dispute_leaves_the_commission_undecided()
    {
        var (booking, payment) = Build.PaidBooking();
        booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start);
        booking.RecordReturn(BookingParty.Dealer, Id.New(), booking.Period.End);
        var ticket = Resolved(booking, refund: 9m, platform: 0m, dealer: 9m, booking.Period.End.AddHours(1));
        Assert.True(payment.RequestRefund(Money.Jod(9m), ticket.Id, booking.Period.End.AddHours(2)).IsSuccess);
        Assert.True(booking.CloseAfterDisputeResolved(Id.New(), booking.Period.End.AddHours(2)).IsSuccess);

        var financials = Calculate(booking, [payment], [ticket]);

        Assert.Same(BookingStatus.Completed, financials.BookingStatus);
        Assert.Equal(DepositStates.DecidedByDispute, financials.Deposit.State);
        // Owner, 2026-09-26: completed through a dispute resolution — Undecided until Phase 8.
        Assert.Equal(CommissionStates.Undecided, financials.Commission.State);
    }

    // ── Endings before pickup ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_free_cancellation_returns_the_whole_payment_and_earns_nothing()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now).IsSuccess);
        var refund = BookingEndingRefunds.Record(booking, payment, Now)!;

        var owed = Calculate(booking, [payment]);
        Settle(refund);
        var settled = Calculate(booking, [payment]);

        Assert.Equal(BalanceStates.NotDue, owed.Balance.State);
        Assert.Equal(DepositStates.ReturnedWithPayment, owed.Deposit.State);
        Assert.Equal(refund.Id, owed.Deposit.Refund!.RefundId);
        Assert.Equal(CommissionStates.NotEarned, owed.Commission.State);
        Assert.Equal(RefundProgresses.InProgress, Assert.Single(owed.Payments).RefundProgress);
        Assert.Equal(Money.Jod(18m), owed.Summary.Refunds.InProgress);
        Assert.Equal(RefundProgresses.Complete, Assert.Single(settled.Payments).RefundProgress);
        Assert.Equal(Money.Jod(18m), settled.Summary.Refunds.Settled);
    }

    [Fact]
    public void A_late_cancellation_refunds_above_the_deposit_and_holds_the_deposit_for_the_penalty()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);
        var refund = BookingEndingRefunds.Record(booking, payment, Now)!;
        Settle(refund);

        var financials = Calculate(booking, [payment], now: AfterFreeWindow(booking).AddHours(1));
        var office = BookingFinancialsDto.For(financials, BookingParty.Dealer);

        Assert.Equal(BalanceStates.NotDue, financials.Balance.State);
        Assert.Equal(DepositStates.HeldForAssessedPenalty, financials.Deposit.State);
        Assert.Equal(booking.DisputeWindowEndsAt, financials.Deposit.WindowEndsAt);
        // Owner, 2026-09-26: no commission from a deposit held for a customer penalty in Phase 4.
        Assert.Equal(CommissionStates.Undecided, financials.Commission.State);
        var record = Assert.Single(financials.Payments);
        Assert.Equal(RefundProgresses.Partial, record.RefundProgress);
        var line = Assert.Single(record.Refunds);
        Assert.Equal(Money.Jod(76.5m), line.Amount);
        Assert.Equal(Money.Jod(4.5m), line.FeePart);
        // The office reads the booking money only.
        Assert.Equal(72m, Assert.Single(Assert.Single(office.Payments).Refunds).Amount.Amount);
        Assert.Null(Assert.Single(Assert.Single(office.Payments).Refunds).FeePart);
        Assert.Equal(72m, office.Summary.Refunded.Amount);
    }

    [Fact]
    public void Once_the_window_closes_a_deposit_held_for_a_penalty_is_held_unresolved()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);

        var financials = Calculate(booking, [payment], now: booking.DisputeWindowEndsAt!.Value.AddMinutes(1));

        // Pre-launch item 164: held, final settlement pending, nothing promised to either side.
        Assert.Equal(DepositStates.HeldUnresolved, financials.Deposit.State);
        Assert.Equal(CommissionStates.Undecided, financials.Commission.State);
    }

    [Fact]
    public void An_office_cancellation_holds_the_deposit_until_the_window_then_releases_it()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", Now.AddHours(3)).IsSuccess);
        Assert.Null(BookingEndingRefunds.Record(booking, payment, Now.AddHours(3)));

        var waiting = Calculate(booking, [payment]);
        var release = payment.RefundHeldDeposit(Money.Jod(18m), booking.DisputeWindowEndsAt!.Value).Value!;
        var released = Calculate(booking, [payment], now: booking.DisputeWindowEndsAt.Value);

        Assert.Equal(DepositStates.HeldUntilWindowCloses, waiting.Deposit.State);
        Assert.Equal(booking.DisputeWindowEndsAt, waiting.Deposit.WindowEndsAt);
        Assert.Equal(CommissionStates.Undecided, waiting.Commission.State);
        Assert.Equal(DepositStates.Released, released.Deposit.State);
        Assert.Equal(release.Id, released.Deposit.Refund!.RefundId);
        Assert.Equal(CommissionStates.NotEarned, released.Commission.State);
    }

    /// <summary>
    /// The full-payment version of the clean release: the office cancels late, everything above the
    /// deposit goes back with the fee, and the deposit follows when the window closes with nothing
    /// holding it. The release's amount is checked against its rule on a FULL payment, not only a
    /// deposit, and the office reads all the booking money it applied as returned.
    /// </summary>
    [Fact]
    public void A_full_payment_cancelled_by_the_office_is_returned_whole_once_the_window_closes_cleanly()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m);
        var cancelledAt = Now.AddHours(3);
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", cancelledAt).IsSuccess);
        var aboveDeposit = BookingEndingRefunds.Record(booking, payment, cancelledAt)!;
        Settle(aboveDeposit);
        var release = payment.RefundHeldDeposit(Money.Jod(18m), booking.DisputeWindowEndsAt!.Value).Value!;
        Settle(release);

        var financials = Calculate(booking, [payment], now: booking.DisputeWindowEndsAt.Value.AddMinutes(1));
        var office = BookingFinancialsDto.For(financials, BookingParty.Dealer);

        Assert.Same(RefundReason.EndedBeforePickup, aboveDeposit.Reason);
        Assert.Equal(Money.Jod(76.5m), aboveDeposit.Amount);
        Assert.Same(RefundReason.DisputeWindowClosed, release.Reason);
        Assert.Empty(financials.Issues);
        Assert.Equal(DepositStates.Released, financials.Deposit.State);
        Assert.Equal(Money.Jod(18m), financials.Deposit.Amount);
        Assert.Equal(release.Id, financials.Deposit.Refund!.RefundId);
        Assert.Equal(CommissionStates.NotEarned, financials.Commission.State);
        Assert.Equal(Money.Jod(94.5m), financials.Summary.Refunds.Settled);
        var record = Assert.Single(financials.Payments);
        Assert.Equal(RefundProgresses.Complete, record.RefundProgress);
        Assert.Equal(RefundProgresses.Complete, record.OfficeRefundProgress);
        // The office reads booking money only: all 90 it applied, and never the fee.
        Assert.Equal(90m, office.Summary.Refunded.Amount);
        Assert.Equal(RefundProgresses.Complete, Assert.Single(office.Payments).RefundProgress);
    }

    [Fact]
    public void An_administrators_cancellation_returns_the_whole_payment()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true);
        Assert.True(booking.Cancel(BookingParty.Admin, Id.New(), "Office suspended.", AfterFreeWindow(booking)).IsSuccess);
        var refund = BookingEndingRefunds.Record(booking, payment, Now)!;

        var financials = Calculate(booking, [payment]);

        Assert.Same(RefundReason.PlatformCancellation, refund.Reason);
        Assert.Equal(DepositStates.ReturnedWithPayment, financials.Deposit.State);
        Assert.Equal(CommissionStates.NotEarned, financials.Commission.State);
        Assert.Equal(BalanceStates.NotDue, financials.Balance.State);
    }

    [Fact]
    public void A_delivery_no_show_waits_for_the_window_but_a_self_pickup_no_show_holds_the_deposit_for_its_penalty()
    {
        var (delivery, deliveryPayment) = Build.PaidBooking(pickupMethod: PickupMethod.Delivery);
        Assert.True(delivery.MarkNoShow(delivery.Period.Start.Add(delivery.Terms.NoShowTimeout)).IsSuccess);
        var (selfPickup, selfPayment) = Build.PaidBooking();
        Assert.True(selfPickup.MarkNoShow(selfPickup.Period.Start.Add(selfPickup.Terms.NoShowTimeout)).IsSuccess);
        var at = selfPickup.Period.Start.Add(selfPickup.Terms.NoShowTimeout).AddHours(1);

        Assert.Equal(DepositStates.HeldUntilWindowCloses, Calculate(delivery, [deliveryPayment], now: at).Deposit.State);
        Assert.Equal(DepositStates.HeldForAssessedPenalty, Calculate(selfPickup, [selfPayment], now: at).Deposit.State);
    }

    // ── Disputes ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The KH-NY8AHLNK shape: paid in full, cancelled late (everything above the deposit refunded), and
    /// a dispute that split the 18 deposit 9 to the customer and 9 to the office — two refunds against
    /// one payment. Each reader sees its own share (owner, 2026-09-26).
    /// </summary>
    [Fact]
    public void A_dispute_split_shows_each_reader_only_its_share()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);
        BookingEndingRefunds.Record(booking, payment, Now);
        var ticket = Resolved(booking, refund: 9m, platform: 0m, dealer: 9m, Now.AddHours(5));
        Assert.True(payment.RequestRefund(Money.Jod(9m), ticket.Id, Now.AddHours(6)).IsSuccess);

        var financials = Calculate(booking, [payment], [ticket]);
        var customer = BookingFinancialsDto.For(financials, BookingParty.Customer);
        var office = BookingFinancialsDto.For(financials, BookingParty.Dealer);
        var admin = BookingFinancialsDto.For(financials, BookingParty.Admin);

        Assert.Equal(DepositStates.DecidedByDispute, financials.Deposit.State);
        Assert.Equal(2, Assert.Single(financials.Payments).Refunds.Count);
        Assert.False(financials.NeedsReview);

        Assert.Equal(9m, customer.Deposit.Decision!.ToCustomer!.Amount);
        Assert.Equal("Requested", customer.Deposit.Decision.ToCustomerRefundStatus);
        Assert.Null(customer.Deposit.Decision.ToOffice);
        Assert.Null(customer.Deposit.Decision.KeptByPlatform);
        Assert.Equal(2, Assert.Single(customer.Payments).Refunds.Count);

        Assert.Equal(9m, office.Deposit.Decision!.ToOffice!.Amount);
        Assert.Null(office.Deposit.Decision.ToCustomer);
        Assert.Null(office.Deposit.Decision.KeptByPlatform);
        // The customer's share is not the office's to read, as a refund row either.
        Assert.Equal("EndedBeforePickup", Assert.Single(Assert.Single(office.Payments).Refunds).Reason);

        Assert.Equal(9m, admin.Deposit.Decision!.ToCustomer!.Amount);
        Assert.Equal(9m, admin.Deposit.Decision.ToOffice!.Amount);
        Assert.Equal(0m, admin.Deposit.Decision.KeptByPlatform!.Amount);
    }

    /// <summary>
    /// The office's badge is read from the rows the office is shown (advisor, Phase 4a). In the
    /// KH-NY8AHLNK shape the refund above the deposit has settled while the customer's dispute share is
    /// still on its way: the customer and the administrator read "in progress", the office — shown only
    /// the settled row — "partly refunded", never the status of a row it cannot see.
    /// </summary>
    [Fact]
    public void The_offices_refund_badge_is_read_from_the_rows_the_office_is_shown()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);
        Settle(BookingEndingRefunds.Record(booking, payment, Now)!);
        var ticket = Resolved(booking, refund: 9m, platform: 0m, dealer: 9m, Now.AddHours(5));
        var share = payment.RequestRefund(Money.Jod(9m), ticket.Id, Now.AddHours(6)).Value;

        var onItsWay = Calculate(booking, [payment], [ticket]);

        Assert.Equal("InProgress", Assert.Single(BookingFinancialsDto.For(onItsWay, BookingParty.Customer).Payments).RefundProgress);
        Assert.Equal("InProgress", Assert.Single(BookingFinancialsDto.For(onItsWay, BookingParty.Admin).Payments).RefundProgress);
        var office = Assert.Single(BookingFinancialsDto.For(onItsWay, BookingParty.Dealer).Payments);
        Assert.Equal("Partial", office.RefundProgress);
        Assert.Equal("Settled", Assert.Single(office.Refunds).Status);

        Settle(share);
        var settled = Calculate(booking, [payment], [ticket]);

        Assert.Equal("Partial", Assert.Single(BookingFinancialsDto.For(settled, BookingParty.Customer).Payments).RefundProgress);
        Assert.Equal("Partial", Assert.Single(BookingFinancialsDto.For(settled, BookingParty.Dealer).Payments).RefundProgress);
    }

    /// <summary>
    /// A dispute that returned the WHOLE deposit completes the customer's refund, and the office's
    /// badge must not say so: "refunded in full" would tell an office that got nothing that the
    /// customer got everything — the customer's share, read back out of a badge.
    /// </summary>
    [Fact]
    public void The_customers_dispute_share_cannot_be_read_back_out_of_the_offices_badge()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);
        Settle(BookingEndingRefunds.Record(booking, payment, Now)!);
        var ticket = Resolved(booking, refund: 18m, platform: 0m, dealer: 0m, Now.AddHours(5));
        Settle(payment.RequestRefund(Money.Jod(18m), ticket.Id, Now.AddHours(6)).Value);

        var financials = Calculate(booking, [payment], [ticket]);
        var office = BookingFinancialsDto.For(financials, BookingParty.Dealer);

        Assert.Equal(RefundProgresses.Complete, Assert.Single(financials.Payments).RefundProgress);
        Assert.Equal(RefundProgresses.Partial, Assert.Single(office.Payments).RefundProgress);
        Assert.Equal(72m, office.Summary.Refunded.Amount);
        Assert.Null(office.Deposit.Decision!.ToCustomer);
    }

    /// <summary>
    /// A ticket that was WITHDRAWN is neither resolved nor live: the deposit reads as the booking's own
    /// status says, as if no dispute was ever raised (the release rule's reading too). Handed to the
    /// calculator among the resolved ones, it still decides nothing.
    /// </summary>
    [Fact]
    public void A_withdrawn_ticket_decides_nothing_and_the_deposit_reads_as_its_status_says()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", Now.AddHours(3)).IsSuccess);
        var ticket = DisputeTicket.Open(booking.Id, booking.CustomerId, BookingParty.Customer, "Never came.", TimeSpan.FromHours(48), Now.AddHours(4)).Value;
        Assert.True(ticket.Withdraw(booking.CustomerId, Now.AddHours(5)).IsSuccess);

        var none = Calculate(booking, [payment]);
        var handedOver = Calculate(booking, [payment], [ticket]);

        foreach (var financials in new[] { none, handedOver })
        {
            Assert.Equal(DepositStates.HeldUntilWindowCloses, financials.Deposit.State);
            Assert.Equal(booking.DisputeWindowEndsAt, financials.Deposit.WindowEndsAt);
            Assert.Null(financials.Deposit.Decision);
            Assert.False(financials.NeedsReview);
        }
    }

    [Fact]
    public void A_second_dispute_that_split_nothing_keeps_the_first_decision()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);
        var first = Resolved(booking, refund: 13m, platform: 0m, dealer: 5m, Now.AddHours(5));
        Assert.True(payment.RequestRefund(Money.Jod(13m), first.Id, Now.AddHours(6)).IsSuccess);
        var second = DisputeTicket.Open(booking.Id, booking.CustomerId, BookingParty.Customer, "Again.", TimeSpan.FromHours(48), Now.AddHours(8)).Value;
        Assert.True(second.Resolve(DisputeResolution.Create(
            DepositDisposition.Create(Money.Jod(0m), Money.Jod(0m), Money.Jod(0m), Money.Jod(0m)).Value,
            null, null, "Nothing left.", Id.New(), Now.AddHours(9)).Value).IsSuccess);

        var financials = Calculate(booking, [payment], [first, second]);

        Assert.Equal(DepositStates.DecidedByDispute, financials.Deposit.State);
        Assert.Equal(Money.Jod(13m), financials.Deposit.Decision!.ToCustomer);
        Assert.Equal(Money.Jod(5m), financials.Deposit.Decision.ToOffice);
        Assert.Equal(2, financials.Deposit.Decision.TicketIds.Count);
        Assert.False(financials.NeedsReview);
    }

    // ── Refused refunds and captures that never applied ─────────────────────────────────────────────

    [Fact]
    public void A_refused_refund_reads_as_delayed_and_still_owed()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now).IsSuccess);
        var refund = BookingEndingRefunds.Record(booking, payment, Now)!;
        refund.MarkFailed("card_closed", Now.AddMinutes(1));

        var financials = Calculate(booking, [payment]);
        var admin = BookingFinancialsDto.For(financials, BookingParty.Admin);
        var customer = BookingFinancialsDto.For(financials, BookingParty.Customer);

        Assert.Equal(RefundProgresses.Delayed, Assert.Single(financials.Payments).RefundProgress);
        Assert.Equal(Money.Jod(18m), financials.Summary.Refunds.Delayed);
        Assert.Equal("card_closed", Assert.Single(Assert.Single(admin.Payments).Refunds).FailureCode);
        // No provider code reaches a customer (pre-launch item 135 is still open).
        Assert.Null(Assert.Single(Assert.Single(customer.Payments).Refunds).FailureCode);
    }

    [Fact]
    public void A_capture_that_could_not_be_applied_is_shown_to_the_customer_and_the_administrator_only()
    {
        var booking = Build.ApprovedBooking();
        Assert.True(booking.ExpireUnpaid(booking.PaymentDeadline!.Value.AddMinutes(1)).IsSuccess);
        var late = Payment.Open(booking.Id, booking.CustomerId, Money.Jod(18m), "TestProvider", Now.AddMinutes(30), Now);
        Assert.True(late.AttachProviderSession("sess_late", "https://provider.test/checkout").IsSuccess);
        Assert.True(late.Orphan(Money.Jod(18m), Now.AddMinutes(5), "booking.not_awaiting_payment", Now.AddMinutes(5)).IsSuccess);

        var financials = Calculate(booking, [late]);
        var customer = BookingFinancialsDto.For(financials, BookingParty.Customer);
        var office = BookingFinancialsDto.For(financials, BookingParty.Dealer);
        var admin = BookingFinancialsDto.For(financials, BookingParty.Admin);

        Assert.Equal(BalanceStates.NotDue, financials.Balance.State);
        Assert.Equal(DepositStates.NotPaid, financials.Deposit.State);
        Assert.Equal(CommissionStates.NotApplicable, financials.Commission.State);
        Assert.True(financials.Summary.PaidOnline.IsZero);
        Assert.Equal(Money.Jod(18m), financials.Summary.ChargedOnline);

        var orphan = Assert.Single(customer.Payments);
        Assert.Equal("Orphaned", orphan.Status);
        Assert.Equal(0m, orphan.AppliedToBooking.Amount);
        Assert.Equal("OrphanedCapture", Assert.Single(orphan.Refunds).Reason);
        Assert.Null(orphan.OrphanReason);
        Assert.Empty(office.Payments);
        Assert.Equal(0m, office.Summary.Refunded.Amount + office.Summary.RefundInProgress.Amount);
        Assert.Equal("booking.not_awaiting_payment", Assert.Single(admin.Payments).OrphanReason);
    }

    /// <summary>
    /// A capture the provider took in ANOTHER currency is listed with its payment — the customer must
    /// see money that left their card — and never added to a total in the booking's currency, which it
    /// cannot be part of. Every step that splits or sums money branches on the currency; none throws.
    /// </summary>
    [Fact]
    public void A_capture_in_another_currency_is_listed_never_summed_and_never_throws()
    {
        var booking = Build.ApprovedBooking();
        Assert.True(booking.ExpireUnpaid(booking.PaymentDeadline!.Value.AddMinutes(1)).IsSuccess);
        var late = Payment.Open(booking.Id, booking.CustomerId, Money.Jod(18m), "TestProvider", Now.AddMinutes(30), Now,
            PaymentPurpose.Deposit, Money.Jod(0.5m), feeRefundable: true);
        Assert.True(late.AttachProviderSession("sess_usd", "https://provider.test/checkout").IsSuccess);
        Assert.True(late.Orphan(Money.Create(25.4m, "USD"), Now.AddMinutes(5), "booking.not_awaiting_payment", Now.AddMinutes(5)).IsSuccess);

        var financials = Calculate(booking, [late]);
        var customer = BookingFinancialsDto.For(financials, BookingParty.Customer);

        Assert.Equal("JOD", financials.Currency);
        Assert.True(financials.Summary.ChargedOnline.IsZero);
        Assert.Equal("JOD", financials.Summary.ChargedOnline.CurrencyCode);
        Assert.True(financials.Summary.Refunds.InProgress.IsZero);
        var record = Assert.Single(financials.Payments);
        Assert.Equal(Money.Create(25.4m, "USD"), record.AmountCharged);
        Assert.Equal(RefundProgresses.InProgress, record.RefundProgress);
        var refund = Assert.Single(record.Refunds);
        Assert.Equal(Money.Create(25.4m, "USD"), refund.BookingPart);
        Assert.True(refund.FeePart.IsZero);
        var listed = Assert.Single(customer.Payments);
        Assert.Equal("USD", listed.AmountCharged!.Currency);
    }

    [Fact]
    public void A_non_refundable_fee_is_kept_when_the_whole_payment_goes_back()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m, feeRefundable: false);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now).IsSuccess);
        Settle(BookingEndingRefunds.Record(booking, payment, Now)!);

        var financials = Calculate(booking, [payment]);
        var record = Assert.Single(financials.Payments);

        Assert.False(record.FeeRefundable);
        Assert.Equal(Money.Jod(90m), Assert.Single(record.Refunds).Amount);
        Assert.True(Assert.Single(record.Refunds).FeePart.IsZero);
        Assert.Equal(RefundProgresses.Complete, record.RefundProgress);
        Assert.Equal(CommissionStates.NotEarned, financials.Commission.State);
    }

    // ── Records that contradict one another ─────────────────────────────────────────────────────────

    [Fact]
    public void A_missing_ending_refund_is_reported_to_the_administrator_not_thrown()
    {
        var (booking, payment) = Build.PaidBooking();
        // Cancelled inside the free window with NO refund recorded: the 2316 case (pre-launch item 165).
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now).IsSuccess);

        var financials = BookingFinancialsCalculator.Calculate(booking, [payment], [], false, Now);
        var admin = BookingFinancialsDto.For(financials, BookingParty.Admin);
        var customer = BookingFinancialsDto.For(financials, BookingParty.Customer);

        Assert.True(financials.NeedsReview);
        Assert.Contains(FinancialIssues.EndingRefundMissing, admin.Issues!);
        Assert.Equal(DepositStates.ReturnedWithPayment, financials.Deposit.State);
        Assert.Null(financials.Deposit.Refund);
        Assert.True(customer.NeedsReview);
        Assert.Null(customer.Issues);
    }

    [Fact]
    public void A_confirming_payment_that_cannot_be_found_is_reported_not_thrown()
    {
        // Confirmed against a payment id nothing stores — a booking from before Payments existed.
        var booking = Build.ConfirmedBooking();

        var financials = BookingFinancialsCalculator.Calculate(booking, [], [], false, Now);

        Assert.Contains(FinancialIssues.ConfirmingPaymentMissing, financials.Issues);
        Assert.Equal(DepositStates.Held, financials.Deposit.State);
        Assert.Equal(BalanceStates.DueAtHandover, financials.Balance.State);
    }

    [Fact]
    public void A_refund_that_does_not_match_its_rule_is_reported()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);
        // A dispute refund with no resolved ticket of this booking behind it.
        Assert.True(payment.RequestRefund(Money.Jod(5m), Id.New(), Now.AddHours(5)).IsSuccess);

        var financials = BookingFinancialsCalculator.Calculate(booking, [payment], [], false, Now);

        Assert.Contains(FinancialIssues.RefundAmountUnexpected, financials.Issues);
    }

    // ── What each reader may see ────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_customer_never_receives_commission_provider_references_or_sandbox_markers()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now).IsSuccess);
        var refund = BookingEndingRefunds.Record(booking, payment, Now)!;
        refund.MarkSent("rf_provider_ref", Now);

        var customer = BookingFinancialsDto.For(Calculate(booking, [payment]), BookingParty.Customer);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(customer, Web));
        var root = json.RootElement;
        var paymentJson = root.GetProperty("payments")[0];

        Assert.Equal(JsonValueKind.Null, root.GetProperty("commission").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("issues").ValueKind);
        Assert.Equal(JsonValueKind.Null, paymentJson.GetProperty("providerReference").ValueKind);
        Assert.Equal(JsonValueKind.Null, paymentJson.GetProperty("isSandbox").ValueKind);
        Assert.Equal(JsonValueKind.Null, paymentJson.GetProperty("refunds")[0].GetProperty("providerReference").ValueKind);
        Assert.Equal(1, root.GetProperty("calculatorVersion").GetInt32());
        Assert.Equal("Cancelled", root.GetProperty("bookingStatus").GetString());
        Assert.Equal("JOD", root.GetProperty("currency").GetString());
    }

    [Fact]
    public void The_office_and_the_administrator_see_the_frozen_commission()
    {
        var (booking, payment) = Build.PaidBooking();
        var financials = Calculate(booking, [payment]);

        var office = BookingFinancialsDto.For(financials, BookingParty.Dealer);
        var admin = BookingFinancialsDto.For(financials, BookingParty.Admin);

        Assert.Equal(6m, office.Commission!.Amount.Amount);
        Assert.Equal(20m, office.Commission.Percent);
        Assert.Equal("OneDay", office.Commission.Basis);
        Assert.Equal(CommissionStates.Expected, office.Commission.State);
        Assert.Equal(office.Commission, admin.Commission);
    }
}
