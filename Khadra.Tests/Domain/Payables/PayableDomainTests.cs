using Khadra.Domain.Common;
using Khadra.Domain.Payables;
using Khadra.Domain.Payments;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Payables;

/// <summary>
/// The office payables ledger's own rules (payments Phase 8): a payable's figures are derived from its lines and
/// frozen, a settlement closes open payables of one office, currency and kind of money at their net, and holds say
/// why a payable is held back.
/// </summary>
public sealed class PayableDomainTests
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly DateOnly Today = Build.AmmanDate(Now);

    private static PayableDraft Draft(
        PayableOutcome? outcome = null,
        IReadOnlyList<PayableLineDraft>? lines = null,
        Id? dealerId = null,
        string currency = "JOD",
        string provider = PaymentProviders.Sandbox) =>
        new(
            Id.New(),
            dealerId ?? Id.New(),
            "KH-ABCD1234",
            currency,
            provider,
            outcome ?? PayableOutcome.Rental,
            Now.AddDays(-1),
            2,
            lines ?? [new(PayableLineKind.RentalRevenue, 18m), new(PayableLineKind.Commission, 6m)]);

    // ── A payable ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_payables_figures_are_derived_from_its_lines()
    {
        var ticket = Id.New();
        var payable = OfficePayable.Record(
            Draft(PayableOutcome.RentalAfterDispute, [
                new(PayableLineKind.RentalRevenue, 72m),
                new(PayableLineKind.DisputeShare, 7m, ticket),
                new(PayableLineKind.Commission, 6m),
                new(PayableLineKind.DisputeCharge, 100m, ticket),
            ]),
            Now);

        Assert.Equal(79m, payable.OfficeMoney);
        Assert.Equal(6m, payable.Commission);
        Assert.Equal(100m, payable.OfficeCharges);
        // Signed: the office owes 27.
        Assert.Equal(-27m, payable.Net);
        Assert.Equal([1, 2, 3, 4], payable.Lines.Select(line => line.Position));
        Assert.Equal([72m, 7m, -6m, -100m], payable.Lines.Select(line => line.SignedAmount));
        Assert.Equal(Now, payable.RecordedAt);
        Assert.False(payable.IsSettled);
        Assert.True(payable.IsTest);
    }

    [Fact]
    public void A_net_zero_payable_is_recorded_with_no_lines()
    {
        var payable = OfficePayable.Record(Draft(PayableOutcome.PaymentReturned, []), Now);

        Assert.Empty(payable.Lines);
        Assert.Equal(0m, payable.Net);
        Assert.False(OfficePayable.Record(Draft(provider: "Tap"), Now).IsTest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.0001)]
    public void A_line_is_a_positive_amount_the_ledger_can_hold(decimal amount) =>
        Assert.Throws<DomainException>(() => OfficePayable.Record(Draft(lines: [new(PayableLineKind.RentalRevenue, amount)]), Now));

    [Fact]
    public void Khadras_commission_is_never_more_than_the_offices_money() =>
        Assert.Throws<DomainException>(() => OfficePayable.Record(
            Draft(lines: [new(PayableLineKind.RentalRevenue, 4m), new(PayableLineKind.Commission, 6m)]),
            Now));

    [Fact]
    public void A_payable_carries_one_commission() =>
        Assert.Throws<DomainException>(() => OfficePayable.Record(
            Draft(lines: [new(PayableLineKind.RentalRevenue, 18m), new(PayableLineKind.Commission, 3m), new(PayableLineKind.Commission, 3m)]),
            Now));

    [Fact]
    public void An_outcome_that_never_reached_the_office_carries_no_office_money_but_may_carry_a_charge()
    {
        Assert.Throws<DomainException>(() => OfficePayable.Record(
            Draft(PayableOutcome.DepositReleased, [new(PayableLineKind.PenaltyKept, 18m)]),
            Now));

        var charged = OfficePayable.Record(
            Draft(PayableOutcome.PaymentReturned, [new(PayableLineKind.DisputeCharge, 5m, Id.New())]),
            Now);
        Assert.Equal(-5m, charged.Net);
    }

    [Fact]
    public void A_disputes_line_names_its_ticket() =>
        Assert.Throws<DomainException>(() => OfficePayable.Record(
            Draft(PayableOutcome.DisputeDecided, [new(PayableLineKind.DisputeShare, 9m)]),
            Now));

    [Fact]
    public void A_payable_is_settled_once_and_opened_again_only_by_the_settlement_that_closed_it()
    {
        var payable = OfficePayable.Record(Draft(), Now);
        var settlement = Id.New();

        Assert.True(payable.SettleUnder(settlement, Now).IsSuccess);
        Assert.Equal(PayableErrors.AlreadySettled, payable.SettleUnder(Id.New(), Now).Error);
        Assert.Throws<DomainException>(() => payable.ReopenAfterVoid(Id.New()));

        payable.ReopenAfterVoid(settlement);

        Assert.False(payable.IsSettled);
        Assert.Null(payable.SettledAt);
    }

    // ── A settlement ────────────────────────────────────────────────────────────────────────────────

    private static List<OfficePayable> Open(Id dealerId, params decimal[] nets) =>
        nets.Select(net => OfficePayable.Record(
                Draft(
                    net >= 0 ? PayableOutcome.Rental : PayableOutcome.DisputeDecided,
                    net >= 0
                        ? [new(PayableLineKind.RentalRevenue, net + 1m), new(PayableLineKind.Commission, 1m)]
                        : [new(PayableLineKind.DisputeCharge, -net, Id.New())],
                    dealerId),
                Now))
            .ToList();

    [Fact]
    public void A_settlement_closes_every_payable_at_its_net_and_its_sign_is_its_direction()
    {
        var dealerId = Id.New();
        var payables = Open(dealerId, 12m, 30m, -10m);

        var settlement = OfficeSettlement.Record(
            Id.New(), "TEST-SET-2026-000001", dealerId, payables, Today, Today, "  TRX-99  ", " ", Id.New(), Now).Value;

        Assert.Equal(32m, settlement.Amount);
        Assert.Same(SettlementDirection.Payout, settlement.Direction);
        Assert.Equal("TRX-99", settlement.Reference);
        Assert.Null(settlement.Note);
        Assert.True(settlement.IsTest);
        Assert.Equal(payables.Select(payable => (payable.Id, payable.Net)), settlement.Lines.Select(line => (line.PayableId, line.Net)));
    }

    [Fact]
    public void An_office_that_owes_more_than_it_is_owed_pays_khadra_and_an_even_balance_moves_nothing()
    {
        var dealerId = Id.New();

        var received = OfficeSettlement.Record(Id.New(), "SET-2026-000001", dealerId, Open(dealerId, 5m, -20m), Today, Today, null, null, Id.New(), Now).Value;
        var netted = OfficeSettlement.Record(Id.New(), "SET-2026-000002", dealerId, Open(dealerId, 10m, -10m), Today, Today, null, null, Id.New(), Now).Value;

        Assert.Same(SettlementDirection.Received, received.Direction);
        Assert.Equal(-15m, received.Amount);
        Assert.Same(SettlementDirection.Netted, netted.Direction);
        Assert.Equal(0m, netted.Amount);
    }

    [Fact]
    public void A_settlement_is_refused_for_nothing_a_future_day_or_a_payable_already_settled()
    {
        var dealerId = Id.New();
        var settled = Open(dealerId, 12m);
        settled[0].SettleUnder(Id.New(), Now);

        Assert.Equal(PayableErrors.NothingDue, OfficeSettlement.Record(Id.New(), "SET-2026-000001", dealerId, [], Today, Today, null, null, Id.New(), Now).Error);
        Assert.Equal(
            PayableErrors.PaidOnInFuture,
            OfficeSettlement.Record(Id.New(), "SET-2026-000001", dealerId, Open(dealerId, 12m), Today.AddDays(1), Today, null, null, Id.New(), Now).Error);
        Assert.Equal(
            PayableErrors.AlreadySettled,
            OfficeSettlement.Record(Id.New(), "SET-2026-000001", dealerId, settled, Today, Today, null, null, Id.New(), Now).Error);
    }

    [Fact]
    public void A_settlement_never_mixes_offices_currencies_or_kinds_of_money()
    {
        var dealerId = Id.New();
        var other = OfficePayable.Record(Draft(dealerId: Id.New()), Now);
        var dollars = OfficePayable.Record(Draft(dealerId: dealerId, currency: "USD"), Now);
        var real = OfficePayable.Record(Draft(dealerId: dealerId, provider: "Tap"), Now);

        foreach (var stranger in new[] { other, dollars, real })
        {
            Assert.Throws<DomainException>(() => OfficeSettlement.Record(
                Id.New(), "SET-2026-000001", dealerId, [.. Open(dealerId, 12m), stranger], Today, Today, null, null, Id.New(), Now));
        }
    }

    [Fact]
    public void A_settlements_number_is_its_own_series_per_year_and_kind_of_money()
    {
        Assert.Equal("SET-2026", OfficeSettlement.SeriesKey(2026, isTest: false));
        Assert.Equal("TEST-SET-2026", OfficeSettlement.SeriesKey(2026, isTest: true));
    }

    [Fact]
    public void A_void_needs_a_reason_of_a_readable_length()
    {
        Assert.Equal(PayableErrors.VoidReasonRequired, OfficeSettlementVoid.Record(Id.New(), Id.New(), "  ", Now).Error);
        Assert.Equal(PayableErrors.VoidReasonTooLong, OfficeSettlementVoid.Record(Id.New(), Id.New(), new string('x', 501), Now).Error);

        var settlementId = Id.New();
        var voided = OfficeSettlementVoid.Record(settlementId, Id.New(), " Wrong office. ", Now).Value;
        Assert.Equal(settlementId, voided.SettlementId);
        Assert.Equal("Wrong office.", voided.Reason);
    }

    // ── Holds ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_system_holds_a_booking_before_recording_and_a_payable_after()
    {
        var payable = OfficePayable.Record(Draft(), Now);

        var review = OfficePayableHold.OpenBySystem(PayableHoldReason.NeedsReview, payable.BookingId, payable.DealerId, null, "RefundWithoutCause", Now, Now.AddMinutes(5));
        var contradicted = OfficePayableHold.OpenBySystem(PayableHoldReason.Contradicted, payable.BookingId, payable.DealerId, payable.Id, "the net is now 3", Now, Now.AddMinutes(5));

        Assert.True(review.IsOpen);
        Assert.Null(review.PayableId);
        Assert.Equal(1, review.Checks);
        Assert.Equal(payable.Id, contradicted.PayableId);
        Assert.Throws<DomainException>(() => OfficePayableHold.OpenBySystem(PayableHoldReason.NeedsReview, payable.BookingId, payable.DealerId, payable.Id, null, Now, Now));
        Assert.Throws<DomainException>(() => OfficePayableHold.OpenBySystem(PayableHoldReason.Contradicted, payable.BookingId, payable.DealerId, null, null, Now, Now));
        Assert.Throws<DomainException>(() => OfficePayableHold.OpenBySystem(PayableHoldReason.Manual, payable.BookingId, payable.DealerId, payable.Id, null, Now, Now));
    }

    [Fact]
    public void A_system_hold_is_checked_again_and_released_by_the_system_only()
    {
        var hold = OfficePayableHold.OpenBySystem(PayableHoldReason.NeedsReview, Id.New(), Id.New(), null, "a", Now, Now.AddMinutes(5));

        hold.CheckedAgain("b", Now.AddMinutes(5), Now.AddMinutes(15));
        Assert.Equal(2, hold.Checks);
        Assert.Equal("b", hold.Detail);
        Assert.Equal(PayableErrors.NotHeld, hold.ReleaseByAdmin(Id.New(), null, Now).Error);

        hold.ReleaseBySystem(Now.AddMinutes(20));
        Assert.False(hold.IsOpen);
        Assert.Null(hold.NextCheckAt);
        Assert.Throws<DomainException>(() => hold.CheckedAgain("c", Now, Now));
    }

    [Fact]
    public void An_administrator_holds_an_open_payable_with_a_reason_and_releases_it()
    {
        var payable = OfficePayable.Record(Draft(), Now);
        var admin = Id.New();

        Assert.Equal(PayableErrors.HoldReasonRequired, OfficePayableHold.OpenByAdmin(payable, admin, " ", Now).Error);
        Assert.Equal(PayableErrors.HoldReasonTooLong, OfficePayableHold.OpenByAdmin(payable, admin, new string('x', 501), Now).Error);

        var hold = OfficePayableHold.OpenByAdmin(payable, admin, " Bank details unconfirmed. ", Now).Value;
        Assert.Same(PayableHoldReason.Manual, hold.Reason);
        Assert.Equal("Bank details unconfirmed.", hold.Detail);
        Assert.Equal(admin, hold.OpenedByAdminId);
        Assert.Null(hold.NextCheckAt);
        Assert.Throws<DomainException>(() => hold.ReleaseBySystem(Now));

        Assert.True(hold.ReleaseByAdmin(admin, " Confirmed. ", Now.AddDays(1)).IsSuccess);
        Assert.Equal("Confirmed.", hold.ReleaseNote);
        Assert.Equal(PayableErrors.NotHeld, hold.ReleaseByAdmin(admin, null, Now.AddDays(2)).Error);
    }

    [Fact]
    public void A_settled_payable_cannot_be_held()
    {
        var payable = OfficePayable.Record(Draft(), Now);
        payable.SettleUnder(Id.New(), Now);

        Assert.Equal(PayableErrors.AlreadySettled, OfficePayableHold.OpenByAdmin(payable, Id.New(), "Why", Now).Error);
    }

    [Fact]
    public void A_balance_changed_refusal_carries_the_balance_due_now()
    {
        var error = PayableErrors.BalanceChanged(-15.5m, "JOD");

        Assert.Equal("payables.balance_changed", error.Code);
        Assert.Equal(ErrorKind.Conflict, error.Kind);
        var current = Assert.IsType<Dictionary<string, object?>>(error.Extensions!["currentAmount"]);
        Assert.Equal(-15.5m, current["amount"]);
        Assert.Equal("JOD", current["currency"]);
    }
}
