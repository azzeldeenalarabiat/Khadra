using Khadra.Application.Common;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Common;
using Khadra.Domain.Payables;

namespace Khadra.Application.Payables.ReadModels;

/// <summary>Why an open payable is not due, read live every time (payments Phase 8): never a stored flag.</summary>
public static class PayableBlockKinds
{
    /// <summary>A refund on the booking is not settled — requested, sent, or refused and being sent again.</summary>
    public const string RefundOutstanding = "RefundOutstanding";

    /// <summary>A dispute on the booking is open.</summary>
    public const string DisputeLive = "DisputeLive";
}

/// <summary>One reason a booking's payable is not due right now.</summary>
/// <param name="Kind">One of <see cref="PayableBlockKinds"/>.</param>
/// <param name="RefundId">The refund still outstanding, for <see cref="PayableBlockKinds.RefundOutstanding"/>.</param>
public sealed record PayableBlock(Id BookingId, string Kind, Id? RefundId);

/// <summary>An open hold, as administrators read it.</summary>
/// <param name="Detail">The system's description, or the administrator's reason. Administrators only.</param>
/// <param name="OpenedByName">The administrator who held it; null for the system's holds.</param>
/// <param name="BookingReference">The booking's reference, as the ledger's screens name it; null if it no longer resolves.</param>
public sealed record LedgerHold(
    Id HoldId,
    Id BookingId,
    string? BookingReference,
    Id? PayableId,
    PayableHoldReason Reason,
    string? Detail,
    DateTimeOffset OpenedAt,
    string? OpenedByName);

/// <summary>A settlement a payable belongs to, as a line on the payable names it.</summary>
public sealed record LedgerSettlementRef(Id SettlementId, string Number, DateOnly PaidOn);

/// <summary>One line of a recorded payable.</summary>
public sealed record LedgerLine(PayableLineKind Kind, decimal Amount, Id? SourceId);

/// <summary>
/// Where a recorded payable stands (payments Phase 8): settled, held, blocked, or open — and then due when it
/// carries money either way, or closed by nothing when it carries none.
/// </summary>
public static class PayableStates
{
    /// <summary>Open, carrying money, and in the next settlement.</summary>
    public const string Due = "Due";

    /// <summary>Open and net zero: nothing will ever move for it. It is recorded so the booking says so.</summary>
    public const string NothingDue = "NothingDue";

    /// <summary>Open, but a refund on the booking is outstanding or a dispute on it is live.</summary>
    public const string Blocked = "Blocked";

    /// <summary>Open, and held back: the records no longer match it, or an administrator left it out.</summary>
    public const string OnHold = "OnHold";

    /// <summary>Closed by a settlement.</summary>
    public const string Settled = "Settled";

    /// <summary>
    /// The state of an open payable, in the order that most needs saying: held before blocked, and blocked before
    /// due. The one definition; every list and the settlement's own choice of what is due read it.
    /// </summary>
    public static string Of(bool settled, bool held, bool blocked, decimal net) =>
        settled ? Settled
        : held ? OnHold
        : blocked ? Blocked
        : net == 0m ? NothingDue
        : Due;
}

/// <summary>A recorded payable, as administrators and the office read it.</summary>
/// <param name="State">One of <see cref="PayableStates"/>.</param>
/// <param name="Holds">Its open holds. Administrators only; the office reads <paramref name="State"/>.</param>
/// <param name="Blocks">Why it is not due, live. Administrators only.</param>
public sealed record LedgerPayable(
    Id PayableId,
    Id BookingId,
    string BookingReference,
    Id DealerId,
    string DealerName,
    PayableOutcome Outcome,
    string Currency,
    bool IsTest,
    decimal OfficeMoney,
    decimal Commission,
    decimal OfficeCharges,
    decimal Net,
    DateTimeOffset FinalAt,
    DateTimeOffset RecordedAt,
    int CalculatorVersion,
    string State,
    IReadOnlyList<LedgerLine> Lines,
    LedgerSettlementRef? Settlement,
    IReadOnlyList<LedgerHold> Holds,
    IReadOnlyList<PayableBlock> Blocks);

