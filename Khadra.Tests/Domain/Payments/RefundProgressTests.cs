using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Payments;

/// <summary>
/// Where a payment's refunds stand, as ONE verdict on the payment (payments Phase 4b, the advisor's
/// condition for the administrator's payments list): the booking's financial state and the list both
/// read <see cref="Payment.RefundProgress"/>, so a payment can never read one way on a booking and
/// another in the list.
/// </summary>
public sealed class RefundProgressTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    private static void Settle(Refund refund)
    {
        refund.MarkSent("rf_" + refund.Id.Value.ToString("N"), Now);
        refund.MarkSettled(Now);
    }

    [Fact]
    public void The_names_are_the_codes_every_client_keys_on()
    {
        Assert.Equal(
            ["None", "InProgress", "Delayed", "Partial", "Complete"],
            new[] { RefundProgress.None, RefundProgress.InProgress, RefundProgress.Delayed, RefundProgress.Partial, RefundProgress.Complete }
                .Select(progress => progress.Name));
    }

    [Fact]
    public void A_payment_nothing_went_back_from_reads_none()
    {
        var (_, payment) = Build.PaidBooking();

        Assert.Same(RefundProgress.None, payment.RefundProgress);
        Assert.False(payment.IsWhollyReturned);
    }

    [Fact]
    public void A_refund_on_its_way_reads_in_progress_and_a_refused_one_outranks_it()
    {
        var (_, payment) = Build.PaidBooking(inFull: true);
        var aboveDeposit = payment.RefundAboveDeposit(Money.Jod(72m), Now).Value!;

        Assert.Same(RefundProgress.InProgress, payment.RefundProgress);

        aboveDeposit.MarkFailed("card_closed", Now.AddMinutes(1));
        Assert.Same(RefundProgress.Delayed, payment.RefundProgress);
    }

    [Fact]
    public void Part_of_a_payment_back_reads_partial_and_all_of_it_complete()
    {
        var (_, payment) = Build.PaidBooking(inFull: true);
        Settle(payment.RefundAboveDeposit(Money.Jod(72m), Now).Value!);

        Assert.Same(RefundProgress.Partial, payment.RefundProgress);
        Assert.False(payment.IsWhollyReturned);

        Settle(payment.RefundHeldDeposit(Money.Jod(18m), Now.AddDays(3)).Value!);

        Assert.Same(RefundProgress.Complete, payment.RefundProgress);
        Assert.True(payment.IsWhollyReturned);
    }

    [Fact]
    public void A_capture_that_never_applied_is_complete_only_once_all_of_it_is_back()
    {
        var booking = Build.ApprovedBooking();
        var late = Payment.Open(booking.Id, booking.CustomerId, Money.Jod(18m), "TestProvider", Now.AddMinutes(30), Now);
        Assert.True(late.AttachProviderSession("sess_orphan", "https://provider.test/checkout").IsSuccess);
        Assert.True(late.Orphan(Money.Jod(18m), Now, "booking.not_awaiting_payment", Now).IsSuccess);

        Assert.Same(RefundProgress.InProgress, late.RefundProgress);

        Settle(Assert.Single(late.Refunds));

        Assert.True(late.IsWhollyReturned);
        Assert.Same(RefundProgress.Complete, late.RefundProgress);
    }

    [Fact]
    public void A_reader_shown_only_some_refunds_is_judged_by_those_and_by_what_it_says_is_complete()
    {
        var (_, payment) = Build.PaidBooking(inFull: true);
        Settle(payment.RefundAboveDeposit(Money.Jod(72m), Now).Value!);
        var share = payment.RequestRefund(Money.Jod(9m), Id.New(), Now.AddHours(1)).Value;

        var shown = payment.Refunds.Where(refund => refund.Id != share.Id);

        Assert.Same(RefundProgress.InProgress, payment.RefundProgress);
        Assert.Same(RefundProgress.Partial, RefundProgress.Of(shown, complete: false));
        Assert.Same(RefundProgress.Complete, RefundProgress.Of(shown, complete: true));
        Assert.Same(RefundProgress.None, RefundProgress.Of([], complete: true));
    }
}
