using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Events;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Payments;

/// <summary>
/// One checkout attempt's life, and the two ways it can end with money having moved.
/// </summary>
/// <remarks>
/// The rule the whole context is built on: a capture is a FACT, and the aggregate must resolve every
/// one of them to either Applied or Orphaned. There is no third resting place, and these tests are
/// what stop one being added.
/// </remarks>
public sealed class PaymentLifecycleTests
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly Money Deposit = Money.Jod(18m);

    private static Payment Open(DateTimeOffset? expiresAt = null) =>
        Payment.Open(Id.New(), Id.New(), Deposit, "TestProvider", expiresAt ?? Now.AddMinutes(30), Now);

    private static Payment Pending()
    {
        var payment = Open();
        payment.AttachProviderSession("sess_1", "https://provider.test/sess_1");
        return payment;
    }

    [Fact]
    public void A_new_attempt_is_initiated_and_carries_no_session()
    {
        var payment = Open();

        Assert.Same(PaymentStatus.Initiated, payment.Status);
        Assert.Null(payment.ProviderReference);
        Assert.Null(payment.CheckoutUrl);
        Assert.True(payment.Status.IsLive);
        Assert.True(payment.IsUsable(Now));
    }

    /// <summary>An attempt that cannot be paid is not an attempt; the factory refuses to make one.</summary>
    [Fact]
    public void An_attempt_cannot_open_for_nothing_or_expire_before_it_begins()
    {
        Assert.Throws<DomainException>(() =>
            Payment.Open(Id.New(), Id.New(), Money.Jod(0m), "TestProvider", Now.AddMinutes(30), Now));
        Assert.Throws<DomainException>(() =>
            Payment.Open(Id.New(), Id.New(), Deposit, "TestProvider", Now, Now));
        Assert.Throws<DomainException>(() =>
            Payment.Open(Id.Empty, Id.New(), Deposit, "TestProvider", Now.AddMinutes(30), Now));
    }

    [Fact]
    public void Attaching_a_session_makes_it_pending_and_only_once()
    {
        var payment = Open();

        Assert.True(payment.AttachProviderSession("sess_1", "https://provider.test/sess_1").IsSuccess);
        Assert.Same(PaymentStatus.Pending, payment.Status);

        // A second session on one attempt would be two card forms for one deposit.
        Assert.Equal("payments.not_live", payment.AttachProviderSession("sess_2", "https://x").Error.Code);
    }

    [Fact]
    public void An_expired_attempt_is_no_longer_usable_even_while_it_reads_pending()
    {
        var payment = Pending();

        Assert.True(payment.IsUsable(payment.ExpiresAt.AddTicks(-1)));
        Assert.False(payment.IsUsable(payment.ExpiresAt));
        // The STATUS has not caught up, and must not: only a sweep or the provider closes it, and
        // until one does the row is the record of what was offered.
        Assert.Same(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public void Failing_is_idempotent_so_a_repeated_provider_notice_is_not_an_error()
    {
        var payment = Pending();

        Assert.True(payment.Fail("card_declined", Now).IsSuccess);
        Assert.Same(PaymentStatus.Failed, payment.Status);
        Assert.Equal("card_declined", payment.FailureCode);

        Assert.True(payment.Fail("card_declined", Now.AddMinutes(1)).IsSuccess);
        // The FIRST failure's time stands: a retried notice describes the same event.
        Assert.Equal(Now, payment.FailedAt);
    }

    [Fact]
    public void A_capture_confirms_the_attempt_and_raises_the_event_that_says_so()
    {
        var payment = Pending();

        Assert.True(payment.Apply(Money.Jod(18m), Now, Now.AddSeconds(2)).IsSuccess);

        Assert.Same(PaymentStatus.Applied, payment.Status);
        Assert.True(payment.Status.IsCaptured);
        Assert.Equal(Money.Jod(18m), payment.AmountCaptured);
        Assert.Equal(Now, payment.CapturedAt);
        var applied = Assert.Single(payment.DomainEvents.OfType<PaymentApplied>());
        Assert.Equal(payment.BookingId, applied.BookingId);
        Assert.Empty(payment.Refunds);
    }

    /// <summary>
    /// The amount AND the currency. A provider misconfigured onto another currency would otherwise
    /// take eighteen of something else and confirm a rental against it.
    /// </summary>
    [Theory]
    [InlineData(17.999)]
    [InlineData(18.001)]
    [InlineData(180)]
    public void A_capture_for_the_wrong_amount_is_refused(decimal captured)
    {
        var payment = Pending();

        var applied = payment.Apply(Money.Jod(captured), Now, Now);

        Assert.Equal("payments.amount_mismatch", applied.Error.Code);
        Assert.Same(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public void A_capture_in_the_wrong_currency_is_refused()
    {
        var payment = Pending();

        var applied = payment.Apply(Money.Create(18m, "USD"), Now, Now);

        Assert.Equal("payments.amount_mismatch", applied.Error.Code);
    }

    /// <summary>
    /// An orphan and its refund are ONE act. This is the invariant that stops a customer's money
    /// sitting on the platform with nothing recording that it is owed back.
    /// </summary>
    [Fact]
    public void An_orphaned_capture_creates_the_refund_it_owes_in_the_same_breath()
    {
        var payment = Pending();

        Assert.True(payment.Orphan(Money.Jod(18m), Now, "BookingExpired", Now.AddSeconds(2)).IsSuccess);

        Assert.Same(PaymentStatus.Orphaned, payment.Status);
        Assert.True(payment.Status.IsCaptured);
        Assert.Equal("BookingExpired", payment.OrphanReason);

        var refund = Assert.Single(payment.Refunds);
        Assert.Same(RefundReason.OrphanedCapture, refund.Reason);
        Assert.Same(RefundStatus.Requested, refund.Status);
        Assert.Equal(Money.Jod(18m), refund.Amount);
        // Nobody decided it, so no ticket ordered it.
        Assert.Null(refund.DisputeTicketId);
        Assert.Equal(Money.Jod(18m), payment.RefundedTotal);

        Assert.Single(payment.DomainEvents.OfType<PaymentOrphaned>());
    }

    /// <summary>
    /// The case that makes <see cref="Payment.Orphan"/> deliberately looser than
    /// <see cref="Payment.Apply"/>.
    /// </summary>
    /// <remarks>
    /// A capture for the wrong amount is one of the REASONS a payment is orphaned. Sharing Apply's
    /// guard would have refused to orphan it -- and refusing to orphan a real capture means keeping a
    /// customer's money with nothing on the record saying it is owed. It was written that way first,
    /// and this test is what found it.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(180)]
    public void A_capture_for_the_wrong_amount_can_still_be_orphaned_and_goes_back_in_full(decimal captured)
    {
        var payment = Pending();

        Assert.True(payment.Orphan(Money.Jod(captured), Now, "payments.amount_mismatch", Now).IsSuccess);

        Assert.Same(PaymentStatus.Orphaned, payment.Status);
        // What the provider TOOK, never what this row asked for.
        Assert.Equal(Money.Jod(captured), Assert.Single(payment.Refunds).Amount);
        Assert.Equal(Money.Jod(captured), payment.RefundedTotal);
    }

    /// <summary>The same, in a currency this platform does not price in.</summary>
    [Fact]
    public void A_capture_in_the_wrong_currency_is_orphaned_and_totals_in_the_currency_it_arrived_in()
    {
        var payment = Pending();
        var wrong = Money.Create(18m, "USD");

        Assert.True(payment.Orphan(wrong, Now, "payments.amount_mismatch", Now).IsSuccess);

        // RefundedTotal must not try to add USD to a JOD zero and throw on the very row somebody is
        // investigating.
        Assert.Equal(wrong, payment.RefundedTotal);
    }

    [Fact]
    public void A_captured_payment_refuses_a_second_capture_and_refuses_to_be_failed()
    {
        var payment = Pending();
        payment.Apply(Money.Jod(18m), Now, Now);

        Assert.Equal("payments.already_captured", payment.Apply(Money.Jod(18m), Now, Now).Error.Code);
        Assert.Equal("payments.already_captured", payment.Orphan(Money.Jod(18m), Now, "late", Now).Error.Code);
        // Calling a captured payment failed would be a lie about money.
        Assert.Equal("payments.already_captured", payment.Fail("expired", Now).Error.Code);
    }

    /// <summary>
    /// The guard a caller asks BEFORE it starts changing other aggregates, and it must give the same
    /// answer the capture itself would.
    /// </summary>
    [Fact]
    public void Can_accept_capture_agrees_with_the_capture_it_gates()
    {
        var payment = Pending();

        Assert.True(payment.CanAcceptCapture(Money.Jod(18m)).IsSuccess);
        Assert.Equal("payments.amount_mismatch", payment.CanAcceptCapture(Money.Jod(19m)).Error.Code);

        payment.Apply(Money.Jod(18m), Now, Now);
        Assert.Equal("payments.already_captured", payment.CanAcceptCapture(Money.Jod(18m)).Error.Code);
    }

    [Fact]
    public void A_refund_can_be_requested_against_an_applied_payment_and_never_exceeds_it()
    {
        var payment = Pending();
        payment.Apply(Money.Jod(18m), Now, Now);
        var ticket = Id.New();

        var first = payment.RequestRefund(Money.Jod(12m), ticket, Now);
        Assert.True(first.IsSuccess);
        Assert.Same(RefundReason.DisputeResolution, first.Value.Reason);
        Assert.Equal(ticket, first.Value.DisputeTicketId);

        // Six more is exactly the rest, and is allowed.
        Assert.True(payment.RequestRefund(Money.Jod(6m), ticket, Now).IsSuccess);
        Assert.Equal(Money.Jod(18m), payment.RefundedTotal);

        // A single fils more is not.
        Assert.Equal(
            "payments.refund_exceeds_capture",
            payment.RequestRefund(Money.Jod(0.001m), ticket, Now).Error.Code);
    }

    [Fact]
    public void A_refund_cannot_be_requested_against_money_that_never_arrived()
    {
        var pending = Pending();
        Assert.Equal("payments.not_live", pending.RequestRefund(Money.Jod(1m), null, Now).Error.Code);

        var orphaned = Pending();
        orphaned.Orphan(Money.Jod(18m), Now, "BookingCancelled", Now);
        // An orphan already carries its own full refund; a second one would send the money twice.
        Assert.Equal("payments.not_live", orphaned.RequestRefund(Money.Jod(1m), null, Now).Error.Code);
    }

    /// <summary>
    /// A refused refund still counts as owed, so the ceiling does not quietly rise when a provider
    /// says no and an admin tries again.
    /// </summary>
    [Fact]
    public void A_failed_refund_is_still_owed()
    {
        var payment = Pending();
        payment.Apply(Money.Jod(18m), Now, Now);
        var refund = payment.RequestRefund(Money.Jod(18m), null, Now).Value;

        refund.MarkSent("rf_1", Now);
        Assert.Equal(Money.Jod(18m), payment.RefundedTotal);

        refund.MarkFailed("insufficient_funds", Now.AddMinutes(1));
        Assert.Same(RefundStatus.Failed, refund.Status);
        // It left the outstanding total, which is what lets the sweep pick it up again -- and what
        // stops a retry being refused as if the money had already gone back.
        Assert.Equal(Money.Jod(0m), payment.RefundedTotal);

        refund.MarkSent("rf_2", Now.AddMinutes(2));
        Assert.Null(refund.FailureCode);
        // The FIRST send's time stands: a retry is the same instruction, not a new one.
        Assert.Equal(Now, refund.SentAt);
    }

    [Fact]
    public void A_settled_refund_cannot_be_undone_by_a_late_notice()
    {
        var payment = Pending();
        payment.Apply(Money.Jod(18m), Now, Now);
        var refund = payment.RequestRefund(Money.Jod(18m), null, Now).Value;
        refund.MarkSent("rf_1", Now);
        refund.MarkSettled(Now.AddMinutes(1));

        refund.MarkFailed("whatever", Now.AddMinutes(2));

        Assert.Same(RefundStatus.Settled, refund.Status);
    }
}
