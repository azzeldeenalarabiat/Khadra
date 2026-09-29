using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Payables;
using Khadra.Domain.Payments;

namespace Khadra.Application.Payments.Financials;

/// <summary>
/// Everything a booking's money has done, as ONE server answer (payments Phase 4, owner 2026-09-26):
/// the booking's frozen figures, what was charged and applied, what went back and where it is, the
/// balance and deposit as states rather than as guesses, and the commission for the readers entitled
/// to it. Built by <see cref="BookingFinancialsCalculator"/> and nowhere else.
/// </summary>
/// <remarks>
/// <para>
/// Computed only from facts that never change once written — the booking's frozen pricing and terms,
/// each payment's amount, fee and capture, each refund's amount and reason, each dispute decision's
/// shares — plus refund STATUS, which is a lifecycle. Never from the current business rules, a car's
/// price, an office's settings or its delivery fee. Phase 5 freezes this whole answer into an issued
/// booking statement, which is why every state is a stable code and <see cref="CalculatorVersion"/>
/// travels with it.
/// </para>
/// <para>
/// What each reader may see is a PROJECTION of this one answer (<c>BookingFinancialsDto.For</c>): the
/// customer never sees commission or the office's and platform's dispute shares; the office never sees
/// processing fees or the customer's dispute share; the administrator sees everything.
/// </para>
/// </remarks>
public sealed record BookingFinancials(
    Id BookingId,
    BookingStatus BookingStatus,
    string Currency,
    DateTimeOffset GeneratedAt,
    FinancialSummary Summary,
    BalancePosition Balance,
    DepositPosition Deposit,
    CommissionPosition Commission,
    IReadOnlyList<PaymentRecord> Payments,
    IReadOnlyList<string> Issues,
    OfficePosition Office)
{
    /// <summary>
    /// The version of the rules this answer was computed by. Raised whenever a figure's definition
    /// changes, so a statement frozen from an earlier answer says which rules produced it.
    /// </summary>
    /// <remarks>
    /// 2 (payments Phase 8, owner 2026-09-29): what a final booking comes to for the office; a customer's penalty
    /// kept from the deposit when the window closes with no dispute; commission earned, capped at the office's
    /// money, whenever the ledger records a payable; and a refund on a completed booking that no dispute explains
    /// is a contradiction.
    /// </remarks>
    public const int CalculatorVersion = 2;

    /// <summary>
    /// Whether the records contradict one another (<see cref="FinancialIssues"/>): the answer is still
    /// served — a read must never fail a customer's booking page — but somebody has to look.
    /// </summary>
    public bool NeedsReview => Issues.Count > 0;
}

/// <summary>The booking's figures, every one in the booking's currency.</summary>
/// <param name="Days">The billed calendar days, frozen on the booking: a screen never counts them.</param>
/// <param name="DailyRate">The frozen daily rate the rental was priced at.</param>
/// <param name="DepositPercent">The frozen share of the rental the deposit is.</param>
/// <param name="RentalSubtotal">The rental alone: the frozen daily rate over the frozen days.</param>
/// <param name="BookingTotal">Rental plus delivery fee: what the booking costs, before any fee or deposit.</param>
/// <param name="SecurityDeposit">
/// The car's security deposit the OFFICE holds at pickup. Information only: never paid online and
/// never part of any total here.
/// </param>
/// <param name="PaidOnline">What the customer paid online towards the booking, processing fees excluded.</param>
/// <param name="ProcessingFees">The processing fees on the payments that applied to the booking.</param>
/// <param name="ChargedOnline">
/// Everything the customer's card was charged: applied payments with their fees, and any capture that
/// could not be applied (it is being refunded). A capture in another currency is listed, never summed.
/// </param>
/// <param name="Refunds">Every refund against the booking's payments, by where it is.</param>
/// <param name="OfficeRefunds">
/// What went back of the booking's own money, as the rental office reads it: booking money only (no
/// processing fee), from the payment that applied, and without a dispute decision's share, which is
/// the customer's (owner, 2026-09-26).
/// </param>
public sealed record FinancialSummary(
    int Days,
    Money DailyRate,
    decimal DepositPercent,
    Money RentalSubtotal,
    Money DeliveryFee,
    Money BookingTotal,
    Money RequiredDeposit,
    Money SecurityDeposit,
    Money PaidOnline,
    Money ProcessingFees,
    Money ChargedOnline,
    RefundTotals Refunds,
    RefundTotals OfficeRefunds);