/// <summary>What the ledger holds about ONE booking: its payable, if recorded, and every open hold on it.</summary>
/// <param name="Holds">
/// The booking's open holds: on its payable, or — when none could be recorded — on the booking itself.
/// </param>
public sealed record BookingLedger(LedgerPayable? Payable, IReadOnlyList<LedgerHold> Holds, IReadOnlyList<PayableBlock> Blocks);

/// <summary>Which recorded payables a list shows.</summary>
public static class PayableListScopes
{
    /// <summary>
    /// Every payable not yet settled that still has something to happen: due, blocked or held (Wave 4, F56 c). A net
    /// zero payable nothing holds back is Nothing due, and leaves this list — it used to stay here for ever, since no
    /// settlement ever closes it.
    /// </summary>
    public const string Open = "Open";

    /// <summary>Open, net zero, and neither held nor blocked: recorded so the booking says so, and nothing more.</summary>
    public const string NothingDue = "NothingDue";

    public const string Settled = "Settled";

    public const string All = "All";
}

/// <param name="Scope">One of <see cref="PayableListScopes"/>.</param>
/// <param name="FinalFrom">Only payables whose outcome became final at or after this instant.</param>
/// <param name="FinalBefore">Only payables whose outcome became final before this instant.</param>
public sealed record PayableListFilter(
    Id? DealerId,
    string Scope,
    DateTimeOffset? FinalFrom,
    DateTimeOffset? FinalBefore,
    int Page,
    int PageSize);

/// <summary>
/// One office's balance in one currency and one kind of money (payments Phase 8): what is due now, netted, and what
/// is recorded but not due yet.
/// </summary>
/// <param name="DueNet">The sum of every due payable's net: above zero Khadra owes the office, below zero the office owes.</param>
/// <param name="NotYetDueNet">The sum of the open payables held back or blocked.</param>
/// <param name="HeldNet">
/// Of those, the ones held back (Wave 4, F56 a): netted apart from the blocked ones, so a held payable is never
/// called "not due yet" and a held credit and a blocked debit never cancel into a zero nobody can read.
/// </param>
/// <param name="BlockedNet">Of those, the ones blocked: a refund on the booking outstanding, or a dispute on it live.</param>
public sealed record OfficeBalance(
    Id DealerId,
    string DealerName,
    string Currency,
    string Provider,
    bool IsTest,
    int DueCount,
    decimal DueNet,
    int NotYetDueCount,
    decimal NotYetDueNet,
    LedgerSettlementSummary? LastSettlement,
    int HeldCount,
    decimal HeldNet,
    int BlockedCount,
    decimal BlockedNet);

/// <summary>The newest settlement that was not voided.</summary>
public sealed record LedgerSettlementSummary(Id SettlementId, string Number, SettlementDirection Direction, decimal Amount, DateOnly PaidOn);

/// <summary>A settlement, as its list shows it.</summary>
/// <param name="RecordedByName">The administrator who recorded it, as the audit trail names them.</param>
/// <param name="Note">The administrator's note. Administrators only.</param>
public sealed record LedgerSettlement(
    Id SettlementId,
    string Number,
    Id DealerId,
    string DealerName,
    string Currency,
    bool IsTest,
    SettlementDirection Direction,
    decimal Amount,
    DateOnly PaidOn,
    string? Reference,
    string? Note,
    DateTimeOffset RecordedAt,
    string? RecordedByName,
    int PayableCount,
    LedgerSettlementVoid? Void);

/// <param name="Reason">The administrator's reason. Administrators only.</param>
public sealed record LedgerSettlementVoid(DateTimeOffset VoidedAt, string? VoidedByName, string Reason);

