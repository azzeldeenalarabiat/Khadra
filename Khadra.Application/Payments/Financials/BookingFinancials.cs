using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
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
    IReadOnlyList<string> Issues)
{
    /// <summary>
    /// The version of the rules this answer was computed by. Raised whenever a figure's definition
    /// changes, so a statement frozen from an earlier answer says which rules produced it.
    /// </summary>
    public const int CalculatorVersion = 1;

    /// <summary>
    /// Whether the records contradict one another (<see cref="FinancialIssues"/>): the answer is still
    /// served — a read must never fail a customer's booking page — but somebody has to look.
    /// </summary>
    public bool NeedsReview => Issues.Count > 0;
}

/// <summary>The booking's figures, every one in the booking's currency.</summary>
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
public sealed record CommissionPosition(Money Amount, decimal Percent, string Basis, string State);

/// <summary>One checkout attempt, as the financial history shows it.</summary>
/// <param name="AmountCharged">What the card was charged: the capture, or what was asked while nothing was.</param>
/// <param name="AppliedToBooking">What went towards the booking: the charge less the fee, or nothing if it did not apply.</param>
/// <param name="RefundProgress">Where this payment's refunds stand (<see cref="RefundProgresses"/>).</param>
/// <param name="OfficeRefundProgress">
/// The same reading over only the refunds the office is shown — every one but a dispute decision's share,
/// which is the customer's (owner, 2026-09-26) — and complete only when THOSE return all the booking money
/// the payment applied, so the office's badge never speaks for, or gives away, a row it cannot see.
/// </param>
public sealed record PaymentRecord(
    Id PaymentId,
    PaymentPurpose Purpose,
    PaymentStatus Status,
    string RefundProgress,
    string OfficeRefundProgress,
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
    /// A penalty on the customer, the window closed, and no dispute was opened (pre-launch item 164):
    /// held, with final settlement pending. Nothing is promised to either side.
    /// </summary>
    public const string HeldUnresolved = "HeldUnresolved";

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
    /// Completed through a dispute, or ended before pickup with money still held: the office payables
    /// ledger (payments Phase 8) decides. Phase 4 takes no commission from a held deposit.
    /// </summary>
    public const string Undecided = "Undecided";
}

/// <summary>Where a payment's refunds stand, as one reading of their statuses.</summary>
public static class RefundProgresses
{
    /// <summary>Nothing has been refunded.</summary>
    public const string None = "None";

    /// <summary>A refund is on its way.</summary>
    public const string InProgress = "InProgress";

    /// <summary>A refund was refused and is being sent again: still owed.</summary>
    public const string Delayed = "Delayed";

    /// <summary>Every refund reached the customer, and part of the payment was kept.</summary>
    public const string Partial = "Partial";

    /// <summary>Everything the payment will ever return has reached the customer.</summary>
    public const string Complete = "Complete";
}

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
}
