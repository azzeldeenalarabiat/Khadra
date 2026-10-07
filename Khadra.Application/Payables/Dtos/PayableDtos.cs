using Khadra.Application.Common.Dtos;
using Khadra.Application.Payables.ReadModels;

namespace Khadra.Application.Payables.Dtos;

// The office payables ledger on the wire (payments Phase 8). Administrators see everything; an office sees its own
// payables and settlements without the ledger's internal notes: no hold's reason or detail, no administrator's
// note, no void's reason. Every money value carries its currency, and a NET may be below zero — the office owes.

/// <param name="Kind">
/// <c>RentalRevenue</c>, <c>DisputeShare</c> or <c>PenaltyKept</c> (the office's money, +), <c>Commission</c> or
/// <c>DisputeCharge</c> (taken from it, −).
/// </param>
/// <param name="Amount">Always positive: the kind says which way it goes.</param>
/// <param name="TicketId">The dispute a share or a charge came from.</param>
public sealed record PayableLineDto(string Kind, MoneyDto Amount, Guid? TicketId)
{
    internal static PayableLineDto From(LedgerLine line, string currency) =>
        new(line.Kind.Name, new MoneyDto(line.Amount, currency), line.SourceId?.Value);
}

/// <param name="Reason"><c>NeedsReview</c>, <c>PenaltyNotWholeDeposit</c>, <c>Contradicted</c> or <c>Manual</c>.</param>
/// <param name="Detail">The system's description, or the administrator's reason.</param>
/// <param name="OpenedBy">The administrator who held it; null for the system's holds.</param>
public sealed record PayableHoldDto(
    Guid HoldId,
    Guid BookingId,
    string? BookingReference,
    Guid? PayableId,
    string Reason,
    string? Detail,
    DateTimeOffset OpenedAt,
    string? OpenedBy)
{
    internal static PayableHoldDto From(LedgerHold hold) =>
        new(
            hold.HoldId.Value,
            hold.BookingId.Value,
            hold.BookingReference,
            hold.PayableId?.Value,
            hold.Reason.Name,
            hold.Detail,
            hold.OpenedAt,
            hold.OpenedByName);
}

/// <param name="Kind"><c>RefundOutstanding</c> or <c>DisputeLive</c>.</param>
public sealed record PayableBlockDto(string Kind, Guid? RefundId)
{
    internal static PayableBlockDto From(PayableBlock block) => new(block.Kind, block.RefundId?.Value);
}

public sealed record SettlementRefDto(Guid SettlementId, string Number, DateOnly PaidOn)
{
    internal static SettlementRefDto? From(LedgerSettlementRef? settlement) =>
        settlement is null ? null : new SettlementRefDto(settlement.SettlementId.Value, settlement.Number, settlement.PaidOn);
}

/// <summary>One booking's payable.</summary>
/// <param name="Outcome">
/// <c>Rental</c>, <c>RentalAfterDispute</c>, <c>DisputeDecided</c>, <c>PenaltyKept</c>, <c>DepositReleased</c> or
/// <c>PaymentReturned</c>.
/// </param>
/// <param name="State"><c>Due</c>, <c>NothingDue</c>, <c>Blocked</c>, <c>OnHold</c> or <c>Settled</c>.</param>
/// <param name="Net">What the office is owed on the booking: below zero, it owes.</param>
/// <param name="IsTest">Sandbox money. Administrators only.</param>
/// <param name="Holds">Open holds, with their reasons. Administrators only.</param>
/// <param name="Blocks">Why it is not due, live. Administrators only.</param>
public sealed record OfficePayableDto(
    Guid PayableId,
    Guid BookingId,
    string BookingReference,
    Guid DealerId,
    string DealerName,
    string Outcome,
    string State,
    MoneyDto OfficeMoney,
    MoneyDto Commission,
    MoneyDto OfficeCharges,
    MoneyDto Net,
    DateTimeOffset FinalAt,
    DateTimeOffset RecordedAt,
    IReadOnlyList<PayableLineDto> Lines,
    SettlementRefDto? Settlement,
    bool? IsTest,
    int? CalculatorVersion,
    IReadOnlyList<PayableHoldDto>? Holds,
    IReadOnlyList<PayableBlockDto>? Blocks)
{
    public static OfficePayableDto ForAdmin(LedgerPayable payable) => Of(payable, admin: true);

    public static OfficePayableDto ForOffice(LedgerPayable payable) => Of(payable, admin: false);

    private static OfficePayableDto Of(LedgerPayable payable, bool admin)
    {
        ArgumentNullException.ThrowIfNull(payable);
        var currency = payable.Currency;
        return new OfficePayableDto(
            payable.PayableId.Value,
            payable.BookingId.Value,
            payable.BookingReference,
            payable.DealerId.Value,
            payable.DealerName,
            payable.Outcome.Name,
            payable.State,
            new MoneyDto(payable.OfficeMoney, currency),
            new MoneyDto(payable.Commission, currency),
            new MoneyDto(payable.OfficeCharges, currency),
            new MoneyDto(payable.Net, currency),
            payable.FinalAt,
            payable.RecordedAt,
            payable.Lines.Select(line => PayableLineDto.From(line, currency)).ToList(),
            SettlementRefDto.From(payable.Settlement),
            admin ? payable.IsTest : null,
            admin ? payable.CalculatorVersion : null,
            admin ? payable.Holds.Select(PayableHoldDto.From).ToList() : null,
            admin ? payable.Blocks.Select(PayableBlockDto.From).ToList() : null);
    }
}

