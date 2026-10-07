using Khadra.Application.Common;
using Khadra.Application.Dealers;
using Khadra.Application.Payables.Queries;
using Khadra.Application.Payables.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Payables;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Payables;

/// <summary>
/// The office's own payouts (payments Phase 8): its money and no other office's, for the owner and an employee granted
/// the reports, without what only administrators read — the kind of money, the notes, who recorded or voided a
/// settlement and why, the holds and the blocks. The live check saw each of these once; these tests keep them.
/// </summary>
public sealed class OfficePayoutQueryTests
{
    private static readonly Id OwnerId = Id.New();
    private static readonly Id EmployeeId = Id.New();

    private readonly IDealerRepository _dealers = Substitute.For<IDealerRepository>();
    private readonly IOfficeLedgerReader _ledger = Substitute.For<IOfficeLedgerReader>();

    private OfficePayoutQueryHandlers Handlers() => new(_ledger, new DealerMembershipResolver(_dealers));

    [Fact]
    public async Task Another_offices_settlement_is_answered_as_one_that_does_not_exist()
    {
        OwnedOffice();
        var elsewhere = new LedgerSettlementDetail(Settlement(dealerId: Id.New()), []);
        _ledger.SettlementAsync(elsewhere.Settlement.SettlementId, Arg.Any<CancellationToken>()).Returns(elsewhere);

        var theirs = await Handlers().Handle(new GetMySettlementQuery(OwnerId, elsewhere.Settlement.SettlementId), CancellationToken.None);
        var never = await Handlers().Handle(new GetMySettlementQuery(OwnerId, Id.New()), CancellationToken.None);

        // The same answer as a number never issued: a guessed id learns nothing about another office.
        Assert.Equal("payables.settlement_not_found", theirs.Error.Code);
        Assert.Equal(never.Error.Code, theirs.Error.Code);
    }

    [Fact]
    public async Task The_offices_own_settlement_comes_without_what_only_administrators_read()
    {
        var office = OwnedOffice();
        var voided = new LedgerSettlementDetail(
            Settlement(office.Id, voided: true),
            [new LedgerSettlementLine(Id.New(), Id.New(), "KH-EVUYXLJD", PayableOutcome.PenaltyKept, 12m)]);
        _ledger.SettlementAsync(voided.Settlement.SettlementId, Arg.Any<CancellationToken>()).Returns(voided);

        var detail = (await Handlers().Handle(new GetMySettlementQuery(OwnerId, voided.Settlement.SettlementId), CancellationToken.None)).Value;

        // Its own figures, its reference, what it covered and when it was voided...
        Assert.Equal("TEST-SET-2026-000001", detail.Settlement.Number);
        Assert.Equal(12m, detail.Settlement.Amount.Amount);
        Assert.Equal("TRX-77", detail.Settlement.Reference);
        Assert.Equal("KH-EVUYXLJD", Assert.Single(detail.Lines).BookingReference);
        Assert.Equal(Build.Now.AddMinutes(8), detail.Settlement.Void!.VoidedAt);
        // ...and nothing that is the administrators': the kind of money, the note, who recorded or voided it, and why.
        Assert.Null(detail.Settlement.IsTest);
        Assert.Null(detail.Settlement.Note);
        Assert.Null(detail.Settlement.RecordedBy);
        Assert.Null(detail.Settlement.Void.VoidedBy);
        Assert.Null(detail.Settlement.Void.Reason);
    }

    [Fact]
    public async Task Every_read_names_the_callers_own_office_and_strips_what_only_administrators_read()
    {
        var office = OwnedOffice();
        _ledger.BalancesAsync(office.Id, Arg.Any<CancellationToken>()).Returns([Balance(office.Id)]);
        _ledger.ListPayablesAsync(Arg.Any<PayableListFilter>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<LedgerPayable>([HeldPayable(office.Id)], 1, 25, 1));
        _ledger.ListSettlementsAsync(office.Id, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<LedgerSettlement>([Settlement(office.Id)], 1, 25, 1));

        var balances = (await Handlers().Handle(new GetMyPayoutsQuery(OwnerId), CancellationToken.None)).Value;
        var payables = (await Handlers().Handle(new ListMyPayablesQuery(OwnerId, PayableListScopes.All, 1, 25), CancellationToken.None)).Value;
        var settlements = (await Handlers().Handle(new ListMySettlementsQuery(OwnerId, 1, 25), CancellationToken.None)).Value;

        // A null office is EVERY office's balance, so it is the one argument these reads must never pass.
        await _ledger.Received(1).BalancesAsync(office.Id, Arg.Any<CancellationToken>());
        await _ledger.DidNotReceive().BalancesAsync(null, Arg.Any<CancellationToken>());
        await _ledger.Received(1).ListPayablesAsync(
            Arg.Is<PayableListFilter>(filter => filter.DealerId == office.Id), Arg.Any<CancellationToken>());
        await _ledger.Received(1).ListSettlementsAsync(office.Id, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());

        var balance = Assert.Single(balances.Balances);
        Assert.Equal(3m, balance.NotYetDue.Amount);
        // Held back and blocked, each netted on its own (Wave 4, F56 a): the office reads both too.
        Assert.Equal((0, 0m, 1, 3m), (balance.HeldCount, balance.Held.Amount, balance.BlockedCount, balance.Blocked.Amount));
        Assert.Equal("JOD", balance.Blocked.Currency);
        Assert.Equal("TEST-SET-2026-000002", balance.LastSettlement!.Number);
        Assert.Null(balance.Provider);
        Assert.Null(balance.IsTest);

        // The office reads that a payable is held back, never by whom or why.
        var payable = Assert.Single(payables.Items);
        Assert.Equal(PayableStates.OnHold, payable.State);
        Assert.Null(payable.Holds);
        Assert.Null(payable.Blocks);
        Assert.Null(payable.IsTest);
        Assert.Null(payable.CalculatorVersion);

        var settlement = Assert.Single(settlements.Items);
        Assert.Null(settlement.Note);
        Assert.Null(settlement.RecordedBy);
        Assert.Null(settlement.IsTest);
    }