/// <summary>Refunds by where they are: back with the customer, on their way, or refused and being sent again.</summary>
public sealed record RefundTotals(Money Settled, Money InProgress, Money Delayed);

/// <summary>What is still to be paid on the booking, as a state (<see cref="BalanceStates"/>).</summary>
/// <param name="Amount">The balance the state is about: what is due, or was due at handover; zero otherwise.</param>
/// <param name="CashRecorded">
/// Cash the office recorded on a handover, as it recorded it. The platform never sees this money, and
/// a figure recorded at pickup may include a cash security deposit, so it is shown beside the balance
/// and never compared with it.
/// </param>
public sealed record BalancePosition(string State, Money Amount, IReadOnlyList<CashRecord> CashRecorded);

/// <summary>Cash an office recorded on one handover.</summary>
public sealed record CashRecord(HandoverType Handover, Money Amount, DateTimeOffset RecordedAt);

/// <summary>Where the deposit is, as a state (<see cref="DepositStates"/>).</summary>
/// <param name="Amount">
/// The deposit the state is about: the part of the payment that is the deposit, what a release
/// returned, or what a dispute decided.
/// </param>
/// <param name="WindowEndsAt">When the booking's dispute window closes, where that is what the deposit waits for.</param>
/// <param name="Refund">The refund that returned or released it, when one did.</param>
/// <param name="Decision">What the booking's resolved disputes decided, when they did.</param>
public sealed record DepositPosition(
    string State,
    Money Amount,
    DateTimeOffset? WindowEndsAt,
    RefundRecord? Refund,
    DisputeDecision? Decision);

/// <summary>What the booking's resolved disputes decided about its deposit, and any charge to the office.</summary>
/// <param name="CustomerRefunds">The refunds that carry the customer's share, with their status.</param>
public sealed record DisputeDecision(
    IReadOnlyList<Id> TicketIds,
    DateTimeOffset DecidedAt,
    Money ToCustomer,
    Money ToOffice,
    Money KeptByPlatform,
    Money? ChargedToOffice,
    IReadOnlyList<RefundRecord> CustomerRefunds);

/// <summary>
/// Khadra's commission: the figures FROZEN on the booking (terms, never claims about money) and a state
/// (<see cref="CommissionStates"/>). Never shown to a customer.
/// </summary>
/// <param name="Earned">
/// What Khadra earned, when the state is <see cref="CommissionStates.Earned"/> (payments Phase 8): the frozen
/// figure, capped at the office's money on the booking (owner, 2026-09-29). Null in every other state.
/// </param>
public sealed record CommissionPosition(Money Amount, decimal Percent, string Basis, string State, Money? Earned);

/// <summary>
/// What a final booking comes to for the rental office (payments Phase 8): the outcome, the lines that make it, and
/// the office's money, Khadra's commission, any dispute charge and the signed net. The ledger records exactly this
/// (<c>OfficePayable</c>), and checks what it recorded against it again until it is settled. Administrator and
/// office only; never the customer.
/// </summary>
/// <param name="State">One of <see cref="OfficeStates"/>.</param>
/// <param name="FinalAt">When the outcome became final: the booking's completion, or its dispute window's close.</param>
/// <param name="Net">What the office is owed, signed: below zero, the office owes Khadra.</param>
/// <param name="UndeterminedBecause">A <c>PayableHoldReason</c> name, when a final outcome cannot be recorded.</param>
public sealed record OfficePosition(
    string State,
    PayableOutcome? Outcome,
    DateTimeOffset? FinalAt,
    IReadOnlyList<PayableLineDraft> Lines,
    Money OfficeMoney,
    Money Commission,
    Money Charges,
    decimal Net,
    string? UndeterminedBecause)
{
    public bool IsFinal => State == OfficeStates.Final;
}