/// <summary>A settlement with the payables it closed, each at the net it closed it at.</summary>
public sealed record LedgerSettlementDetail(LedgerSettlement Settlement, IReadOnlyList<LedgerSettlementLine> Lines);

public sealed record LedgerSettlementLine(Id PayableId, Id BookingId, string BookingReference, PayableOutcome Outcome, decimal Net);

/// <summary>
/// Khadra's own money over a span of final outcomes, in one currency and one kind of money (payments Phase 8): what
/// it earned, what disputes left with it, and what moved to and from offices. Every figure is a sum of recorded
/// rows; nothing is estimated.
/// </summary>
/// <param name="CommissionEarned">Commission on the payables whose outcome became final in the span.</param>
/// <param name="KeptFromDisputes">What disputes resolved in the span left with the platform.</param>
/// <param name="PaidToOffices">Settlements paid out, by the day the money moved, voided ones excluded.</param>
/// <param name="ReceivedFromOffices">Settlements received, as a positive figure.</param>
/// <param name="OwedToOffices">What is due to offices right now, over every office whose balance is above zero.</param>
/// <param name="OwedByOffices">What offices owe right now, as a positive figure.</param>
public sealed record FinanceTotals(
    string Currency,
    bool IsTest,
    decimal CommissionEarned,
    decimal KeptFromDisputes,
    decimal OfficeMoney,
    decimal OfficeCharges,
    decimal PaidToOffices,
    decimal ReceivedFromOffices,
    decimal OwedToOffices,
    decimal OwedByOffices,
    int PayablesRecorded);

/// <param name="HeldBookings">Bookings a hold stops being recorded.</param>
/// <param name="HeldPayables">Open payables held back from settlements.</param>
/// <param name="BlockedPayables">Open payables not due while a refund is outstanding or a dispute live.</param>
public sealed record FinanceSummary(IReadOnlyList<FinanceTotals> Totals, int HeldBookings, int HeldPayables, int BlockedPayables);

/// <summary>
/// The office payables ledger, as every screen reads it (payments Phase 8). One reader, so the admin screens, the
/// office's payouts and the booking's money card cannot state three different balances.
/// </summary>
public interface IOfficeLedgerReader
{
    Task<BookingLedger> ForBookingAsync(Id bookingId, CancellationToken cancellationToken = default);

