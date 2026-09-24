using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Payments;

/// <summary>
/// The refund a free cancellation owes on a paid deposit (owner, 2026-09-24): the whole capture, once.
/// </summary>
public sealed class FreeCancellationRefundTests
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly Money Deposit = Money.Jod(40m);

    private static Payment Applied()
    {
        var payment = Payment.Open(Id.New(), Id.New(), Deposit, "TestProvider", Now.AddMinutes(30), Now);
        payment.AttachProviderSession("sess_1", "https://provider.test/sess_1");
        Assert.True(payment.Apply(Money.Jod(40m), Now, Now).IsSuccess);
        return payment;
    }

    [Fact]
    public void An_applied_payment_records_one_refund_of_the_whole_capture()
    {
        var payment = Applied();

        var refund = payment.RefundForFreeCancellation(Now).Value;

        Assert.Same(RefundReason.FreeCancellation, refund.Reason);
        Assert.Same(RefundStatus.Requested, refund.Status);
        Assert.Equal(Money.Jod(40m), refund.Amount);
        Assert.Null(refund.DisputeTicketId);
        Assert.Single(payment.Refunds);
        Assert.Equal(Money.Jod(40m), payment.RefundedTotal);
        Assert.Same(refund, payment.FreeCancellationRefund);
    }

    /// <summary>A second instruction for one deposit is the failure this guards: whatever became of the first.</summary>
    [Fact]
    public void Asking_again_returns_the_same_refund_in_every_state_it_can_be_in()
    {
        var payment = Applied();
        var first = payment.RefundForFreeCancellation(Now).Value;

        Assert.Same(first, payment.RefundForFreeCancellation(Now.AddSeconds(1)).Value);

        first.MarkSent("rf_1", Now);
        Assert.Same(first, payment.RefundForFreeCancellation(Now.AddSeconds(2)).Value);

        first.MarkFailed("refund_declined", Now);
        Assert.Same(first, payment.RefundForFreeCancellation(Now.AddSeconds(3)).Value);

        first.MarkSettled(Now);
        Assert.Same(first, payment.RefundForFreeCancellation(Now.AddSeconds(4)).Value);

        Assert.Single(payment.Refunds);
    }

    [Fact]
    public void Money_that_never_arrived_or_was_already_going_back_cannot_be_refunded_again()
    {
        var initiated = Payment.Open(Id.New(), Id.New(), Deposit, "TestProvider", Now.AddMinutes(30), Now);
        Assert.Equal("payments.not_live", initiated.RefundForFreeCancellation(Now).Error.Code);

        var failed = Payment.Open(Id.New(), Id.New(), Deposit, "TestProvider", Now.AddMinutes(30), Now);
        failed.Fail("card_declined", Now);
        Assert.Equal("payments.not_live", failed.RefundForFreeCancellation(Now).Error.Code);

        // An orphan already carries the refund of everything it took; it is not a deposit a booking holds.
        var orphaned = Payment.Open(Id.New(), Id.New(), Deposit, "TestProvider", Now.AddMinutes(30), Now);
        orphaned.Orphan(Money.Jod(40m), Now, "BookingCancelled", Now);
        Assert.Equal("payments.not_live", orphaned.RefundForFreeCancellation(Now).Error.Code);
        Assert.Single(orphaned.Refunds);
    }

    /// <summary>
    /// The full capture, never "what is left": no refund can precede the cancellation, so one that
    /// did is a bug, and refunding the remainder would hide it.
    /// </summary>
    [Fact]
    public void A_payment_already_partly_refunded_is_refused_rather_than_topped_up()
    {
        var payment = Applied();
        Assert.True(payment.RequestRefund(Money.Jod(10m), disputeTicketId: null, Now).IsSuccess);

        Assert.Equal("payments.refund_exceeds_capture", payment.RefundForFreeCancellation(Now).Error.Code);
        Assert.Single(payment.Refunds);
    }
}
