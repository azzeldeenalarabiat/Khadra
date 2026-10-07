using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Payments;

/// <summary>
/// A refused refund waits, is counted, and is never abandoned (Wave 4, B4; checklist 157).
/// </summary>
/// <remarks>
/// It used to be sent again on every sweep, once a minute, with an Error each time, and nothing counted the refusals:
/// <c>MarkFailed</c> overwrote <c>FailedAt</c> on every one. Now each refused SEND is counted — a provider repeating
/// its notice of one refusal is not a second refusal — and sets when the next send may happen.
/// </remarks>
public sealed class RefundBackoffTests
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly RefundRetryPolicy Policy = TestPayments.RetryPolicy;
    private static readonly int[] ShippedWaitsInMinutes = [1, 2, 4, 8, 16, 32, 64, 128, 256, 360, 360];

    private static Refund Owed()
    {
        var payment = Payment.Open(Id.New(), Id.New(), Money.Jod(18m), "TestProvider", Now.AddMinutes(30), Now);
        payment.AttachProviderSession("sess_1", "https://provider.test/sess_1");
        Assert.True(payment.Orphan(Money.Jod(18m), Now, "BookingExpired", Now).IsSuccess);
        return Assert.Single(payment.Refunds);
    }

    [Fact]
    public void The_waits_double_from_the_first_and_stop_at_the_ceiling()
    {
        var policy = new RefundRetryPolicy(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(360), 3);

        Assert.Equal(
            ShippedWaitsInMinutes.Select(minutes => TimeSpan.FromMinutes(minutes)),
            Enumerable.Range(1, 11).Select(policy.DelayAfter));
        // However long a refund has been refused, the wait never overflows past the ceiling.
        Assert.Equal(TimeSpan.FromMinutes(360), policy.DelayAfter(10_000));
        Assert.Equal(TimeSpan.FromMinutes(360), policy.DelayAfter(int.MaxValue));
        Assert.Throws<DomainException>(() => policy.DelayAfter(0));
    }

    [Fact]
    public void A_policy_that_cannot_work_is_refused()
    {
        Assert.Throws<DomainException>(() => new RefundRetryPolicy(TimeSpan.Zero, TimeSpan.FromMinutes(5), 3));
        Assert.Throws<DomainException>(() => new RefundRetryPolicy(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(4), 3));
        Assert.Throws<DomainException>(() => new RefundRetryPolicy(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), 0));
        // A ceiling equal to the first wait is a fixed interval, which is a policy too.
        Assert.Equal(TimeSpan.FromMinutes(5), new RefundRetryPolicy(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), 1).DelayAfter(4));
    }

    [Fact]
    public void A_person_is_needed_from_the_alert_on()
    {
        Assert.False(Policy.NeedsAPerson(0));
        Assert.False(Policy.NeedsAPerson(2));
        Assert.True(Policy.NeedsAPerson(3));
        Assert.True(Policy.NeedsAPerson(40));
    }

    /// <summary>Every send the provider refuses is counted — the re-send of a refund already refused too.</summary>
    [Fact]
    public void Every_refused_send_is_counted_and_sets_the_next_attempt()
    {
        var refund = Owed();

        refund.RecordRefusedSend("card_closed", Now, Policy);
        Assert.Same(RefundStatus.Failed, refund.Status);
        Assert.Equal(1, refund.RefusalCount);
        Assert.Equal(Now, refund.FailedAt);
        Assert.Equal(Now.AddMinutes(1), refund.NextAttemptAt);

        refund.RecordRefusedSend("card_closed", Now.AddMinutes(1), Policy);
        Assert.Equal(2, refund.RefusalCount);
        Assert.Equal(Now.AddMinutes(1), refund.FailedAt);
        Assert.Equal(Now.AddMinutes(3), refund.NextAttemptAt);

        refund.RecordRefusedSend("account_frozen", Now.AddMinutes(3), Policy);
        Assert.Equal(3, refund.RefusalCount);
        Assert.Equal("account_frozen", refund.FailureCode);
        Assert.Equal(Now.AddMinutes(7), refund.NextAttemptAt);
    }

    /// <summary>
    /// A provider repeating its notice of ONE refusal, under a new event id, is the same refusal: nothing changes,
    /// not even when it was refused.
    /// </summary>
    [Fact]
    public void A_refusal_said_again_by_notice_counts_once()
    {
        var refund = Owed();
        refund.MarkSent("rf_1", Now);

        refund.MarkFailed("refund_declined", Now.AddMinutes(1), Policy);
        refund.MarkFailed("refund_declined_again", Now.AddMinutes(2), Policy);

        Assert.Equal(1, refund.RefusalCount);
        Assert.Equal("refund_declined", refund.FailureCode);
        Assert.Equal(Now.AddMinutes(1), refund.FailedAt);
        Assert.Equal(Now.AddMinutes(2), refund.NextAttemptAt);
    }

    /// <summary>
    /// A notice for a refund still recorded as Requested is a refusal of a send the sweep crashed before recording:
    /// it counts.
    /// </summary>
    [Fact]
    public void A_notice_for_a_refund_whose_send_was_never_recorded_counts()
    {
        var refund = Owed();

        refund.MarkFailed("refund_declined", Now, Policy);

        Assert.Equal(1, refund.RefusalCount);
        Assert.Same(RefundStatus.Failed, refund.Status);
    }

    /// <summary>
    /// Sending or settling ends the wait, and keeps the count: "refused 3 times" stays true of a refund that went
    /// through on its fourth send.
    /// </summary>
    [Fact]
    public void Sending_or_settling_ends_the_wait_and_keeps_the_count()
    {
        var refund = Owed();
        refund.RecordRefusedSend("card_closed", Now, Policy);
        refund.RecordRefusedSend("card_closed", Now.AddMinutes(1), Policy);

        refund.MarkSent("rf_1", Now.AddMinutes(3));
        Assert.Null(refund.NextAttemptAt);
        Assert.Null(refund.FailedAt);
        Assert.Equal(2, refund.RefusalCount);

        // A refusal of THAT send, by notice, is a new refusal: Sent → Failed counts.
        refund.MarkFailed("refund_declined", Now.AddMinutes(4), Policy);
        Assert.Equal(3, refund.RefusalCount);
        Assert.Equal(Now.AddMinutes(8), refund.NextAttemptAt);

        refund.MarkSent("rf_1", Now.AddMinutes(8));
        refund.MarkSettled(Now.AddMinutes(9));
        Assert.Null(refund.NextAttemptAt);
        Assert.Equal(3, refund.RefusalCount);
    }

    [Fact]
    public void A_settled_refund_is_never_refused_again()
    {
        var refund = Owed();
        refund.MarkSent("rf_1", Now);
        refund.MarkSettled(Now.AddMinutes(1));

        refund.RecordRefusedSend("card_closed", Now.AddMinutes(2), Policy);
        refund.MarkFailed("refund_declined", Now.AddMinutes(3), Policy);

        Assert.Same(RefundStatus.Settled, refund.Status);
        Assert.Equal(0, refund.RefusalCount);
        Assert.Null(refund.NextAttemptAt);
    }

    [Fact]
    public void A_refund_is_due_while_owed_and_past_its_wait()
    {
        var refund = Owed();
        Assert.True(refund.IsDueToSend(Now));

        refund.RecordRefusedSend("card_closed", Now, Policy);
        Assert.False(refund.IsDueToSend(Now.AddSeconds(59)));
        Assert.True(refund.IsDueToSend(Now.AddMinutes(1)));

        refund.MarkSent("rf_1", Now.AddMinutes(1));
        Assert.False(refund.IsDueToSend(Now.AddDays(1)));

        refund.MarkSettled(Now.AddMinutes(2));
        Assert.False(refund.IsDueToSend(Now.AddDays(1)));
    }
}