    /// <summary>One recorded payable, or null.</summary>
    Task<LedgerPayable?> PayableAsync(Id payableId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the calculator reads from a booking's payable once it is recorded: its outcome and commission. Null
    /// while it has none.
    /// </summary>
    Task<RecordedPayable?> RecordedAsync(Id bookingId, CancellationToken cancellationToken = default);

    /// <summary>Every office's balance, or one office's, per currency and kind of money.</summary>
    Task<IReadOnlyList<OfficeBalance>> BalancesAsync(Id? dealerId, CancellationToken cancellationToken = default);

    /// <summary>Recorded payables, newest outcome first, in a total order so a page boundary never drops one.</summary>
    Task<PagedResult<LedgerPayable>> ListPayablesAsync(PayableListFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Open holds that stop a payable being recorded at all, oldest first: the booking's records need a person.</summary>
    Task<IReadOnlyList<LedgerHold>> ListBookingHoldsAsync(Id? dealerId, CancellationToken cancellationToken = default);

    /// <summary>An office's settlements, newest first.</summary>
    Task<PagedResult<LedgerSettlement>> ListSettlementsAsync(Id dealerId, int page, int pageSize, CancellationToken cancellationToken = default);

    Task<LedgerSettlementDetail?> SettlementAsync(Id settlementId, CancellationToken cancellationToken = default);

    /// <param name="fromDay">The first Amman day: settlements are counted by the day their money moved.</param>
    /// <param name="toDay">The last Amman day, inclusive.</param>
    /// <param name="from">The instant <paramref name="fromDay"/> begins: payables and disputes are counted by instant.</param>
    /// <param name="before">The instant the day after <paramref name="toDay"/> begins.</param>
    Task<FinanceSummary> FinanceSummaryAsync(
        DateOnly fromDay,
        DateOnly toDay,
        DateTimeOffset from,
        DateTimeOffset before,
        CancellationToken cancellationToken = default);

    /// <summary>Why each of these bookings' payables is not due, live.</summary>
    Task<IReadOnlyList<PayableBlock>> BlocksAsync(IReadOnlyCollection<Id> bookingIds, CancellationToken cancellationToken = default);

    /// <summary>The office's name, or null when there is no such office.</summary>
    Task<string?> OfficeNameAsync(Id dealerId, CancellationToken cancellationToken = default);

    /// <summary>The system's open holds, for the work queue.</summary>
    Task<PayableHoldsSummary> SystemHoldsSummaryAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The system's open holds on offices' money, for the work queue (payments Phase 8): bookings that cannot be recorded
/// and payables that no longer match their records. An administrator's own holds are decisions, not work, and are
/// not counted.
/// </summary>
public sealed record PayableHoldsSummary(
    int Count,
    IReadOnlyList<Id> BookingIds,
    IReadOnlyList<string> BookingReferences,
    DateTimeOffset? OldestOpenedAt)
{
    public static readonly PayableHoldsSummary None = new(0, [], [], null);
}

/// <summary>What the payables pass has to look at on one tick.</summary>
/// <param name="BookingsToRecord">Paid bookings that ended, with no payable and nothing holding them until later, oldest first.</param>
/// <param name="PayablesToVerify">Open payables carrying money, to check against their records again: one page of them.</param>
/// <param name="NextVerifyOffset">Where the next tick's page starts: back to 0 after the last page.</param>
public sealed record PayableWork(IReadOnlyList<Id> BookingsToRecord, IReadOnlyList<Id> PayablesToVerify, int NextVerifyOffset);

/// <summary>The work the payables pass finds, cheaply, in SQL; every decision is the calculator's.</summary>
/// <remarks>
/// A cancellation's or a no-show's dispute window is frozen in its terms and judged by the aggregate, as every sweep
/// here judges it; SQL is given today's window as a bound, so the candidates stop re-reading every such booking
/// inside it. One frozen with a LONGER window than today's is let through early and left until it closes; one frozen
/// SHORTER is read later by the difference — a delay, never a wrong figure. Completed rentals come first, so a
/// crowd of cancellations can never hold them up.
/// </remarks>
public interface IPayableWorkReader
{
    /// <param name="completedBefore">Only rentals that completed before this instant: now less the finality margin.</param>
    /// <param name="cancelledBefore">
    /// Only cancellations and no-shows that ended before this instant: now less the margin and today's dispute window.
    /// </param>
    /// <param name="verifyOffset">Where this tick's page of open payables starts, in their stable order.</param>
    Task<PayableWork> ListAsync(
        DateTimeOffset now,
        DateTimeOffset completedBefore,
        DateTimeOffset cancelledBefore,
        int maxBookings,
        int verifyOffset,
        int maxVerifications,
        CancellationToken cancellationToken = default);
}

/// <summary>The payables pass's settings: numbers about how often and how much to look, never business rules.</summary>
public interface IPayablesSettings
{
    /// <summary>
    /// How long after an outcome became final the pass waits before recording it, so a fact committed just after
    /// with an earlier instant is read too.
    /// </summary>
    TimeSpan FinalityMargin { get; }

    int MaxBookingsPerPass { get; }

    int MaxVerificationsPerPass { get; }

    /// <summary>When a booking held back is looked at again, first.</summary>
    TimeSpan RetryInitialDelay { get; }

    /// <summary>The longest wait between two looks at a held booking.</summary>
    TimeSpan RetryMaxDelay { get; }
}