/// <summary>
/// The facts the calculator reads from the office payables ledger once it recorded the booking (payments Phase 8):
/// the recorded decision is the truth about where a kept penalty went and what commission was earned, never a
/// figure worked out again from today's records.
/// </summary>
/// <param name="PayableId">The payable, which a booking statement names when it states a kept penalty (item 212).</param>
/// <param name="RecordedAt">When the ledger recorded it: the instant a kept penalty became final.</param>
public sealed record RecordedPayable(Id PayableId, PayableOutcome Outcome, Money Commission, DateTimeOffset RecordedAt);

/// <summary>One checkout attempt, as the financial history shows it.</summary>
/// <param name="AmountCharged">What the card was charged: the capture, or what was asked while nothing was.</param>
/// <param name="AppliedToBooking">What went towards the booking: the charge less the fee, or nothing if it did not apply.</param>
/// <param name="RefundProgress">Where this payment's refunds stand: <see cref="Payment.RefundProgress"/>.</param>
/// <param name="OfficeRefundProgress">
/// The same reading over only the refunds the office is shown — every one but a dispute decision's share,
/// which is the customer's (owner, 2026-09-26) — and complete only when THOSE return all the booking money
/// the payment applied, so the office's badge never speaks for, or gives away, a row it cannot see.
/// </param>
public sealed record PaymentRecord(
    Id PaymentId,
    PaymentPurpose Purpose,
    PaymentStatus Status,
    RefundProgress RefundProgress,
    RefundProgress OfficeRefundProgress,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt,
    Money AmountCharged,
    Money ProcessingFee,
    bool FeeRefundable,
    Money AppliedToBooking,
    IReadOnlyList<RefundRecord> Refunds,
    bool IsSandbox,
    string? ProviderReference,
    string? FailureCode,
    string? OrphanReason);

/// <summary>One refund, split into the booking's money and the processing fee (<c>Payment.FeeInside</c>).</summary>
public sealed record RefundRecord(
    Id RefundId,
    Id PaymentId,
    RefundReason Reason,
    RefundStatus Status,
    Money Amount,
    Money BookingPart,
    Money FeePart,
    DateTimeOffset RequestedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? SettledAt,
    DateTimeOffset? FailedAt,
    Id? DisputeTicketId,
    string? ProviderReference,
    string? FailureCode);

/// <summary>What is still to be paid on a booking.</summary>
public static class BalanceStates
{
    /// <summary>Requested, or approved and unpaid: the payment choice says what is due now.</summary>
    public const string NotYetDue = "NotYetDue";

    /// <summary>Confirmed on the deposit: the rest is paid to the office at pickup or delivery.</summary>
    public const string DueAtHandover = "DueAtHandover";

    /// <summary>
    /// The car was handed over on a deposit-only booking: the balance was due to the office then, and
    /// any cash the office recorded is shown beside it. Never "paid" without a record.
    /// </summary>
    public const string CashAtHandover = "CashAtHandover";

    /// <summary>The whole total was paid online and the rental is going ahead.</summary>
    public const string PaidInFull = "PaidInFull";

    /// <summary>The booking ended before pickup: nothing further is due.</summary>
    public const string NotDue = "NotDue";
}

/// <summary>Where a booking's deposit is.</summary>
public static class DepositStates
{
    /// <summary>Nothing has been paid.</summary>
    public const string NotPaid = "NotPaid";

    /// <summary>Confirmed: every exit is still open — a free cancellation returns it, a late one assesses on it.</summary>
    public const string Held = "Held";

    /// <summary>The car was collected: the deposit counts towards the rental.</summary>
    public const string AppliedToRental = "AppliedToRental";

    /// <summary>Returned, and a dispute can still be opened until the window closes.</summary>
    public const string InSettlementWindow = "InSettlementWindow";

    /// <summary>A dispute on the booking is open.</summary>
    public const string UnderDispute = "UnderDispute";

    /// <summary>Completed with no dispute decision: the deposit went with the rental.</summary>
    public const string SettledWithRental = "SettledWithRental";

    /// <summary>The whole payment went back — a free cancellation, or an administrator's.</summary>
    public const string ReturnedWithPayment = "ReturnedWithPayment";

    /// <summary>Ended before pickup with no penalty on the customer: released when the window closes.</summary>
    public const string HeldUntilWindowCloses = "HeldUntilWindowCloses";

