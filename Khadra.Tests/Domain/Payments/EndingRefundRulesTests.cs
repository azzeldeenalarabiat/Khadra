using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Payments;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Payments;

/// <summary>
/// Phase 3 (owner, 2026-09-26): what a paid booking that ENDS before pickup owes the customer, and
/// when the deposit it holds goes back.
/// </summary>
/// <remarks>
/// The default test booking is 3 days at 30 JOD: 90 for the booking, an 18 deposit (20%). "Paid in
/// full" adds a 4.5 processing fee on top where a test says so, so the card was charged 94.5.
/// </remarks>
public sealed class EndingRefundRulesTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    private static DateTimeOffset AfterFreeWindow(Booking booking) =>
        booking.FreeCancellationDeadline!.Value.AddMinutes(1);

    // ---------------------------------------------------------------- the whole payment

    [Fact]
    public void A_customer_cancelling_inside_the_free_window_returns_the_whole_payment()
    {
        var (booking, _) = Build.PaidBooking(inFull: true);

        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now).IsSuccess);

        Assert.True(booking.ReturnsWholePayment);
        Assert.True(booking.RefundableAboveDeposit.IsZero);
    }

    /// <summary>Decision 1: an administrator's cancellation before pickup returns everything, deposit included.</summary>
    [Fact]
    public void An_administrator_cancelling_returns_the_whole_payment_even_after_the_free_window()
    {
        var (booking, _) = Build.PaidBooking();

        Assert.True(booking.Cancel(BookingParty.Admin, Id.New(), "Office suspended.", AfterFreeWindow(booking)).IsSuccess);

        Assert.True(booking.ReturnsWholePayment);
    }

    [Fact]
    public void A_gallery_cancelling_or_a_non_delivery_report_does_not_return_the_whole_payment()
    {
        var (byGallery, _) = Build.PaidBooking(inFull: true);
        Assert.True(byGallery.Cancel(BookingParty.Dealer, Id.New(), "No car.", Now).IsSuccess);
        Assert.False(byGallery.ReturnsWholePayment);
        Assert.Equal(Money.Jod(72m), byGallery.RefundableAboveDeposit);

        var (notDelivered, _) = Build.PaidBooking(inFull: true);
        Assert.True(notDelivered.ReportDealerNonDelivery(notDelivered.CustomerId, "Nobody came.", notDelivered.Period.Start).IsSuccess);
        Assert.False(notDelivered.ReturnsWholePayment);
        Assert.Equal(Money.Jod(72m), notDelivered.RefundableAboveDeposit);
    }

    [Fact]
    public void An_unpaid_booking_returns_nothing_however_it_ends()
    {
        var booking = Build.ApprovedBooking();

        Assert.True(booking.Cancel(BookingParty.Admin, Id.New(), "Anything.", Now).IsSuccess);

        Assert.False(booking.ReturnsWholePayment);
        Assert.True(booking.RefundableAboveDeposit.IsZero);
    }

    // ---------------------------------------------------------------- above the deposit

    [Fact]
    public void A_late_cancellation_of_a_booking_paid_in_full_owes_everything_above_the_deposit()
    {
        var (booking, _) = Build.PaidBooking(inFull: true);

        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);

        Assert.False(booking.ReturnsWholePayment);
        Assert.Equal(Money.Jod(72m), booking.RefundableAboveDeposit);
        Assert.Equal(Money.Jod(72m), booking.PaidAboveDeposit);
    }

    /// <summary>The owner's rule: a deposit-only booking gains no new above-deposit refund.</summary>
    [Fact]
    public void A_deposit_only_booking_owes_nothing_above_its_deposit()
    {
        var (booking, _) = Build.PaidBooking();

        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);

        Assert.True(booking.PaidAboveDeposit.IsZero);
        Assert.True(booking.RefundableAboveDeposit.IsZero);
        Assert.False(BookingEndingRefunds.OwesRefund(booking));
    }

    [Fact]
    public void A_no_show_of_a_booking_paid_in_full_owes_everything_above_the_deposit()
    {
        var (booking, _) = Build.PaidBooking(inFull: true);

        Assert.True(booking.MarkNoShow(booking.Period.Start.Add(booking.Terms.NoShowTimeout).AddMinutes(1)).IsSuccess);

        Assert.Equal(Money.Jod(72m), booking.RefundableAboveDeposit);
        Assert.True(BookingEndingRefunds.OwesRefund(booking));
    }

    [Fact]
    public void A_booking_that_has_not_ended_owes_nothing_yet()
    {
        var (booking, _) = Build.PaidBooking(inFull: true);

        Assert.Equal(Money.Jod(72m), booking.PaidAboveDeposit);
        Assert.True(booking.RefundableAboveDeposit.IsZero);
        Assert.False(BookingEndingRefunds.OwesRefund(booking));
    }

    // ---------------------------------------------------------------- the payment's half: the fee, and each refund once

    [Theory]
    [InlineData(true, 76.5)]
    [InlineData(false, 72)]
    public void The_money_above_the_deposit_goes_back_with_the_fee_only_when_the_payment_froze_it_refundable(
        bool feeRefundable, decimal expected)
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m, feeRefundable: feeRefundable);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);

        var refund = BookingEndingRefunds.Record(booking, payment, AfterFreeWindow(booking))!;

        Assert.Same(RefundReason.EndedBeforePickup, refund.Reason);
        Assert.Equal(Money.Jod(expected), refund.Amount);
        // Recording the same ending again is the same refund, never a second one.
        Assert.Same(refund, BookingEndingRefunds.Record(booking, payment, AfterFreeWindow(booking).AddMinutes(5)));
        Assert.Single(payment.Refunds);
    }

    [Fact]
    public void Nothing_above_the_deposit_records_nothing_not_even_the_fee()
    {
        var (_, payment) = Build.PaidBooking(inFull: true, fee: 4.5m);

        var refund = payment.RefundAboveDeposit(Money.Jod(0m), Now);

        Assert.True(refund.IsSuccess);
        Assert.Null(refund.Value);
        Assert.Empty(payment.Refunds);
    }

    [Fact]
    public void The_whole_payment_follows_the_fee_rule_the_payment_froze()
    {
        var (_, refundable) = Build.PaidBooking(inFull: true, fee: 4.5m);
        var (_, kept) = Build.PaidBooking(inFull: true, fee: 4.5m, feeRefundable: false);

        Assert.Equal(Money.Jod(94.5m), refundable.RefundWholePayment(RefundReason.PlatformCancellation, Now).Value.Amount);
        Assert.Equal(Money.Jod(90m), kept.RefundWholePayment(RefundReason.PlatformCancellation, Now).Value.Amount);
    }

    [Fact]
    public void Only_a_whole_payment_reason_may_return_the_whole_payment()
    {
        var (_, payment) = Build.PaidBooking();

        Assert.Throws<DomainException>(() => payment.RefundWholePayment(RefundReason.EndedBeforePickup, Now));
    }

    /// <summary>
    /// The over-refund guard counts a FAILED refund: it is still owed and being re-sent. The ending's
    /// refund and the deposit together are exactly the capture, and nothing fits on top.
    /// </summary>
    [Fact]
    public void A_failed_refund_still_counts_against_what_can_be_promised_back()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m);
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", AfterFreeWindow(booking)).IsSuccess);
        var above = BookingEndingRefunds.Record(booking, payment, AfterFreeWindow(booking))!;
        above.MarkFailed("refund_declined", Now);

        var deposit = payment.RefundHeldDeposit(Money.Jod(18m), Now.AddDays(3));

        Assert.Equal(Money.Jod(18m), deposit.Value!.Amount);
        Assert.Equal(payment.AmountCaptured, payment.RefundedOrOwed);
        Assert.Equal("payments.refund_exceeds_capture", payment.RequestRefund(Money.Jod(0.001m), null, Now).Error.Code);
    }

    [Fact]
    public void Releasing_the_deposit_twice_is_the_same_refund()
    {
        var (_, payment) = Build.PaidBooking();

        var first = payment.RefundHeldDeposit(Money.Jod(18m), Now).Value!;
        var second = payment.RefundHeldDeposit(Money.Jod(18m), Now.AddMinutes(1)).Value!;

        Assert.Same(first, second);
        Assert.Same(RefundReason.DisputeWindowClosed, first.Reason);
        Assert.Single(payment.Refunds);
    }

    [Fact]
    public void Refunded_in_full_means_everything_the_payment_will_ever_return_has_settled()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m, feeRefundable: false);
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", AfterFreeWindow(booking)).IsSuccess);
        var above = BookingEndingRefunds.Record(booking, payment, AfterFreeWindow(booking))!;
        var deposit = payment.RefundHeldDeposit(Money.Jod(18m), Now.AddDays(3)).Value!;

        above.MarkSettled(Now);
        Assert.Equal(Money.Jod(72m), payment.RefundSettled);
        Assert.False(payment.IsRefundedInFull);

        deposit.MarkFailed("refund_declined", Now);
        Assert.False(payment.IsRefundedInFull);

        deposit.MarkSettled(Now);
        // 94.5 captured, the 4.5 fee kept: 90 is everything this payment will ever return.
        Assert.Equal(Money.Jod(90m), payment.RefundSettled);
        Assert.True(payment.IsRefundedInFull);
    }

    [Fact]
    public void A_refund_is_found_by_the_providers_own_reference_and_by_nothing_else()
    {
        var (_, payment) = Build.PaidBooking(inFull: true);
        var refund = payment.RefundHeldDeposit(Money.Jod(18m), Now).Value!;
        refund.MarkSent("rf_deposit", Now);

        Assert.Same(refund, payment.RefundWithProviderReference("rf_deposit"));
        Assert.Null(payment.RefundWithProviderReference("RF_DEPOSIT"));
        Assert.Null(payment.RefundWithProviderReference("rf_other"));
        Assert.Null(payment.RefundWithProviderReference(" "));
    }

    // ---------------------------------------------------------------- the clean close (decision 3a)

    private static Booking EndedWithPenaltyOnTheGallery(out Payment payment, bool inFull = false)
    {
        var (booking, paid) = Build.PaidBooking(inFull);
        payment = paid;
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", AfterFreeWindow(booking)).IsSuccess);
        return booking;
    }

    private static DateTimeOffset WindowCloses(Booking booking) =>
        booking.FinishedAt!.Value.Add(booking.Terms.PostReturnSettlementWindow);

    [Fact]
    public void The_deposit_is_released_only_once_the_bookings_own_window_has_closed()
    {
        var booking = EndedWithPenaltyOnTheGallery(out _);

        Assert.True(booking.DepositReleasedOnCleanClose(WindowCloses(booking).AddTicks(-1), depositClaimed: false).IsZero);
        Assert.Equal(Money.Jod(18m), booking.DepositReleasedOnCleanClose(WindowCloses(booking), depositClaimed: false));
        // The window is the one the booking FROZE, and it is the same one a dispute is judged by.
        Assert.False(booking.CanBeDisputed(WindowCloses(booking)));
        Assert.True(booking.CanBeDisputed(WindowCloses(booking).AddTicks(-1)));
    }

    [Fact]
    public void A_claim_on_the_deposit_holds_it_whatever_the_clock_says()
    {
        var booking = EndedWithPenaltyOnTheGallery(out _);

        Assert.True(booking.DepositReleasedOnCleanClose(WindowCloses(booking).AddDays(365), depositClaimed: true).IsZero);
    }

    [Fact]
    public void A_penalty_against_the_customer_holds_the_deposit_but_a_penalty_that_owes_nothing_does_not()
    {
        var (lateCancel, _) = Build.PaidBooking();
        Assert.True(lateCancel.Cancel(BookingParty.Customer, lateCancel.CustomerId, null, AfterFreeWindow(lateCancel)).IsSuccess);
        Assert.True(lateCancel.DepositReleasedOnCleanClose(WindowCloses(lateCancel).AddDays(30), false).IsZero);

        var (noShow, _) = Build.PaidBooking();
        Assert.True(noShow.MarkNoShow(noShow.Period.Start.Add(noShow.Terms.NoShowTimeout).AddMinutes(1)).IsSuccess);
        Assert.True(noShow.DepositReleasedOnCleanClose(WindowCloses(noShow).AddDays(30), false).IsZero);

        // Terms under which a late cancellation costs the customer nothing: no penalty in substance.
        var (lenient, _) = Build.PaidBooking(terms: Build.Terms(customerPenaltyPercent: 0m));
        Assert.True(lenient.Cancel(BookingParty.Customer, lenient.CustomerId, null, AfterFreeWindow(lenient)).IsSuccess);
        Assert.Same(BookingParty.Customer, lenient.Penalty!.AttributedTo);
        Assert.Equal(Money.Jod(18m), lenient.DepositReleasedOnCleanClose(WindowCloses(lenient), false));
    }

    [Fact]
    public void A_booking_that_already_returned_its_whole_payment_has_no_deposit_left_to_release()
    {
        var (booking, _) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now).IsSuccess);

        Assert.True(booking.DepositReleasedOnCleanClose(WindowCloses(booking), false).IsZero);
    }

    [Fact]
    public void A_booking_paid_in_full_releases_exactly_its_deposit()
    {
        var booking = EndedWithPenaltyOnTheGallery(out _, inFull: true);

        Assert.Equal(Money.Jod(18m), booking.DepositReleasedOnCleanClose(WindowCloses(booking), false));
    }

    // ---------------------------------------------------------------- the cancel sheet's figure

    /// <summary>
    /// The figure a screen promises and the one <c>expectedRefund</c> is checked against are one rule
    /// through two doors: from the aggregate, and from the read model a DTO is built from.
    /// </summary>
    [Theory]
    [InlineData(false, 0, true, 0)]
    [InlineData(false, 0, true, 90)]
    [InlineData(true, 4.5, true, 0)]
    [InlineData(true, 4.5, true, 90)]
    [InlineData(true, 4.5, false, 0)]
    [InlineData(true, 4.5, false, 90)]
    public void The_cancel_sheet_states_exactly_what_the_cancellation_guard_checks(
        bool inFull, decimal fee, bool feeRefundable, int minutesAfterPayment)
    {
        var (booking, payment) = Build.PaidBooking(inFull, fee, feeRefundable);
        var now = Now.AddMinutes(minutesAfterPayment);
        var readModel = new ConfirmingPaymentDto(
            payment.Purpose.Name,
            MoneyDto.From(payment.AmountCaptured!),
            MoneyDto.From(payment.ProcessingFee),
            MoneyDto.From(payment.AppliedToBooking),
            payment.AppliedAt,
            MoneyDto.From(payment.WholePaymentRefundAmount!),
            MoneyDto.From(payment.RefundableFee));
        var context = new BookingContext(null, "Gallery", false, null, "Customer", false, null, null, ConfirmingPayment: readModel);

        var fromAggregate = BookingEndingRefunds.PreviewForCustomer(booking, payment, now);
        var onTheSheet = BookingDto.From(booking, context, now).Cancellation.RefundAmount;

        Assert.Equal(fromAggregate?.Amount, onTheSheet?.Amount);
        Assert.Equal(fromAggregate?.CurrencyCode, onTheSheet?.Currency);

        // And the cancellation records exactly that figure.
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, now).IsSuccess);
        var recorded = BookingEndingRefunds.Record(booking, payment, now);
        Assert.Equal(fromAggregate, recorded?.Amount);
    }
}