/// <summary>The newest settlement that stands.</summary>
/// <param name="Direction"><c>Payout</c>, <c>Received</c> or <c>Netted</c>.</param>
/// <param name="Amount">Signed from Khadra's side: above zero Khadra paid the office.</param>
public sealed record SettlementSummaryDto(Guid SettlementId, string Number, string Direction, MoneyDto Amount, DateOnly PaidOn);

/// <summary>An office's balance in one currency and one kind of money.</summary>
/// <param name="Provider">The kind of money, as payments name it. Administrators send it back when they settle.</param>
/// <param name="Due">Due now, netted: above zero Khadra owes the office, below zero the office owes Khadra.</param>
/// <param name="NotYetDue">Recorded but held back or blocked, netted together. Kept as it was; the two parts follow.</param>
/// <param name="Held">Of those, the ones held back, netted on their own (Wave 4, F56 a). Added last.</param>
/// <param name="Blocked">Of those, the ones blocked, netted on their own.</param>
public sealed record OfficeBalanceDto(
    Guid DealerId,
    string DealerName,
    string Currency,
    string? Provider,
    bool? IsTest,
    int DueCount,
    MoneyDto Due,
    int NotYetDueCount,
    MoneyDto NotYetDue,
    SettlementSummaryDto? LastSettlement,
    int HeldCount,
    MoneyDto Held,
    int BlockedCount,
    MoneyDto Blocked)
{
    public static OfficeBalanceDto ForAdmin(OfficeBalance balance) => Of(balance, admin: true);

    public static OfficeBalanceDto ForOffice(OfficeBalance balance) => Of(balance, admin: false);

    private static OfficeBalanceDto Of(OfficeBalance balance, bool admin)
    {
        ArgumentNullException.ThrowIfNull(balance);
        var currency = balance.Currency;
        return new OfficeBalanceDto(
            balance.DealerId.Value,
            balance.DealerName,
            currency,
            admin ? balance.Provider : null,
            admin ? balance.IsTest : null,
            balance.DueCount,
            new MoneyDto(balance.DueNet, currency),
            balance.NotYetDueCount,
            new MoneyDto(balance.NotYetDueNet, currency),
            balance.LastSettlement is { } last
                ? new SettlementSummaryDto(last.SettlementId.Value, last.Number, last.Direction.Name, new MoneyDto(last.Amount, currency), last.PaidOn)
                : null,
            balance.HeldCount,
            new MoneyDto(balance.HeldNet, currency),
            balance.BlockedCount,
            new MoneyDto(balance.BlockedNet, currency));
    }
}

/// <param name="VoidedBy">The administrator who voided it. Administrators only.</param>
/// <param name="Reason">Why. Administrators only.</param>
public sealed record OfficeSettlementVoidDto(DateTimeOffset VoidedAt, string? VoidedBy, string? Reason);