    /// <summary>A penalty on the customer was assessed; a dispute can still be opened.</summary>
    public const string HeldForAssessedPenalty = "HeldForAssessedPenalty";

    /// <summary>
    /// A penalty on the customer, the window closed, and no dispute was opened (pre-launch item 164), and the
    /// office payables ledger has not recorded the booking yet — or cannot (payments Phase 8): held.
    /// </summary>
    public const string HeldUnresolved = "HeldUnresolved";

    /// <summary>
    /// A penalty on the customer, the window closed with no dispute, and the ledger recorded it: the deposit was
    /// kept as the penalty, for the office less Khadra's commission (owner, 2026-09-29; pre-launch item 164).
    /// </summary>
    public const string KeptAsPenalty = "KeptAsPenalty";

    /// <summary>Returned to the customer when the window closed cleanly.</summary>
    public const string Released = "Released";

    /// <summary>A resolved dispute decided it.</summary>
    public const string DecidedByDispute = "DecidedByDispute";
}

/// <summary>Khadra's commission on a booking (owner, 2026-09-26). Never shown to a customer.</summary>
public static class CommissionStates
{
    /// <summary>Requested, or approved and unpaid: the frozen figure, if the booking goes ahead.</summary>
    public const string Projected = "Projected";

    /// <summary>Paid and not yet completed: the rental is going ahead or has happened.</summary>
    public const string Expected = "Expected";

    /// <summary>Completed, with no dispute or refund affecting settlement.</summary>
    public const string Earned = "Earned";

    /// <summary>The whole payment went back: Khadra keeps nothing.</summary>
    public const string NotEarned = "NotEarned";

    /// <summary>The booking ended without anything being paid.</summary>
    public const string NotApplicable = "NotApplicable";

    /// <summary>
    /// Completed through a dispute, or ended before pickup with money still held, and not yet recorded by the
    /// office payables ledger (payments Phase 8), which decides once the outcome is final.
    /// </summary>
    public const string Undecided = "Undecided";
}

/// <summary>What a booking comes to for the rental office, as a state (payments Phase 8).</summary>
public static class OfficeStates
{
    /// <summary>Nothing was paid online: the booking never reaches the office payables ledger.</summary>
    public const string NotApplicable = "NotApplicable";

    /// <summary>The outcome is not final yet: the booking is running, its dispute window is open, or a dispute is live.</summary>
    public const string Open = "Open";

    /// <summary>The outcome is final, and these figures are what the ledger records.</summary>
    public const string Final = "Final";

    /// <summary>The outcome is final but cannot be recorded (<see cref="OfficePosition.UndeterminedBecause"/>).</summary>
    public const string Undetermined = "Undetermined";
}

/// <summary>Where a payment's refunds stand, as one reading of their statuses.</summary>
/// <summary>Contradictions between the records, as stable codes an administrator reads.</summary>
public static class FinancialIssues
{
    /// <summary>The booking names a confirming payment that is missing or never applied.</summary>
    public const string ConfirmingPaymentMissing = "ConfirmingPaymentMissing";

    /// <summary>The booking's ending owes a refund that was never recorded (see pre-launch item 165).</summary>
    public const string EndingRefundMissing = "EndingRefundMissing";

    /// <summary>A refund's amount is not what the rule it was recorded under says.</summary>
    public const string RefundAmountUnexpected = "RefundAmountUnexpected";

    /// <summary>Refunds that cannot coexist on one payment: two whole-payment refunds, or one beside a partial ending.</summary>
    public const string RefundsConflict = "RefundsConflict";

    /// <summary>The resolved disputes' shares do not add up to the deposit they decided.</summary>
    public const string DisputeSharesUnbalanced = "DisputeSharesUnbalanced";

    /// <summary>
    /// A completed booking's payment carries a refund no resolved dispute of the booking explains (payments Phase 8):
    /// a rental that happened owes nothing back except what a dispute decided.
    /// </summary>
    public const string RefundWithoutCause = "RefundWithoutCause";

    /// <summary>
    /// What the booking records as paid online is not what its confirming payment applied (payments Phase 8): the
    /// office's money is priced from one of them, and they must be the same fact.
    /// </summary>
    public const string PaidOnlineDisagrees = "PaidOnlineDisagrees";
}
