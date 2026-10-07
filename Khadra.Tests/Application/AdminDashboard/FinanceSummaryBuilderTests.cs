using Khadra.Application.AdminDashboard;
using Khadra.Application.Payments.ReadModels;

namespace Khadra.Tests.Application.AdminDashboard;

/// <summary>
/// "Money in motion" (payments Phase 4b): every definition on the panel, pinned. The month's figures
/// are flows counted by their own event dates; the right-now figures are the stock of refunds owed
/// back; money in another currency is listed apart and never added to a figure in the platform's.
/// </summary>
public sealed class FinanceSummaryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 10, 1);

    private static RefundFact Refund(decimal amount, string status, string reason = "EndedBeforePickup", string currency = "JOD") =>
        new(amount, currency, status, reason);

    [Fact]
    public void Nothing_moved_reads_as_zero_in_the_platforms_currency_not_as_nothing()
    {
        var summary = FinanceSummaryBuilder.Build(new FinanceFacts([], [], []), "JOD", "Sandbox", From, To, Now);

        Assert.Equal("Sandbox", summary.PaymentMode);
        Assert.Equal("JOD", summary.Currency);
        Assert.Equal(From, summary.ThisMonth.From);
        Assert.Equal(To, summary.ThisMonth.To);
        Assert.Equal(0m, summary.ThisMonth.AppliedToBookings.Amount);
        Assert.Equal("JOD", summary.ThisMonth.AppliedToBookings.Currency);
        Assert.Equal(0, summary.ThisMonth.PaymentsApplied);
        Assert.Equal(0m, summary.RightNow.RefundsFailed.Amount);
        Assert.Empty(summary.OtherCurrencies);
        Assert.Equal(Now, summary.GeneratedAt);
    }

    [Fact]
    public void The_month_counts_booking_money_and_fees_charged_apart_and_the_refunds_it_settled()
    {
        var facts = new FinanceFacts(
            [new AppliedPaymentFact(90m, 4.5m, "JOD"), new AppliedPaymentFact(18m, 0m, "JOD")],
            [Refund(72m, "Settled"), Refund(18m, "Settled", "DisputeWindowClosed")],
            []);

        var month = FinanceSummaryBuilder.Build(facts, "JOD", "Sandbox", From, To, Now).ThisMonth;

        Assert.Equal(108m, month.AppliedToBookings.Amount);
        Assert.Equal(2, month.PaymentsApplied);
        Assert.Equal(4.5m, month.ProcessingFeesCharged.Amount);
        Assert.Equal(90m, month.RefundsSettled.Amount);
        Assert.Equal(2, month.RefundsSettledCount);
    }

    [Fact]
    public void Right_now_splits_refused_refunds_from_the_ones_on_their_way_and_orphans_are_a_part_of_them()
    {
        var facts = new FinanceFacts(
            [],
            [],
            [
                Refund(72m, "Requested"),
                Refund(9m, "Sent", "DisputeResolution"),
                Refund(18m, "Failed", "OrphanedCapture"),
                Refund(20m, "Sent", "OrphanedCapture"),
            ]);

        var now = FinanceSummaryBuilder.Build(facts, "JOD", "Sandbox", From, To, Now).RightNow;

        Assert.Equal(101m, now.RefundsInProgress.Amount);
        Assert.Equal(3, now.RefundsInProgressCount);
        Assert.Equal(18m, now.RefundsFailed.Amount);
        Assert.Equal(1, now.RefundsFailedCount);
        // A PART of the two above — one refused, one on its way — never added to them.
        Assert.Equal(38m, now.OrphanedCapturesOwed.Amount);
        Assert.Equal(2, now.OrphanedCapturesOwedCount);
    }

    /// <summary>
    /// Open capture incidents are a COUNT on the panel (Wave 4, B1), never money in any figure: what that money is —
    /// a second charge to return, a contradiction to reconcile — a person decides at the provider.
    /// </summary>
    [Fact]
    public void Open_capture_incidents_are_counted_and_never_added_to_any_figure()
    {
        var facts = new FinanceFacts([], [], [Refund(18m, "Sent", "OrphanedCapture")], OpenCaptureIncidents: 2);

        var now = FinanceSummaryBuilder.Build(facts, "JOD", "Sandbox", From, To, Now).RightNow;

        Assert.Equal(2, now.OpenCaptureIncidentsCount);
        Assert.Equal(18m, now.RefundsInProgress.Amount);
        Assert.Equal(1, now.RefundsInProgressCount);
        Assert.Equal(18m, now.OrphanedCapturesOwed.Amount);
        Assert.Equal(0, FinanceSummaryBuilder.Build(new FinanceFacts([], [], []), "JOD", "Sandbox", From, To, Now).RightNow.OpenCaptureIncidentsCount);
    }

    [Fact]
    public void A_capture_in_another_currency_is_listed_apart_and_never_summed()
    {
        var facts = new FinanceFacts(
            [new AppliedPaymentFact(90m, 0m, "JOD")],
            [Refund(25.4m, "Settled", "OrphanedCapture", "USD")],
            [Refund(12m, "Sent", "OrphanedCapture", "USD"), Refund(18m, "Sent", "OrphanedCapture")]);

        var summary = FinanceSummaryBuilder.Build(facts, "JOD", "Sandbox", From, To, Now);

        Assert.Equal(0m, summary.ThisMonth.RefundsSettled.Amount);
        Assert.Equal(18m, summary.RightNow.RefundsInProgress.Amount);
        Assert.Equal(18m, summary.RightNow.OrphanedCapturesOwed.Amount);
        var usd = Assert.Single(summary.OtherCurrencies);
        Assert.Equal("USD", usd.Currency);
        Assert.Equal(25.4m, usd.RefundsSettledThisMonth.Amount);
        Assert.Equal(12m, usd.RefundsOutstanding.Amount);
        Assert.Equal("USD", usd.RefundsOutstanding.Currency);
    }
}
