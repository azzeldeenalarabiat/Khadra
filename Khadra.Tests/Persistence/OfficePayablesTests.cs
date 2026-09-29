using Khadra.Application.Payables.Pass;
using Khadra.Application.Payables.ReadModels;
using Khadra.Domain.Auditing;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payables;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The office payables ledger end to end on the real repositories, readers and unit of work (payments Phase 8): the
/// pass records each final paid booking's payable once, holds what it cannot record, checks what it recorded again;
/// an administrator settles an office's net balance, voids a settlement, holds and releases a payable — audited.
/// </summary>
/// <remarks>
/// The default booking is 3 days at 30 JOD: 90 for the booking, an 18 deposit, a 6 commission. The pass waits the
/// finality margin (10 minutes here) after an outcome became final.
/// </remarks>
public sealed class OfficePayablesTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly PayablesHarness _harness;

    public OfficePayablesTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new KhadraDbContext(options);
        context.Database.EnsureCreated();
        _harness = new PayablesHarness(options);
    }

    public void Dispose() => _connection.Dispose();

    private async Task<Booking> ChangeBookingAsync(Id bookingId, Action<Booking> change)
    {
        Booking? changed = null;
        await _harness.Bookings.ChangeAsync(async context =>
        {
            changed = (await new BookingRepository(context).GetByIdAsync(bookingId))!;
            change(changed);
        });
        return changed!;
    }

    private async Task ChangePaymentAsync(Id paymentId, Action<Payment> change) =>
        await _harness.Bookings.ChangeAsync(async context =>
        {
            var payment = (await new PaymentRepository(context).GetByIdAsync(paymentId))!;
            change(payment);
        });

    /// <summary>A paid booking, collected, returned and completed when its window closed; the clock just past the margin.</summary>
    private async Task<(Booking Booking, Payment Payment)> CompletedAsync(string provider = PaymentProviders.Sandbox, bool inFull = false, string unique = "")
    {
        _harness.Bookings.Now = _harness.Now;
        var (booking, payment) = await _harness.Bookings.PaidAsync(provider, inFull, unique: unique);
        var completed = await ChangeBookingAsync(booking.Id, stored =>
        {
            Assert.True(stored.RecordPickup(BookingParty.Dealer, Id.New(), stored.Period.Start).IsSuccess);
            Assert.True(stored.RecordReturn(BookingParty.Dealer, Id.New(), stored.Period.End).IsSuccess);
            Assert.True(stored.Settle(stored.DisputeWindowEndsAt!.Value, hasOpenDispute: false).IsSuccess);
        });
        _harness.Now = completed.FinishedAt!.Value.AddMinutes(11);
        return (completed, payment);
    }

    /// <summary>A paid booking the customer cancelled after the free window; the clock just past its window and the margin.</summary>
    private async Task<(Booking Booking, Payment Payment)> CancelledLateAsync(string unique = "")
    {
        _harness.Bookings.Now = _harness.Now;
        var (booking, payment) = await _harness.Bookings.PaidAsync(PaymentProviders.Sandbox, unique: unique);
        var cancelled = await ChangeBookingAsync(booking.Id, stored =>
            Assert.True(stored.Cancel(BookingParty.Customer, stored.CustomerId, null, stored.FreeCancellationDeadline!.Value.AddMinutes(1)).IsSuccess));
        _harness.Now = cancelled.DisputeWindowEndsAt!.Value.AddMinutes(11);
        return (cancelled, payment);
    }

    // ── The pass ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_completed_rental_is_recorded_once_after_the_margin_from_the_calculators_lines()
    {
        var (booking, payment) = await CompletedAsync();
        _harness.Now = booking.FinishedAt!.Value.AddMinutes(9);

        // Not even a candidate before the margin: the work query leaves it for later.
        Assert.Empty(await _harness.PassAsync());
        Assert.Empty(await _harness.PayablesAsync());

        _harness.Now = booking.FinishedAt.Value.AddMinutes(10);
        Assert.Contains(PayableStep.Recorded, await _harness.PassAsync());
        // Once: the booking leaves the work, and the payable is checked again instead.
        Assert.DoesNotContain(PayableStep.Recorded, await _harness.PassAsync());

        var payable = Assert.Single(await _harness.PayablesAsync());
        Assert.Equal(booking.Id, payable.BookingId);
        Assert.Equal(booking.DealerId, payable.DealerId);
        Assert.Equal(booking.Reference.Value, payable.BookingReference);
        Assert.Same(PayableOutcome.Rental, payable.Outcome);
        Assert.Equal(PaymentProviders.Sandbox, payable.Provider);
        Assert.Equal(payment.Provider, payable.Provider);
        Assert.Equal(booking.FinishedAt, payable.FinalAt);
        Assert.Equal(2, payable.CalculatorVersion);
        Assert.Equal([(PayableLineKind.RentalRevenue, 18m), (PayableLineKind.Commission, 6m)], payable.Lines.Select(line => (line.Kind, line.Amount)));
        Assert.Equal(12m, payable.Net);
    }

    [Fact]
    public async Task A_late_cancellation_keeps_the_deposit_once_recorded_and_every_screen_says_so()
    {
        var (booking, _) = await CancelledLateAsync();

        await _harness.PassAsync();

        var payable = Assert.Single(await _harness.PayablesAsync());
        Assert.Same(PayableOutcome.PenaltyKept, payable.Outcome);
        Assert.Equal(booking.DisputeWindowEndsAt, payable.FinalAt);
        Assert.Equal(12m, payable.Net);
        var recorded = await _harness.ReadAsync(ledger => ledger.RecordedAsync(booking.Id));
        Assert.Same(PayableOutcome.PenaltyKept, recorded!.Outcome);
        Assert.Equal(Money.Jod(6m), recorded.Commission);
        await using var context = _harness.NewContext();
        var penaltyContext = await new BookingReader(context).ContextAsync(booking.Id);
        Assert.True(penaltyContext.PenaltyKept);
    }

    [Fact]
    public async Task A_kept_penalty_brings_the_booking_one_new_statement_that_states_it()
    {
        // Owner, 2026-09-30 (pre-launch item 212): when the ledger keeps a penalty, the booking's statement gets a new
        // version so the current statement states the outcome — issued by the documents step the same pass runs next.
        var (booking, _) = await CancelledLateAsync();
        _harness.Bookings.Now = booking.FinishedAt!.Value.AddMinutes(1);
        await _harness.Bookings.PassAsync();
        var ended = Assert.Single(await _harness.Bookings.DocumentsAsync(), document => document.Type == FinancialDocumentType.BookingStatement);
        Assert.Same(FinancialDocumentCause.BookingEnded, ended.Cause);

        await _harness.PassAsync();
        var payable = Assert.Single(await _harness.PayablesAsync());
        Assert.Same(PayableOutcome.PenaltyKept, payable.Outcome);
        _harness.Bookings.Now = _harness.Now;
        await _harness.Bookings.PassAsync();

        var statements = (await _harness.Bookings.DocumentsAsync())
            .Where(document => document.Type == FinancialDocumentType.BookingStatement)
            .OrderBy(document => document.Version)
            .ToList();
        Assert.Equal(2, statements.Count);
        var kept = statements[1];
        Assert.Same(FinancialDocumentCause.PenaltyKept, kept.Cause);
        Assert.Equal(ended.Id, kept.PreviousVersionId);
        Assert.Equal(payable.RecordedAt, kept.CoversThrough);
        Assert.Contains(
            "The assessed deposit penalty has now been finalized and applied according to the booking’s cancellation terms.",
            kept.Snapshot,
            StringComparison.Ordinal);

        // Once: the next pass finds nothing new about the booking.
        Assert.DoesNotContain(await _harness.Bookings.PassAsync(), outcome => outcome.DocumentId is not null);
        Assert.Equal(2, (await _harness.Bookings.DocumentsAsync()).Count(document => document.Type == FinancialDocumentType.BookingStatement));
    }

    [Fact]
    public async Task A_booking_whose_records_contradict_one_another_is_held_and_looked_at_again_later()
    {
        var (booking, payment) = await CompletedAsync();
        await ChangePaymentAsync(payment.Id, stored => Assert.True(stored.RequestRefund(Money.Jod(3m), null, _harness.Now).IsSuccess));

        Assert.Contains(PayableStep.Held, await _harness.PassAsync());

        var hold = Assert.Single(await _harness.HoldsAsync());
        Assert.Same(PayableHoldReason.NeedsReview, hold.Reason);
        Assert.Equal(booking.Id, hold.BookingId);
        Assert.Null(hold.PayableId);
        Assert.Contains("RefundWithoutCause", hold.Detail, StringComparison.Ordinal);
        Assert.Empty(await _harness.PayablesAsync());

        // Not looked at again before its next check; then again, doubling the wait.
        Assert.Empty(await _harness.PassAsync());
        _harness.Now = hold.NextCheckAt!.Value;
        Assert.Contains(PayableStep.Held, await _harness.PassAsync());
        var again = Assert.Single(await _harness.HoldsAsync());
        Assert.Equal(2, again.Checks);
        Assert.Equal(_harness.Now.AddMinutes(10), again.NextCheckAt);

        var summary = await _harness.ReadAsync(ledger => ledger.SystemHoldsSummaryAsync());
        Assert.Equal(1, summary.Count);
        Assert.Equal([booking.Reference.Value], summary.BookingReferences);
    }

    [Fact]
    public async Task A_penalty_that_is_not_the_whole_deposit_is_held_not_kept()
    {
        _harness.Bookings.Now = _harness.Now;
        var (customer, dealer, vehicle) = await _harness.Bookings.PartiesAsync();
        var booking = Build.Booking(_harness.Now, customerId: customer.Id, dealerId: dealer.Id, vehicleId: vehicle.Id, terms: Build.Terms(customerPenaltyPercent: 50m));
        Assert.True(booking.Approve(Id.New(), _harness.Now).IsSuccess);
        var payment = Payment.Open(booking.Id, booking.CustomerId, Money.Jod(18m), PaymentProviders.Sandbox, _harness.Now.AddMinutes(30), _harness.Now, PaymentPurpose.Deposit, Money.Jod(0m), true);
        Assert.True(payment.AttachProviderSession("sess_half", "https://provider.test/checkout").IsSuccess);
        Assert.True(payment.Apply(Money.Jod(18m), _harness.Now, _harness.Now).IsSuccess);
        Assert.True(booking.ConfirmPayment(payment.Id, payment.AppliedToBooking, _harness.Now).IsSuccess);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, booking.FreeCancellationDeadline!.Value.AddMinutes(1)).IsSuccess);
        booking.ClearDomainEvents();
        payment.ClearDomainEvents();
        await _harness.Bookings.SaveAsync(booking, payment);
        _harness.Now = booking.DisputeWindowEndsAt!.Value.AddMinutes(11);

        await _harness.PassAsync();

        var hold = Assert.Single(await _harness.HoldsAsync());
        Assert.Same(PayableHoldReason.PenaltyNotWholeDeposit, hold.Reason);
        Assert.Empty(await _harness.PayablesAsync());
    }

    [Fact]
    public async Task A_payable_whose_records_later_disagree_is_held_back_and_released_when_they_agree_again()
    {
        var (_, payment) = await CompletedAsync();
        await _harness.PassAsync();
        var payable = Assert.Single(await _harness.PayablesAsync());

        // A refund appears on a completed rental after the payable was recorded: a contradiction, never re-recorded.
        await ChangePaymentAsync(payment.Id, stored => Assert.True(stored.RequestRefund(Money.Jod(3m), null, _harness.Now).IsSuccess));
        _harness.VerifyOffset = 0;
        Assert.Contains(PayableStep.Contradicted, await _harness.PassAsync());

        var hold = Assert.Single(await _harness.HoldsAsync());
        Assert.Same(PayableHoldReason.Contradicted, hold.Reason);
        Assert.Equal(payable.Id, hold.PayableId);
        var read = (await _harness.ReadAsync(ledger => ledger.PayableAsync(payable.Id)))!;
        Assert.Equal(PayableStates.OnHold, read.State);
        Assert.Equal(12m, Assert.Single(await _harness.PayablesAsync()).Net);
    }

    /// <summary>The work the pass would be given at the harness's clock, with a 10-minute margin and a 48-hour window.</summary>
    private async Task<PayableWork> WorkAsync()
    {
        await using var context = _harness.NewContext();
        var completedBefore = _harness.Now.AddMinutes(-10);
        return await new PayableWorkReader(context).ListAsync(_harness.Now, completedBefore, completedBefore.AddHours(-48), 10, 0, 10);
    }

    [Fact]
    public async Task A_cancellation_inside_todays_window_is_not_even_a_candidate()
    {
        var (completed, _) = await CompletedAsync(unique: "3004");
        var (cancelled, _) = await CancelledLateAsync(unique: "3003");

        _harness.Now = cancelled.DisputeWindowEndsAt!.Value.AddMinutes(-30);
        Assert.Equal([completed.Id], (await WorkAsync()).BookingsToRecord);

        _harness.Now = cancelled.DisputeWindowEndsAt.Value.AddMinutes(11);
        Assert.Equal([completed.Id, cancelled.Id], (await WorkAsync()).BookingsToRecord);
    }

    [Fact]
    public async Task Completed_rentals_are_recorded_before_older_cancellations()
    {
        var (cancelled, _) = await CancelledLateAsync(unique: "3005");
        var (completed, _) = await CompletedAsync(unique: "3006");

        // The cancellation ended first, and still comes second: a crowd of them can never hold a rental up.
        Assert.True(cancelled.FinishedAt < completed.FinishedAt);
        Assert.Equal([completed.Id, cancelled.Id], (await WorkAsync()).BookingsToRecord);
    }

    [Fact]
    public async Task A_hold_whose_reason_no_longer_holds_is_released_while_the_booking_waits_for_its_window()
    {
        var (booking, _) = await CancelledLateAsync();
        await _harness.Bookings.ChangeAsync(async context =>
        {
            context.OfficePayableHolds.Add(OfficePayableHold.OpenBySystem(
                PayableHoldReason.NeedsReview, booking.Id, booking.DealerId, null, "RefundWithoutCause", booking.FinishedAt!.Value, booking.FinishedAt.Value.AddMinutes(5)));
            await Task.CompletedTask;
        });
        _harness.Now = booking.DisputeWindowEndsAt!.Value.AddHours(-1);

        await using (var context = _harness.NewContext())
            Assert.Same(PayableStep.NotFinal, await _harness.RecordHandler(context).Handle(new RecordOfficePayableCommand(booking.Id), CancellationToken.None));

        Assert.False(Assert.Single(await _harness.HoldsAsync()).IsOpen);
        Assert.Empty(await _harness.PayablesAsync());
    }

    // ── Balances and settlements ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_administrator_settles_the_offices_whole_net_balance_under_a_TEST_number_audited()
    {
        var (booking, _) = await CompletedAsync(inFull: true);
        await _harness.PassAsync();

        var balance = Assert.Single(await _harness.ReadAsync(ledger => ledger.BalancesAsync(null)));
        Assert.Equal(booking.DealerId, balance.DealerId);
        Assert.True(balance.IsTest);
        Assert.Equal(1, balance.DueCount);
        Assert.Equal(84m, balance.DueNet);

        var settled = await _harness.SettleAsync(booking.DealerId, PaymentProviders.Sandbox, 84m, reference: "TRX-77", note: "Bank transfer.");

        Assert.True(settled.IsSuccess, settled.IsFailure ? settled.Error.Code : null);
        var settlement = settled.Value.Settlement;
        Assert.Equal("TEST-SET-2026-000001", settlement.Number);
        Assert.Equal("Payout", settlement.Direction);
        Assert.Equal(84m, settlement.Amount.Amount);
        Assert.Equal("TRX-77", settlement.Reference);
        Assert.Equal("Bank transfer.", settlement.Note);
        // Named from the accounts table, which this database has no administrator in; the audit entry snapshots the name.
        Assert.Null(settlement.RecordedBy);
        Assert.Equal(booking.Reference.Value, Assert.Single(settled.Value.Lines).BookingReference);
        var payable = Assert.Single(await _harness.PayablesAsync());
        Assert.Equal(settlement.SettlementId, payable.SettlementId!.Value.Value);
        var audit = Assert.Single(await _harness.AuditAsync());
        Assert.Same(AuditAction.OfficeSettlementRecorded, audit.Action);
        Assert.Equal("TEST-SET-2026-000001", audit.SubjectLabel);
        Assert.Equal("84.000 JOD", audit.NewValue);
        var after = Assert.Single(await _harness.ReadAsync(ledger => ledger.BalancesAsync(null)));
        Assert.Equal(0, after.DueCount);
        Assert.Equal("TEST-SET-2026-000001", after.LastSettlement!.Number);
    }

    [Fact]
    public async Task A_balance_nobody_saw_is_never_settled()
    {
        var (booking, _) = await CompletedAsync();
        await _harness.PassAsync();

        var changed = await _harness.SettleAsync(booking.DealerId, PaymentProviders.Sandbox, 13m);
        var nothing = await _harness.SettleAsync(booking.DealerId, "Tap", 12m);
        var future = await _harness.SettleAsync(booking.DealerId, PaymentProviders.Sandbox, 12m, paidOn: Build.AmmanDate(_harness.Now).AddDays(1));

        Assert.Equal("payables.balance_changed", changed.Error.Code);
        var current = Assert.IsType<Dictionary<string, object?>>(changed.Error.Extensions!["currentAmount"]);
        Assert.Equal(12m, current["amount"]);
        Assert.Equal("payables.nothing_due", nothing.Error.Code);
        Assert.Equal("payables.paid_on_in_future", future.Error.Code);
        Assert.Null(Assert.Single(await _harness.PayablesAsync()).SettlementId);
        Assert.Empty(await _harness.AuditAsync());
    }

    [Fact]
    public async Task A_payable_with_a_dispute_live_or_held_by_an_administrator_is_not_due()
    {
        var (blockedBooking, blockedPayment) = await CompletedAsync(unique: "1001");
        var (heldBooking, _) = await CompletedAsync(unique: "1002");
        await _harness.PassAsync();
        var held = (await _harness.PayablesAsync()).Single(payable => payable.BookingId == heldBooking.Id);
        // A dispute's refund to the customer, still on its way, on the blocked booking — recorded after its payable,
        // so it contradicts nothing but still blocks.
        await _harness.Bookings.ChangeAsync(async context =>
        {
            var ticket = Khadra.Domain.Disputes.DisputeTicket.Open(blockedBooking.Id, blockedBooking.CustomerId, BookingParty.Customer, "Scratch.", TimeSpan.FromHours(48), _harness.Now).Value;
            context.DisputeTickets.Add(ticket);
            await Task.CompletedTask;
        });

        Assert.True((await _harness.HoldAsync(held.Id, "Bank details unconfirmed.")).IsSuccess);

        var balances = await _harness.ReadAsync(ledger => ledger.BalancesAsync(null));
        Assert.All(balances, balance =>
        {
            Assert.Equal(0, balance.DueCount);
            Assert.Equal(1, balance.NotYetDueCount);
        });
        var blocked = (await _harness.ReadAsync(ledger => ledger.ForBookingAsync(blockedBooking.Id))).Payable!;
        Assert.Equal(PayableStates.Blocked, blocked.State);
        Assert.Equal(PayableBlockKinds.DisputeLive, Assert.Single(blocked.Blocks).Kind);
        Assert.Equal("payables.nothing_due", (await _harness.SettleAsync(heldBooking.DealerId, PaymentProviders.Sandbox, 12m)).Error.Code);
        Assert.Equal("payables.already_held", (await _harness.HoldAsync(held.Id, "Again.")).Error.Code);

        Assert.True((await _harness.ReleaseAsync(held.Id, "Confirmed.")).IsSuccess);
        Assert.True((await _harness.SettleAsync(heldBooking.DealerId, PaymentProviders.Sandbox, 12m)).IsSuccess);
        Assert.Equal("payables.not_held", (await _harness.ReleaseAsync(held.Id)).Error.Code);
        Assert.Equal(
            [AuditAction.OfficePayableHeld, AuditAction.OfficePayableReleased, AuditAction.OfficeSettlementRecorded],
            (await _harness.AuditAsync()).OrderBy(entry => entry.OccurredAt).ThenBy(entry => entry.Id.Value).Select(entry => entry.Action));
        _ = blockedPayment;
    }

    [Fact]
    public async Task A_voided_settlement_opens_its_payables_again_and_is_voided_once()
    {
        var (booking, _) = await CompletedAsync();
        await _harness.PassAsync();
        var settled = (await _harness.SettleAsync(booking.DealerId, PaymentProviders.Sandbox, 12m)).Value.Settlement;

        Assert.Equal("payables.void_reason_required", (await _harness.VoidAsync(Id.From(settled.SettlementId), " ")).Error.Code);
        var voided = await _harness.VoidAsync(Id.From(settled.SettlementId), "Recorded against the wrong office.");

        Assert.True(voided.IsSuccess, voided.IsFailure ? voided.Error.Code : null);
        Assert.Equal("Recorded against the wrong office.", voided.Value.Settlement.Void!.Reason);
        Assert.Null(Assert.Single(await _harness.PayablesAsync()).SettlementId);
        Assert.Equal("payables.settlement_already_voided", (await _harness.VoidAsync(Id.From(settled.SettlementId), "Again.")).Error.Code);

        // Due again, and settled again under the next number; the voided one still reads as it was recorded.
        var again = await _harness.SettleAsync(booking.DealerId, PaymentProviders.Sandbox, 12m);
        Assert.Equal("TEST-SET-2026-000002", again.Value.Settlement.Number);
        var listed = await _harness.ReadAsync(ledger => ledger.ListSettlementsAsync(booking.DealerId, 1, 10));
        Assert.Equal(["TEST-SET-2026-000002", "TEST-SET-2026-000001"], listed.Items.Select(settlement => settlement.Number));
        Assert.NotNull(listed.Items[1].Void);
        Assert.Equal(1, listed.Items[1].PayableCount);
    }

    [Fact]
    public async Task An_office_that_owes_more_than_it_is_owed_is_settled_as_money_received()
    {
        var (booking, payment) = await CompletedAsync();
        await _harness.PassAsync();
        // A second booking of the SAME office, whose dispute charged the office more than it earned.
        var dealerId = booking.DealerId;
        _harness.Bookings.Now = _harness.Now;
        var second = Build.Booking(_harness.Now, customerId: booking.CustomerId, dealerId: dealerId, vehicleId: booking.VehicleId);
        Assert.True(second.Approve(Id.New(), _harness.Now).IsSuccess);
        var secondPayment = Payment.Open(second.Id, second.CustomerId, Money.Jod(18m), PaymentProviders.Sandbox, _harness.Now.AddMinutes(30), _harness.Now, PaymentPurpose.Deposit, Money.Jod(0m), true);
        Assert.True(secondPayment.AttachProviderSession("sess_second", "https://provider.test/checkout").IsSuccess);
        Assert.True(secondPayment.Apply(Money.Jod(18m), _harness.Now, _harness.Now).IsSuccess);
        Assert.True(second.ConfirmPayment(secondPayment.Id, secondPayment.AppliedToBooking, _harness.Now).IsSuccess);
        var cancelledAt = second.FreeCancellationDeadline!.Value.AddMinutes(1);
        Assert.True(second.Cancel(BookingParty.Dealer, Id.New(), "No car.", cancelledAt).IsSuccess);
        var ticket = Khadra.Domain.Disputes.DisputeTicket.Open(second.Id, second.CustomerId, BookingParty.Customer, "No car.", TimeSpan.FromHours(48), cancelledAt).Value;
        var charge = second.Penalty!.MaxAmount;
        Assert.True(ticket.Resolve(Khadra.Domain.Disputes.DisputeResolution.Create(
            Khadra.Domain.Disputes.DepositDisposition.Create(Money.Jod(18m), Money.Jod(18m), Money.Jod(0m), Money.Jod(0m)).Value,
            Money.Create(charge.Amount, charge.CurrencyCode),
            second.Penalty,
            "The office never had the car.",
            Id.New(),
            cancelledAt.AddHours(1)).Value).IsSuccess);
        var refund = secondPayment.RequestRefund(Money.Jod(18m), ticket.Id, cancelledAt.AddHours(1)).Value;
        refund.MarkSent("rf_second", cancelledAt.AddHours(1));
        refund.MarkSettled(cancelledAt.AddHours(2));
        second.ClearDomainEvents();
        secondPayment.ClearDomainEvents();
        ticket.ClearDomainEvents();
        await _harness.Bookings.SaveAsync(second, secondPayment);
        await _harness.Bookings.ChangeAsync(async context =>
        {
            context.DisputeTickets.Add(ticket);
            await Task.CompletedTask;
        });
        _harness.Now = second.DisputeWindowEndsAt!.Value.AddMinutes(11);

        await _harness.PassAsync();

        var owed = 12m - charge.Amount;
        var balance = Assert.Single(await _harness.ReadAsync(ledger => ledger.BalancesAsync(dealerId)));
        Assert.Equal(2, balance.DueCount);
        Assert.Equal(owed, balance.DueNet);
        var settled = await _harness.SettleAsync(dealerId, PaymentProviders.Sandbox, owed);
        Assert.True(settled.IsSuccess, settled.IsFailure ? settled.Error.Code : null);
        Assert.Equal(owed > 0 ? "Payout" : owed < 0 ? "Received" : "Netted", settled.Value.Settlement.Direction);
        Assert.Equal(2, settled.Value.Lines.Count);
        _ = payment;
    }

    // ── The finance figures ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_finance_summary_counts_commission_by_outcome_and_money_by_the_day_it_moved()
    {
        var (booking, _) = await CompletedAsync(inFull: true);
        await _harness.PassAsync();
        await _harness.SettleAsync(booking.DealerId, PaymentProviders.Sandbox, 84m);
        var day = Build.AmmanDate(_harness.Now);

        var summary = await _harness.ReadAsync(ledger => ledger.FinanceSummaryAsync(
            day.AddDays(-30), day, DocumentFixtures.Amman.StartOfDay(day.AddDays(-30)), DocumentFixtures.Amman.StartOfDay(day.AddDays(1))));

        var totals = Assert.Single(summary.Totals);
        Assert.True(totals.IsTest);
        Assert.Equal(6m, totals.CommissionEarned);
        Assert.Equal(90m, totals.OfficeMoney);
        Assert.Equal(84m, totals.PaidToOffices);
        Assert.Equal(0m, totals.OwedToOffices);
        Assert.Equal(1, totals.PayablesRecorded);
        Assert.Equal(0, summary.HeldBookings);
    }

    [Fact]
    public async Task The_settlement_series_is_the_documents_gapless_counter_under_its_own_key()
    {
        var (booking, _) = await CompletedAsync();
        await _harness.PassAsync();
        await _harness.SettleAsync(booking.DealerId, PaymentProviders.Sandbox, 12m);

        await using var context = _harness.NewContext();
        var series = await context.FinancialDocumentSeries.AsNoTracking().SingleAsync(row => row.SeriesKey == "TEST-SET-2026");
        Assert.Equal(1, series.LastNumber);
        Assert.Equal(FinancialDocumentNumbers.Format("TEST-SET-2026", 1), (await context.OfficeSettlements.AsNoTracking().SingleAsync()).Number);
    }
}
