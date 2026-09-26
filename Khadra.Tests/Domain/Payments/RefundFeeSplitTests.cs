using Khadra.Application.Payments;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Payments;

/// <summary>
/// Which part of each refund is the processing fee (payments Phase 4; the owner's Phase 5 decision of
/// 2026-09-26 stores this split). The fee goes back once, only with the money it rode on, and only when
/// the payment froze it as refundable.
/// </summary>
/// <remarks>
/// The default booking is 3 days at 30 JOD: 90 for the booking and an 18 deposit. "Paid in full" with a
/// 4.5 fee charged the card 94.5.
/// </remarks>
public sealed class RefundFeeSplitTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    private static DateTimeOffset AfterFreeWindow(Booking booking) => booking.FreeCancellationDeadline!.Value.AddMinutes(1);

    [Fact]
    public void A_whole_payment_refund_returns_a_refundable_fee_with_the_booking_money()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now).IsSuccess);
        var refund = BookingEndingRefunds.Record(booking, payment, Now)!;

        Assert.Equal(Money.Jod(94.5m), refund.Amount);
        Assert.Equal(Money.Jod(4.5m), payment.FeeInside(refund));
        Assert.Equal(Money.Jod(90m), payment.BookingMoneyIn(refund));
    }

    [Fact]
    public void A_non_refundable_fee_is_never_inside_a_refund()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m, feeRefundable: false);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now).IsSuccess);
        var refund = BookingEndingRefunds.Record(booking, payment, Now)!;

        Assert.Equal(Money.Jod(90m), refund.Amount);
        Assert.True(payment.FeeInside(refund).IsZero);
        Assert.Equal(Money.Jod(90m), payment.BookingMoneyIn(refund));
    }

    [Fact]
    public void The_refund_above_the_deposit_carries_the_refundable_fee()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);
        var refund = BookingEndingRefunds.Record(booking, payment, Now)!;

        Assert.Same(RefundReason.EndedBeforePickup, refund.Reason);
        Assert.Equal(Money.Jod(76.5m), refund.Amount);
        Assert.Equal(Money.Jod(4.5m), payment.FeeInside(refund));
        Assert.Equal(Money.Jod(72m), payment.BookingMoneyIn(refund));
    }

    [Fact]
    public void The_deposits_release_and_a_disputes_share_are_booking_money_alone()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m);
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", Now).IsSuccess);
        BookingEndingRefunds.Record(booking, payment, Now);
        var release = payment.RefundHeldDeposit(Money.Jod(18m), Now.AddDays(3)).Value!;

        var (disputed, disputedPayment) = Build.PaidBooking(inFull: true, fee: 4.5m);
        Assert.True(disputed.Cancel(BookingParty.Customer, disputed.CustomerId, null, AfterFreeWindow(disputed)).IsSuccess);
        BookingEndingRefunds.Record(disputed, disputedPayment, Now);
        var share = disputedPayment.RequestRefund(Money.Jod(9m), Id.New(), Now.AddDays(1)).Value;

        Assert.True(payment.FeeInside(release).IsZero);
        Assert.Equal(Money.Jod(18m), payment.BookingMoneyIn(release));
        Assert.True(disputedPayment.FeeInside(share).IsZero);
        Assert.Equal(Money.Jod(9m), disputedPayment.BookingMoneyIn(share));
    }

    [Fact]
    public void A_deposit_only_payment_has_no_fee_in_any_refund()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now).IsSuccess);
        var refund = BookingEndingRefunds.Record(booking, payment, Now)!;

        Assert.True(payment.FeeInside(refund).IsZero);
        Assert.Equal(Money.Jod(18m), payment.BookingMoneyIn(refund));
    }

    [Fact]
    public void An_orphaned_capture_returns_the_fee_the_attempt_asked_for()
    {
        var booking = Build.ApprovedBooking();
        var payment = Payment.Open(
            booking.Id, booking.CustomerId, Money.Jod(94.5m), "TestProvider", Now.AddMinutes(30), Now,
            PaymentPurpose.FullPayment, Money.Jod(4.5m));
        Assert.True(payment.AttachProviderSession("sess_orphan", "https://provider.test/checkout").IsSuccess);
        Assert.True(payment.Orphan(Money.Jod(94.5m), Now, "booking.not_awaiting_payment", Now).IsSuccess);
        var refund = Assert.Single(payment.Refunds);

        Assert.Equal(Money.Jod(4.5m), payment.FeeInside(refund));
        Assert.Equal(Money.Jod(90m), payment.BookingMoneyIn(refund));
    }

    [Fact]
    public void A_refund_of_another_payment_is_a_programming_error()
    {
        var (booking, payment) = Build.PaidBooking();
        var (other, otherPayment) = Build.PaidBooking();
        Assert.True(other.Cancel(BookingParty.Customer, other.CustomerId, null, Now).IsSuccess);
        var refund = BookingEndingRefunds.Record(other, otherPayment, Now)!;

        Assert.NotEqual(booking.Id, other.Id);
        Assert.Throws<DomainException>(() => payment.FeeInside(refund));
    }
}
