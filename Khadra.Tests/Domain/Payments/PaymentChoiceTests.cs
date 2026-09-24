using Khadra.Application.Common.Ports;
using Khadra.Application.Payments;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Payments;

/// <summary>
/// The two ways an approved booking can be paid (owner, 2026-09-24): the mandatory deposit or the full
/// amount, every figure worked out once, on the server.
/// </summary>
/// <remarks>
/// The owner's own example runs through these tests: 50 JOD a day for 5 days is a 250 JOD booking, a
/// 50 JOD deposit, and a 200 JOD balance — or 250 now and nothing after.
/// </remarks>
public sealed class PaymentChoiceTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    private static Booking Approved(decimal dailyRate = 50m, int days = 5, decimal deliveryFee = 0m)
    {
        var booking = Build.Booking(
            Now,
            period: Build.Period(days: days),
            pickupMethod: deliveryFee > 0m ? PickupMethod.Delivery : null,
            pricing: Build.Pricing(dailyRate: dailyRate, days: days, deliveryFee: deliveryFee));
        booking.Approve(Id.New(), Now);
        return booking;
    }

    private static ProcessingFeePolicy Fee(decimal percent, ProcessingFeeBasis? basis = null, bool refundable = true) =>
        new(true, Build.Percent(percent), basis ?? ProcessingFeeBasis.FullAmount, refundable);

    // ── The two options ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_owners_example_deposit_or_everything()
    {
        var choices = PaymentChoices.For(Approved(), ProcessingFeePolicy.Disabled);

        var deposit = Assert.Single(choices, choice => choice.Purpose == PaymentPurpose.Deposit);
        Assert.Equal(Money.Jod(50m), deposit.BookingAmount);
        Assert.Equal(Money.Jod(0m), deposit.ProcessingFee);
        Assert.Equal(Money.Jod(50m), deposit.ChargedNow);
        Assert.Equal(Money.Jod(200m), deposit.RemainingAfter);

        var full = Assert.Single(choices, choice => choice.Purpose == PaymentPurpose.FullPayment);
        Assert.Equal(Money.Jod(250m), full.BookingAmount);
        Assert.Equal(Money.Jod(250m), full.ChargedNow);
        Assert.Equal(Money.Jod(0m), full.RemainingAfter);
    }

    [Fact]
    public void Deposit_comes_first_and_only_the_two_offered_options_exist()
    {
        var choices = PaymentChoices.For(Approved(), ProcessingFeePolicy.Disabled);

        Assert.Equal(new[] { PaymentPurpose.Deposit, PaymentPurpose.FullPayment }, choices.Select(choice => choice.Purpose));
    }

    [Fact]
    public void Paying_the_remaining_balance_online_is_not_offered_in_this_release()
    {
        var chosen = PaymentChoices.Choose(Approved(), PaymentPurpose.RemainingBalance, ProcessingFeePolicy.Disabled);

        Assert.Equal("payments.purpose_unavailable", chosen.Error.Code);
    }

    [Fact]
    public void The_delivery_fee_is_in_the_full_amount_but_never_in_the_deposit()
    {
        // 30 × 3 = 90 rental, 10 delivery: deposit 18 on the rental alone; the full payment is 100.
        var choices = PaymentChoices.For(Approved(dailyRate: 30m, days: 3, deliveryFee: 10m), ProcessingFeePolicy.Disabled);

        Assert.Equal(Money.Jod(18m), choices[0].BookingAmount);
        Assert.Equal(Money.Jod(82m), choices[0].RemainingAfter);
        Assert.Equal(Money.Jod(100m), choices[1].BookingAmount);
    }

    // ── The optional processing fee ───────────────────────────────────────────────────────────────

    [Fact]
    public void An_enabled_fee_on_the_whole_full_amount()
    {
        var full = PaymentChoices.For(Approved(), Fee(1.5m))[1];

        Assert.Equal(Money.Jod(3.75m), full.ProcessingFee);
        Assert.Equal(Money.Jod(253.75m), full.ChargedNow);
        // The fee is not rental: nothing about what the booking is still owed changes.
        Assert.Equal(Money.Jod(250m), full.BookingAmount);
        Assert.Equal(Money.Jod(0m), full.RemainingAfter);
    }

    [Fact]
    public void An_enabled_fee_on_only_the_part_above_the_deposit()
    {
        var full = PaymentChoices.For(Approved(), Fee(1.5m, ProcessingFeeBasis.AboveDeposit))[1];

        // 1.5% of 200, not of 250.
        Assert.Equal(Money.Jod(3m), full.ProcessingFee);
        Assert.Equal(Money.Jod(253m), full.ChargedNow);
    }

    [Fact]
    public void Khadra_absorbs_the_fee_on_the_mandatory_deposit()
    {
        var deposit = PaymentChoices.For(Approved(), Fee(1.5m))[0];

        Assert.Equal(Money.Jod(0m), deposit.ProcessingFee);
        Assert.Equal(Money.Jod(50m), deposit.ChargedNow);
    }

    [Fact]
    public void The_fee_rounds_to_three_decimals_half_to_even()
    {
        // 1.25% of 100.04 is 1.2505: half way at the third decimal, so the even 1.250.
        var fee = Fee(1.25m).FeeFor(PaymentPurpose.FullPayment, Money.Jod(100.04m), Money.Jod(20m));

        Assert.Equal(Money.Jod(1.250m), fee);
    }

    [Fact]
    public void A_disabled_fee_is_zero_whatever_its_figure()
    {
        var rules = TestBusinessRules.Values() with { ProcessingFee = new ProcessingFeeRules(false, 1.5m, "FullAmount", true) };

        Assert.Same(ProcessingFeePolicy.Disabled, PaymentChoices.PolicyFrom(rules));
        Assert.Same(ProcessingFeePolicy.Disabled, PaymentChoices.PolicyFrom(TestBusinessRules.Values()));
    }

    // ── What a payment and a booking record ───────────────────────────────────────────────────────

    [Fact]
    public void A_payment_keeps_its_fee_apart_from_what_it_puts_towards_the_booking()
    {
        var booking = Approved();
        var payment = Payment.Open(
            booking.Id, booking.CustomerId, Money.Jod(253.75m), TestPayments.TestProviderName,
            Now.AddMinutes(30), Now, PaymentPurpose.FullPayment, Money.Jod(3.75m));

        Assert.Equal(Money.Jod(253.75m), payment.Amount);
        Assert.Equal(Money.Jod(3.75m), payment.ProcessingFee);
        Assert.Equal(Money.Jod(250m), payment.AppliedToBooking);
        Assert.Same(PaymentPurpose.FullPayment, payment.Purpose);
    }

    [Fact]
    public void A_fee_can_never_be_the_whole_payment()
    {
        var booking = Approved();

        Assert.Throws<DomainException>(() => Payment.Open(
            booking.Id, booking.CustomerId, Money.Jod(5m), TestPayments.TestProviderName,
            Now.AddMinutes(30), Now, PaymentPurpose.FullPayment, Money.Jod(5m)));
    }

    [Fact]
    public void An_attempt_opened_the_old_way_is_a_deposit_with_no_fee()
    {
        var booking = Approved();
        var payment = Payment.Open(booking.Id, booking.CustomerId, Money.Jod(50m), TestPayments.TestProviderName, Now.AddMinutes(30), Now);

        Assert.Same(PaymentPurpose.Deposit, payment.Purpose);
        Assert.Equal(Money.Jod(0m), payment.ProcessingFee);
        Assert.Equal(Money.Jod(50m), payment.AppliedToBooking);
    }

    [Fact]
    public void Paying_the_full_amount_confirms_the_booking_and_leaves_nothing_owed()
    {
        var booking = Approved();

        var confirmed = booking.ConfirmPayment(Id.New(), Money.Jod(250m), Now);

        Assert.True(confirmed.IsSuccess);
        Assert.Same(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(Money.Jod(250m), booking.OnlinePaid);
        Assert.Equal(Money.Jod(0m), booking.RemainingBalance);
    }

    [Fact]
    public void Paying_the_deposit_leaves_the_balance_to_be_paid_to_the_office()
    {
        var booking = Approved();

        booking.ConfirmDepositPaid(Id.New(), Now);

        Assert.Equal(Money.Jod(50m), booking.OnlinePaid);
        Assert.Equal(Money.Jod(200m), booking.RemainingBalance);
    }

    [Fact]
    public void Before_any_payment_the_balance_is_the_deposit_only_projection()
    {
        var booking = Approved(dailyRate: 30m, days: 3, deliveryFee: 10m);

        Assert.Equal(Money.Jod(0m), booking.OnlinePaid);
        Assert.Equal(Money.Jod(82m), booking.RemainingBalance);
    }

    [Fact]
    public void Nothing_below_the_deposit_and_nothing_above_the_total_can_confirm_a_booking()
    {
        Assert.Equal("booking.payment_out_of_range", Approved().ConfirmPayment(Id.New(), Money.Jod(49.999m), Now).Error.Code);
        Assert.Equal("booking.payment_out_of_range", Approved().ConfirmPayment(Id.New(), Money.Jod(250.001m), Now).Error.Code);
    }

    [Fact]
    public void Confirming_with_the_same_payment_twice_is_one_confirmation()
    {
        var booking = Approved();
        var paymentId = Id.New();

        booking.ConfirmPayment(paymentId, Money.Jod(250m), Now);
        var again = booking.ConfirmPayment(paymentId, Money.Jod(250m), Now.AddMinutes(1));

        Assert.True(again.IsSuccess);
        Assert.Equal(Money.Jod(250m), booking.OnlinePaid);
    }

    [Fact]
    public void The_booking_response_says_nothing_is_owed_after_a_full_payment_and_hides_the_commission_from_the_customer()
    {
        var booking = Approved();
        booking.ConfirmPayment(Id.New(), Money.Jod(250m), Now);

        var dto = Khadra.Application.Bookings.Dtos.BookingDto.From(booking, new Khadra.Application.Bookings.ReadModels.BookingContext(null, "Petra Wheels", false, null, "Layla Odeh", false, null, null), Now);

        // Installed apps print pricing.balanceDue as "pay at pickup": a fully paid customer must read zero.
        Assert.Equal(0m, dto.Pricing.BalanceDue.Amount);
        Assert.Equal(250m, dto.OnlinePaid!.Amount);
        Assert.NotNull(dto.CommissionAmount);
        Assert.Null(dto.ForCustomer().CommissionAmount);
    }

    [Fact]
    public void A_capture_that_does_not_fit_the_booking_is_refused_not_thrown()
    {
        var result = Approved().ConfirmPayment(Id.New(), Money.Jod(49.999m), Now);

        Assert.Equal("booking.payment_out_of_range", result.Error.Code);
    }

    [Fact]
    public void A_free_cancellation_of_a_full_payment_returns_everything_captured()
    {
        var booking = Approved();
        var payment = Payment.Open(
            booking.Id, booking.CustomerId, Money.Jod(253.75m), TestPayments.TestProviderName, Now.AddMinutes(30), Now,
            PaymentPurpose.FullPayment, Money.Jod(3.75m));
        payment.Apply(Money.Jod(253.75m), Now, Now);

        var refund = payment.RefundForFreeCancellation(Now.AddMinutes(5));

        Assert.Equal(Money.Jod(253.75m), refund.Value.Amount);
    }

    [Fact]
    public void A_fee_opened_as_non_refundable_stays_when_the_payment_is_refunded()
    {
        var booking = Approved();
        var payment = Payment.Open(
            booking.Id, booking.CustomerId, Money.Jod(253.75m), TestPayments.TestProviderName, Now.AddMinutes(30), Now,
            PaymentPurpose.FullPayment, Money.Jod(3.75m), feeRefundable: false);
        payment.Apply(Money.Jod(253.75m), Now, Now);

        var refund = payment.RefundForFreeCancellation(Now.AddMinutes(5));

        Assert.Equal(Money.Jod(250m), refund.Value.Amount);
        Assert.False(payment.FeeRefundable);
    }

    [Fact]
    public void A_full_payment_puts_only_the_deposit_at_stake_in_a_dispute()
    {
        var booking = Approved();
        booking.ConfirmPayment(Id.New(), Money.Jod(250m), Now);

        // The office's rental revenue is never split by a dispute (owner, 2026-09-24).
        Assert.Equal(Money.Jod(50m), Khadra.Application.Bookings.BookingDisputeSettlement.DepositHeldFor(booking));
    }
}
