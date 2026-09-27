using Khadra.Application.FinancialDocuments.Composition;
using Khadra.Application.Payments;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;
using Khadra.Tests.Support;

namespace Khadra.Tests.Application.FinancialDocuments;

/// <summary>
/// When a statement gets a new version (owner, 2026-09-27): only when money moved — a closed list of
/// checkpoints, fingerprinted on the facts themselves — and never because the clock moved or a refund was
/// merely recorded, sent or refused.
/// </summary>
public sealed class StatementCheckpointsTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    [Fact]
    public void A_capture_is_the_first_checkpoint()
    {
        var (booking, payment) = Build.PaidBooking();

        var checkpoints = StatementCheckpoints.Of(booking, [payment], []);

        var only = Assert.Single(checkpoints.All);
        Assert.Equal(FinancialDocumentCause.PaymentCaptured, only.Kind);
        Assert.Equal(payment.AppliedAt, only.At);
        Assert.Equal(payment.AppliedAt, checkpoints.CoversThrough);
        Assert.Equal(64, checkpoints.Fingerprint.Length);
    }

    [Fact]
    public void An_unpaid_booking_has_nothing_to_state() =>
        Assert.True(StatementCheckpoints.Of(Build.ApprovedBooking(), [], []).IsEmpty);

    [Fact]
    public void The_clock_alone_never_changes_the_fingerprint()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now).IsSuccess);
        var before = StatementCheckpoints.Of(booking, [payment], []).Fingerprint;

        // Days pass, the dispute window closes: nothing stored changed, so nothing is new.
        var after = StatementCheckpoints.Of(booking, [payment], []).Fingerprint;

        Assert.Equal(before, after);
    }

    [Fact]
    public void A_refund_being_recorded_sent_or_refused_is_not_a_checkpoint_and_its_settlement_is()
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now).IsSuccess);
        var ended = StatementCheckpoints.Of(booking, [payment], []).Fingerprint;

        var refund = BookingEndingRefunds.Record(booking, payment, Now)!;
        Assert.Equal(ended, StatementCheckpoints.Of(booking, [payment], []).Fingerprint);

        refund.MarkSent("rf_1", Now.AddMinutes(1));
        Assert.Equal(ended, StatementCheckpoints.Of(booking, [payment], []).Fingerprint);

        refund.MarkFailed("card_expired", Now.AddMinutes(2));
        Assert.Equal(ended, StatementCheckpoints.Of(booking, [payment], []).Fingerprint);

        refund.MarkSent("rf_2", Now.AddMinutes(3));
        refund.MarkSettled(Now.AddMinutes(4));
        var settled = StatementCheckpoints.Of(booking, [payment], []);
        Assert.NotEqual(ended, settled.Fingerprint);
        Assert.Equal(Now.AddMinutes(4), settled.CoversThrough);
        Assert.Equal(FinancialDocumentCause.RefundSettled, settled.Latest.Kind);
    }

    [Fact]
    public void The_booking_ending_is_a_checkpoint()
    {
        var (booking, payment) = Build.PaidBooking();
        var paid = StatementCheckpoints.Of(booking, [payment], []).Fingerprint;

        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, Now.AddMinutes(5)).IsSuccess);
        var ended = StatementCheckpoints.Of(booking, [payment], []);

        Assert.NotEqual(paid, ended.Fingerprint);
        Assert.Equal(FinancialDocumentCause.BookingEnded, ended.Latest.Kind);
    }

    [Fact]
    public void Cash_recorded_at_a_handover_is_a_checkpoint()
    {
        var (booking, payment) = Build.PaidBooking();
        var paid = StatementCheckpoints.Of(booking, [payment], []).Fingerprint;

        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start, cashCollected: Money.Jod(72m)).IsSuccess);
        var cash = StatementCheckpoints.Of(booking, [payment], []);

        Assert.NotEqual(paid, cash.Fingerprint);
        Assert.Equal(FinancialDocumentCause.CashRecorded, cash.Latest.Kind);
    }

    [Fact]
    public void A_pickup_with_no_cash_is_not_one()
    {
        var (booking, payment) = Build.PaidBooking();
        var paid = StatementCheckpoints.Of(booking, [payment], []).Fingerprint;

        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start).IsSuccess);

        Assert.Equal(paid, StatementCheckpoints.Of(booking, [payment], []).Fingerprint);
    }

    [Fact]
    public void A_resolved_dispute_is_a_checkpoint_and_wins_a_tie_with_the_ending_it_brings_about()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, booking.FreeCancellationDeadline!.Value.AddMinutes(1)).IsSuccess);
        var ended = StatementCheckpoints.Of(booking, [payment], []).Fingerprint;
        var open = DisputeTicket.Open(booking.Id, booking.CustomerId, BookingParty.Customer, "Charged.", TimeSpan.FromHours(48), Now.AddHours(1)).Value;

        // An open ticket decides nothing: not a checkpoint.
        Assert.Equal(ended, StatementCheckpoints.Of(booking, [payment], [open]).Fingerprint);

        var decidedAt = booking.FinishedAt!.Value;
        Assert.True(open.Resolve(DisputeResolution.Create(
            DepositDisposition.Create(Money.Jod(18m), Money.Jod(18m), Money.Jod(0m), Money.Jod(0m)).Value,
            null, null, "Refund.", Id.New(), decidedAt).Value).IsSuccess);
        var resolved = StatementCheckpoints.Of(booking, [payment], [open]);

        Assert.NotEqual(ended, resolved.Fingerprint);
        Assert.Equal(booking.FinishedAt, open.ClosedAt);
        Assert.Equal(FinancialDocumentCause.DisputeResolved, resolved.Latest.Kind);
    }

    [Fact]
    public void A_capture_that_never_applied_still_moved_money()
    {
        var booking = Build.ApprovedBooking();
        var payment = Payment.Open(booking.Id, booking.CustomerId, Money.Jod(18m), "TestProvider", Now.AddMinutes(30), Now, PaymentPurpose.Deposit, Money.Jod(0m), true);
        Assert.True(payment.AttachProviderSession("sess_x", "https://provider.test/checkout").IsSuccess);
        Assert.True(payment.Orphan(Money.Jod(18m), Now, "booking.not_awaiting_payment", Now).IsSuccess);

        var checkpoints = StatementCheckpoints.Of(booking, [payment], []);

        Assert.Equal(FinancialDocumentCause.PaymentCaptured, Assert.Single(checkpoints.All).Kind);
    }

    [Fact]
    public void Another_bookings_money_is_not_this_ones()
    {
        var (booking, _) = Build.PaidBooking();
        var (_, other) = Build.PaidBooking();

        Assert.True(StatementCheckpoints.Of(booking, [other], []).IsEmpty);
    }

    [Fact]
    public void The_fingerprint_does_not_encode_the_offices_or_the_platforms_share()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, booking.FreeCancellationDeadline!.Value.AddMinutes(1)).IsSuccess);
        var ticket = DisputeTicket.Open(booking.Id, booking.CustomerId, BookingParty.Customer, "Charged.", TimeSpan.FromHours(48), Now.AddHours(1)).Value;
        Assert.True(ticket.Resolve(DisputeResolution.Create(
            DepositDisposition.Create(Money.Jod(18m), Money.Jod(13m), Money.Jod(0m), Money.Jod(5m)).Value,
            null, null, "Split.", Id.New(), Now.AddHours(2)).Value).IsSuccess);

        var dispute = StatementCheckpoints.Of(booking, [payment], [ticket]).All.Single(checkpoint => checkpoint.Kind == FinancialDocumentCause.DisputeResolved);

        Assert.Equal(string.Empty, dispute.Facts);
    }
}