    [Fact]
    public async Task An_employee_without_the_reports_is_refused_every_read_before_the_ledger_is_asked()
    {
        var office = Build.ApprovedDealer(ownerUserId: OwnerId);
        office.HireEmployee(EmployeeId, canViewReports: false, Build.Now);
        _dealers.GetByStaffUserIdAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(office);
        var handlers = Handlers();

        Error[] refusals =
        [
            (await handlers.Handle(new GetMyPayoutsQuery(EmployeeId), CancellationToken.None)).Error,
            (await handlers.Handle(new ListMyPayablesQuery(EmployeeId, PayableListScopes.All, 1, 25), CancellationToken.None)).Error,
            (await handlers.Handle(new ListMySettlementsQuery(EmployeeId, 1, 25), CancellationToken.None)).Error,
            (await handlers.Handle(new GetMySettlementQuery(EmployeeId, Id.New()), CancellationToken.None)).Error,
        ];

        Assert.All(refusals, refusal => Assert.Equal("dealer.reports_not_granted", refusal.Code));
        Assert.Empty(_ledger.ReceivedCalls());
    }

    [Fact]
    public async Task An_employee_granted_the_reports_reads_the_offices_payouts()
    {
        var office = Build.ApprovedDealer(ownerUserId: OwnerId);
        office.HireEmployee(EmployeeId, canViewReports: true, Build.Now);
        _dealers.GetByStaffUserIdAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(office);
        _ledger.BalancesAsync(office.Id, Arg.Any<CancellationToken>()).Returns([Balance(office.Id)]);

        var payouts = await Handlers().Handle(new GetMyPayoutsQuery(EmployeeId), CancellationToken.None);

        Assert.Equal(office.Id.Value, Assert.Single(payouts.Value.Balances).DealerId);
    }

    [Fact]
    public async Task A_suspended_office_still_reads_what_it_is_owed()
    {
        // Owner, 2026-09-30: a suspended office may read its payout history; it gains no financial action with it.
        var office = OwnedOffice();
        office.Suspend(Id.New(), "Complaints.", Build.Now);
        _ledger.BalancesAsync(office.Id, Arg.Any<CancellationToken>()).Returns([Balance(office.Id)]);

        var payouts = await Handlers().Handle(new GetMyPayoutsQuery(OwnerId), CancellationToken.None);

        Assert.Equal(3m, Assert.Single(payouts.Value.Balances).NotYetDue.Amount);
    }

    [Fact]
    public async Task Someone_with_no_office_is_told_so_and_the_ledger_is_never_asked()
    {
        var answer = await Handlers().Handle(new GetMyPayoutsQuery(Id.New()), CancellationToken.None);

        Assert.Equal("dealer.not_registered", answer.Error.Code);
        Assert.Empty(_ledger.ReceivedCalls());
    }

    private Dealer OwnedOffice()
    {
        var office = Build.ApprovedDealer(ownerUserId: OwnerId);
        _dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(office);
        return office;
    }

    private static OfficeBalance Balance(Id dealerId) =>
        new(
            dealerId,
            "Petra Rentals",
            "JOD",
            "SANDBOX",
            IsTest: true,
            DueCount: 0,
            DueNet: 0m,
            NotYetDueCount: 1,
            NotYetDueNet: 3m,
            new LedgerSettlementSummary(Id.New(), "TEST-SET-2026-000002", SettlementDirection.Payout, 12m, new DateOnly(2026, 9, 30)),
            HeldCount: 0,
            HeldNet: 0m,
            BlockedCount: 1,
            BlockedNet: 3m);

    private static LedgerSettlement Settlement(Id dealerId, bool voided = false) =>
        new(
            Id.New(),
            "TEST-SET-2026-000001",
            dealerId,
            "Petra Rentals",
            "JOD",
            IsTest: true,
            SettlementDirection.Payout,
            Amount: 12m,
            PaidOn: new DateOnly(2026, 9, 30),
            Reference: "TRX-77",
            Note: "Paid from the operating account.",
            RecordedAt: Build.Now,
            RecordedByName: "Rana Haddad",
            PayableCount: 1,
            Void: voided ? new LedgerSettlementVoid(Build.Now.AddMinutes(8), "Rana Haddad", "Recorded against the wrong office.") : null);

    private static LedgerPayable HeldPayable(Id dealerId)
    {
        var payableId = Id.New();
        var bookingId = Id.New();
        return new LedgerPayable(
            payableId,
            bookingId,
            "KH-EVUYXLJD",
            dealerId,
            "Petra Rentals",
            PayableOutcome.PenaltyKept,
            "JOD",
            IsTest: true,
            OfficeMoney: 18m,
            Commission: 6m,
            OfficeCharges: 0m,
            Net: 12m,
            FinalAt: Build.Now,
            RecordedAt: Build.Now.AddMinutes(10),
            CalculatorVersion: 2,
            PayableStates.OnHold,
            [new LedgerLine(PayableLineKind.PenaltyKept, 18m, null), new LedgerLine(PayableLineKind.Commission, 6m, null)],
            Settlement: null,
            [new LedgerHold(Id.New(), bookingId, "KH-EVUYXLJD", payableId, PayableHoldReason.Manual, "Checking the office's bank details.", Build.Now, "Rana Haddad")],
            [new PayableBlock(bookingId, PayableBlockKinds.RefundOutstanding, Id.New())]);
    }
}