/// <summary>A settlement recorded by hand: Khadra paid the office, the office paid Khadra, or the two cancelled out.</summary>
/// <param name="Direction"><c>Payout</c>, <c>Received</c> or <c>Netted</c>.</param>
/// <param name="Amount">Signed from Khadra's side.</param>
/// <param name="Reference">The payment's own reference, as recorded.</param>
/// <param name="Note">The administrator's note. Administrators only.</param>
/// <param name="RecordedBy">The administrator who recorded it. Administrators only.</param>
/// <param name="Void">Set when it was voided; the reason is the administrators' only.</param>
public sealed record OfficeSettlementDto(
    Guid SettlementId,
    string Number,
    Guid DealerId,
    string DealerName,
    string Direction,
    MoneyDto Amount,
    DateOnly PaidOn,
    string? Reference,
    string? Note,
    DateTimeOffset RecordedAt,
    string? RecordedBy,
    int PayableCount,
    bool? IsTest,
    OfficeSettlementVoidDto? Void)
{
    public static OfficeSettlementDto ForAdmin(LedgerSettlement settlement) => Of(settlement, admin: true);

    public static OfficeSettlementDto ForOffice(LedgerSettlement settlement) => Of(settlement, admin: false);

    private static OfficeSettlementDto Of(LedgerSettlement settlement, bool admin)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        return new OfficeSettlementDto(
            settlement.SettlementId.Value,
            settlement.Number,
            settlement.DealerId.Value,
            settlement.DealerName,
            settlement.Direction.Name,
            new MoneyDto(settlement.Amount, settlement.Currency),
            settlement.PaidOn,
            settlement.Reference,
            admin ? settlement.Note : null,
            settlement.RecordedAt,
            admin ? settlement.RecordedByName : null,
            settlement.PayableCount,
            admin ? settlement.IsTest : null,
            settlement.Void is { } voided
                ? new OfficeSettlementVoidDto(voided.VoidedAt, admin ? voided.VoidedByName : null, admin ? voided.Reason : null)
                : null);
    }
}

/// <summary>One payable a settlement closed, at the net it closed it at.</summary>
public sealed record OfficeSettlementLineDto(Guid PayableId, Guid BookingId, string BookingReference, string Outcome, MoneyDto Net);

public sealed record OfficeSettlementDetailDto(OfficeSettlementDto Settlement, IReadOnlyList<OfficeSettlementLineDto> Lines)
{
    public static OfficeSettlementDetailDto ForAdmin(LedgerSettlementDetail detail) => Of(detail, admin: true);

    public static OfficeSettlementDetailDto ForOffice(LedgerSettlementDetail detail) => Of(detail, admin: false);

    private static OfficeSettlementDetailDto Of(LedgerSettlementDetail detail, bool admin)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var currency = detail.Settlement.Currency;
        return new OfficeSettlementDetailDto(
            admin ? OfficeSettlementDto.ForAdmin(detail.Settlement) : OfficeSettlementDto.ForOffice(detail.Settlement),
            detail.Lines
                .Select(line => new OfficeSettlementLineDto(
                    line.PayableId.Value, line.BookingId.Value, line.BookingReference, line.Outcome.Name, new MoneyDto(line.Net, currency)))
                .ToList());
    }
}

/// <summary>Khadra's own money over a span, per currency and kind of money (payments Phase 8).</summary>
/// <param name="KeptFromDisputes">What resolved disputes left with the platform: beside commission, never inside it.</param>
public sealed record FinanceTotalsDto(
    string Currency,
    bool IsTest,
    MoneyDto CommissionEarned,
    MoneyDto KeptFromDisputes,
    MoneyDto OfficeMoney,
    MoneyDto OfficeCharges,
    MoneyDto PaidToOffices,
    MoneyDto ReceivedFromOffices,
    MoneyDto OwedToOffices,
    MoneyDto OwedByOffices,
    int PayablesRecorded)
{
    public static FinanceTotalsDto From(FinanceTotals totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        var currency = totals.Currency;
        return new FinanceTotalsDto(
            currency,
            totals.IsTest,
            new MoneyDto(totals.CommissionEarned, currency),
            new MoneyDto(totals.KeptFromDisputes, currency),
            new MoneyDto(totals.OfficeMoney, currency),
            new MoneyDto(totals.OfficeCharges, currency),
            new MoneyDto(totals.PaidToOffices, currency),
            new MoneyDto(totals.ReceivedFromOffices, currency),
            new MoneyDto(totals.OwedToOffices, currency),
            new MoneyDto(totals.OwedByOffices, currency),
            totals.PayablesRecorded);
    }
}

/// <param name="From">The first Amman day of the span.</param>
/// <param name="To">The last Amman day of the span, inclusive.</param>
public sealed record FinanceSummaryDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<FinanceTotalsDto> Totals,
    int HeldBookings,
    int HeldPayables,
    int BlockedPayables)
{
    /// <summary>
    /// Everything held back from what is due: the bookings held before a payable was recorded and the payables held
    /// after. Sent, so the screen states the server's count rather than adding two of its own.
    /// </summary>
    public int Held => HeldBookings + HeldPayables;
}

/// <summary>The office's own payouts page: its balances, per currency.</summary>
public sealed record OfficePayoutsDto(IReadOnlyList<OfficeBalanceDto> Balances);
