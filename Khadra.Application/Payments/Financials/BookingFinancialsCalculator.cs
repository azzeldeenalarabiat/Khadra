using Khadra.Application.Bookings;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Payables;
using Khadra.Domain.Payments;

namespace Khadra.Application.Payments.Financials;

/// <summary>
/// The ONE calculator of a booking's financial state (payments Phase 4, owner 2026-09-26). Every screen
/// — the customer's website and app, the rental office's console, the administrator's — reads its
/// answer; none of them computes a figure of its own.
/// </summary>
/// <remarks>
/// <para>
/// A pure function of the booking, its payments (with their refunds), its resolved disputes and — once the
/// office payables ledger recorded it — the booking's payable (payments Phase 8), and it
/// adds NO rule of its own: every state is a reading of a verdict the aggregates already give —
/// <c>Booking.ReturnsWholePayment</c>, <c>RefundableAboveDeposit</c>, <c>HasPenaltyAgainstCustomer</c>,
/// <c>DisputeWindowEndsAt</c>, <c>EndedBeforePickup</c>, <c>IsPaidInFull</c>; <c>Payment.FeeInside</c>,
/// <c>WholePaymentRefundAmount</c>, <c>IsRefundedInFull</c>; the dispute resolutions' own shares. A rule
/// that is missing belongs on the aggregate, not here.
/// </para>
/// <para>
/// The office's position (payments Phase 8) is the one composition no aggregate can make: what a final booking
/// comes to for its office needs the booking, its disputes and the frozen commission together, and the owner's
/// rules for it (2026-09-24 and 2026-09-29) live in <c>Office</c> below, the only place that states them.
/// </para>
/// <para>
/// It never throws on data that contradicts itself — an ending whose refund was never recorded (the
/// 2316 case, pre-launch item 165), shares that do not add up. It reports it (<see cref="FinancialIssues"/>)
/// and answers with what the records do say: a read that threw would be a 500 on a customer's booking
/// page, for a fault they cannot fix. <c>DisputeViewComposer</c> set the precedent.
/// </para>
/// </remarks>
public static class BookingFinancialsCalculator
{
    /// <param name="payments">Every payment attempt for the booking, with its refunds. Others are ignored.</param>
    /// <param name="resolvedTickets">The booking's RESOLVED dispute tickets. Others are ignored.</param>
    /// <param name="hasLiveDispute">Whether a dispute on the booking is open right now.</param>
    /// <param name="now">The instant the answer is for: the dispute window is judged against it.</param>
    /// <param name="recorded">
    /// The booking's payable, once the office payables ledger recorded it (payments Phase 8): what it decided about
    /// a kept penalty and the commission earned. Null before that, and for every booking that never gets one.
    /// </param>
    public static BookingFinancials Calculate(
        Booking booking,
        IEnumerable<Payment> payments,
        IEnumerable<DisputeTicket> resolvedTickets,
        bool hasLiveDispute,
        DateTimeOffset now,
        RecordedPayable? recorded = null)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(resolvedTickets);

        var currency = booking.Pricing.CurrencyCode;
        var issues = new List<string>();

        var own = payments
            .Where(payment => payment.BookingId == booking.Id)
            .OrderBy(payment => payment.CreatedAt)
            .ThenBy(payment => payment.Id.Value)
            .ToList();
        var decided = resolvedTickets
            .Where(ticket => ticket.BookingId == booking.Id && ticket.Resolution is not null)
            .OrderBy(ticket => ticket.OpenedAt)
            .ThenBy(ticket => ticket.Id.Value)
            .ToList();

        var records = own.Select(Describe).ToList();

        // The payment that CONFIRMED the booking — the only one whose money is the booking's.
        var confirming = booking.DepositPaymentId is { } confirmingId
            ? own.FirstOrDefault(payment => payment.Id == confirmingId && payment.Status == PaymentStatus.Applied)
            : null;
        if (booking.DepositPaymentId is not null && confirming is null)
            issues.Add(FinancialIssues.ConfirmingPaymentMissing);

        Check(booking, own, confirming, decided, issues);

        return new BookingFinancials(
            booking.Id,
            booking.Status,
            currency,
            now,
            Summary(booking, records, currency),
            Balance(booking, currency),
            Deposit(booking, confirming, records, decided, hasLiveDispute, now, currency, recorded),
            Commission(booking, confirming, decided, recorded),
            records,
            issues.Distinct(StringComparer.Ordinal).ToList(),
            Office(booking, decided, hasLiveDispute, now, currency));
    }

    // ── One payment ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One payment as the financial history shows it — the same description on a booking's financial
    /// state and on the payment's own page (payments Phase 4b), so the two can never differ.
    /// </summary>
    public static PaymentRecord Describe(Payment payment)
    {
        var refunds = payment.Refunds
            .OrderBy(refund => refund.RequestedAt)
            .ThenBy(refund => refund.Id.Value)
            .Select(refund => RefundOf(payment, refund))
            .ToList();
        // The office is shown every refund but a dispute decision's share, which is the customer's.
        var officeRefunds = payment.Refunds.Where(refund => refund.Reason != RefundReason.DisputeResolution).ToList();

        return new PaymentRecord(
            payment.Id,
            payment.Purpose,
            payment.Status,
            payment.RefundProgress,
            // Never anything but None for a payment that did not apply: the office is not shown one.
            payment.Status == PaymentStatus.Applied
                ? RefundProgress.Of(officeRefunds, BookingMoneyReturned(payment, officeRefunds))
                : RefundProgress.None,
            // When the money was RECORDED — applied, or captured and not applied — the one instant an issued
            // receipt, its statement and their checkpoints use too (payments Phase 5), so a document and
            // this page never state two times for one payment.
            payment.AppliedAt ?? payment.OrphanedAt ?? payment.CapturedAt ?? payment.FailedAt ?? payment.CreatedAt,
            payment.CreatedAt,
            Fresh(payment.AmountCaptured ?? payment.Amount),
            Fresh(payment.ProcessingFee),
            payment.FeeRefundable,
            payment.Status == PaymentStatus.Applied
                ? Fresh(payment.AppliedToBooking)
                : Money.ZeroIn(payment.Amount.CurrencyCode),
            refunds,
            payment.IsSandbox,
            payment.ProviderReference,
            payment.FailureCode,
            payment.OrphanReason);
    }

    private static RefundRecord RefundOf(Payment payment, Refund refund)
    {
        var fee = payment.FeeInside(refund);
        return new RefundRecord(
            refund.Id,
            payment.Id,
            refund.Reason,
            refund.Status,
            Fresh(refund.Amount),
            payment.BookingMoneyIn(refund),
            fee,
            refund.RequestedAt,
            refund.SentAt,
            refund.SettledAt,
            refund.FailedAt,
            refund.DisputeTicketId,
            refund.ProviderReference,
            refund.FailureCode);
    }

    /// <summary>
    /// The office's "complete": the booking money in the refunds the office is shown covers all the
    /// payment applied to the booking. Judged from those rows alone — never from
    /// <see cref="Payment.IsRefundedInFull"/>, which counts a dispute decision's share too — so the
    /// customer's share can neither complete the office's badge nor be read back out of it. Which rows
    /// the office is shown is this reader's policy, which is why it lives here and not on the payment.
    /// </summary>
    private static bool BookingMoneyReturned(Payment payment, List<Refund> shown)
    {
        var applied = payment.AppliedToBooking;
        var returned = shown
            .Where(refund => refund.Status == RefundStatus.Settled &&
                string.Equals(refund.Amount.CurrencyCode, applied.CurrencyCode, StringComparison.Ordinal))
            .Sum(refund => payment.BookingMoneyIn(refund).Amount);
        return !applied.IsZero && returned >= applied.Amount;
    }

    // ── The booking ────────────────────────────────────────────────────────────────────────────────

    private static FinancialSummary Summary(Booking booking, IReadOnlyList<PaymentRecord> records, string currency)
    {
        var pricing = booking.Pricing;
        var fees = 0m;
        var charged = 0m;
        foreach (var record in records.Where(record => record.Status.IsCaptured))
        {
            // A capture in another currency is listed with its payment, never added to a figure it
            // cannot be part of — the same rule the booking's own refund totals follow.
            if (!string.Equals(record.AmountCharged.CurrencyCode, currency, StringComparison.Ordinal))
                continue;
            charged += record.AmountCharged.Amount;
            if (record.Status == PaymentStatus.Applied)
                fees += record.ProcessingFee.Amount;
        }

        var refunds = records.SelectMany(record => record.Refunds).ToList();
        // The office's reading: booking money that went back from the payment that applied, other than
        // a dispute decision's share (the customer's, owner 2026-09-26) and an orphan (never booking money).
        var officeRefunds = records
            .Where(record => record.Status == PaymentStatus.Applied)
            .SelectMany(record => record.Refunds)
            .Where(refund => refund.Reason != RefundReason.DisputeResolution && refund.Reason != RefundReason.OrphanedCapture)
            .ToList();

        return new FinancialSummary(
            pricing.Days,
            Fresh(pricing.DailyRate),
            pricing.DepositPercent.Value,
            Fresh(pricing.RentalTotal),
            Fresh(pricing.DeliveryFee),
            Fresh(pricing.TotalPrice),
            Fresh(pricing.DepositAmount),
            Fresh(pricing.SecurityDeposit),
            Money.Create(booking.OnlinePaid.Amount, currency),
            Money.Create(fees, currency),
            Money.Create(charged, currency),
            Totals(refunds, refund => refund.Amount, currency),
            Totals(officeRefunds, refund => refund.BookingPart, currency));
    }

    private static RefundTotals Totals(IEnumerable<RefundRecord> refunds, Func<RefundRecord, Money> amountOf, string currency)
    {
        var settled = 0m;
        var inProgress = 0m;
        var delayed = 0m;
        foreach (var refund in refunds)
        {
            var amount = amountOf(refund);
            if (!string.Equals(amount.CurrencyCode, currency, StringComparison.Ordinal))
                continue;
            if (refund.Status == RefundStatus.Settled)
                settled += amount.Amount;
            else if (refund.Status == RefundStatus.Failed)
                delayed += amount.Amount;
            else
                inProgress += amount.Amount;
        }

        return new RefundTotals(
            Money.Create(settled, currency),
            Money.Create(inProgress, currency),
            Money.Create(delayed, currency));
    }

    private static BalancePosition Balance(Booking booking, string currency)
    {
        IReadOnlyList<CashRecord> none = [];
        var zero = Money.ZeroIn(currency);

        // Ended before pickup wins over "paid in full": that is a fact about money RECEIVED, and it stays
        // true of a paid booking after it is cancelled; nothing further is due on it all the same.
        if (booking.EndedBeforePickup)
            return new BalancePosition(BalanceStates.NotDue, zero, none);
        if (booking.DepositPaymentId is null)
            return new BalancePosition(BalanceStates.NotYetDue, zero, none);
        if (booking.IsPaidInFull)
            return new BalancePosition(BalanceStates.PaidInFull, zero, none);

        var due = Money.Create(booking.RemainingBalance.Amount, currency);
        if (booking.PickedUpAt is null)
            return new BalancePosition(BalanceStates.DueAtHandover, due, none);

        var cash = booking.Handovers
            .Where(handover => handover.CashCollected is not null)
            .OrderBy(handover => handover.RecordedAt)
            .Select(handover => new CashRecord(handover.Type, Fresh(handover.CashCollected!), handover.RecordedAt))
            .ToList();
        return new BalancePosition(BalanceStates.CashAtHandover, due, cash);
    }

    private static DepositPosition Deposit(
        Booking booking,
        Payment? confirming,
        IReadOnlyList<PaymentRecord> records,
        List<DisputeTicket> decided,
        bool hasLiveDispute,
        DateTimeOffset now,
        string currency,
        RecordedPayable? recorded)
    {
        if (booking.DepositPaymentId is null)
            return new DepositPosition(DepositStates.NotPaid, Money.ZeroIn(currency), null, null, null);

        // The part of what was paid that is the deposit: the frozen deposit, never more than was paid.
        var deposit = Money.Create(Math.Min(booking.Pricing.DepositAmount.Amount, booking.OnlinePaid.Amount), currency);
        IReadOnlyList<RefundRecord> refunds = confirming is null
            ? []
            : records.First(record => record.PaymentId == confirming.Id).Refunds;
        var whole = refunds.FirstOrDefault(refund => refund.Reason.ReturnsWholePayment);
        var release = refunds.FirstOrDefault(refund => refund.Reason == RefundReason.DisputeWindowClosed);
        var windowEndsAt = booking.DisputeWindowEndsAt;

        if (booking.ReturnsWholePayment || whole is not null)
            return new DepositPosition(DepositStates.ReturnedWithPayment, deposit, null, whole, null);
        if (release is not null)
            return new DepositPosition(DepositStates.Released, release.BookingPart, null, release, null);
        if (decided.Count > 0)
            return new DepositPosition(DepositStates.DecidedByDispute, deposit, null, null, Decision(decided, records, currency));
        if (hasLiveDispute)
            return new DepositPosition(DepositStates.UnderDispute, deposit, windowEndsAt, null, null);

        var state =
            booking.Status == BookingStatus.PickedUp ? DepositStates.AppliedToRental
            : booking.Status == BookingStatus.Returned ? DepositStates.InSettlementWindow
            : booking.Status == BookingStatus.Completed ? DepositStates.SettledWithRental
            : booking.Status == BookingStatus.Cancelled || booking.Status == BookingStatus.NoShow
                ? !booking.HasPenaltyAgainstCustomer
                    ? DepositStates.HeldUntilWindowCloses
                    : windowEndsAt is { } end && now < end
                        ? DepositStates.HeldForAssessedPenalty
                        // Kept only once the ledger RECORDED it (payments Phase 8): the stored decision is what
                        // moved the deposit, never this reading of the clock.
                        : recorded?.Outcome == PayableOutcome.PenaltyKept
                            ? DepositStates.KeptAsPenalty
                            : DepositStates.HeldUnresolved
            // Confirmed: every exit is still open.
            : DepositStates.Held;

        var waitsForWindow = state is DepositStates.InSettlementWindow
            or DepositStates.HeldUntilWindowCloses
            or DepositStates.HeldForAssessedPenalty
            or DepositStates.HeldUnresolved;
        return new DepositPosition(state, deposit, waitsForWindow ? windowEndsAt : null, null, null);
    }

    private static DisputeDecision Decision(List<DisputeTicket> decided, IReadOnlyList<PaymentRecord> records, string currency)
    {
        var toCustomer = 0m;
        var toOffice = 0m;
        var kept = 0m;
        var charged = 0m;
        var anyCharge = false;
        foreach (var resolution in decided.Select(ticket => ticket.Resolution!))
        {
            toCustomer += resolution.Deposit.RefundToCustomer.Amount;
            toOffice += resolution.Deposit.TransferredToDealer.Amount;
            kept += resolution.Deposit.RetainedByPlatform.Amount;
            if (resolution.DealerCharge is { } charge)
            {
                charged += charge.Amount;
                anyCharge = true;
            }
        }

        var ticketIds = decided.Select(ticket => ticket.Id).ToList();
        var customerRefunds = records
            .SelectMany(record => record.Refunds)
            .Where(refund =>
                refund.Reason == RefundReason.DisputeResolution &&
                refund.DisputeTicketId is { } ticketId &&
                ticketIds.Contains(ticketId))
            .ToList();

        return new DisputeDecision(
            ticketIds,
            decided.Max(ticket => ticket.Resolution!.ResolvedAt),
            Money.Create(toCustomer, currency),
            Money.Create(toOffice, currency),
            Money.Create(kept, currency),
            anyCharge ? Money.Create(charged, currency) : null,
            customerRefunds);
    }

    /// <summary>
    /// The commission's state, by the owner's rules of 2026-09-26 and 2026-09-29 (docs/payments-programme.md).
    /// </summary>
    /// <remarks>
    /// Once the ledger recorded the booking, its payable decides (payments Phase 8): earned as recorded — the frozen
    /// figure, never more than the office's money on the booking — or not earned when that was nothing. Before
    /// that, completed with nothing affecting settlement is earned in full, as it always was.
    /// </remarks>
    private static CommissionPosition Commission(
        Booking booking,
        Payment? confirming,
        List<DisputeTicket> decided,
        RecordedPayable? recorded)
    {
        string state;
        Money? earned = null;
        if (booking.DepositPaymentId is not null && recorded is not null)
        {
            state = recorded.Commission.IsZero ? CommissionStates.NotEarned : CommissionStates.Earned;
            earned = recorded.Commission.IsZero ? null : Fresh(recorded.Commission);
        }
        else if (booking.DepositPaymentId is null)
            state = booking.Status.IsTerminal ? CommissionStates.NotApplicable : CommissionStates.Projected;
        else if (booking.Status == BookingStatus.Completed)
        {
            // Earned only when nothing affects settlement: completed through a dispute, or with money sent back
            // from the payment, waits for the ledger.
            state = decided.Count > 0 || confirming?.Refunds.Count > 0 ? CommissionStates.Undecided : CommissionStates.Earned;
            if (state == CommissionStates.Earned)
                earned = Fresh(booking.Pricing.CommissionAmount);
        }
        else if (booking.Status == BookingStatus.Confirmed || booking.Status == BookingStatus.PickedUp || booking.Status == BookingStatus.Returned)
            state = CommissionStates.Expected;
        else
            // Ended before pickup, paid: nothing is earned once the whole payment is going back; a deposit still
            // held (a customer penalty, a window not yet closed) waits for the ledger.
            state = WholePaymentReturned(confirming) ? CommissionStates.NotEarned : CommissionStates.Undecided;

        return new CommissionPosition(
            Fresh(booking.Pricing.CommissionAmount),
            booking.Terms.CommissionPercent.Value,
            booking.Terms.CommissionBasis.Name,
            state,
            earned);
    }

    // ── The office ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What a final booking comes to for its rental office (payments Phase 8; owner, 2026-09-24 and 2026-09-29): the
    /// ledger records exactly this, and nothing else computes it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Final means nothing can change the booking's money any more: completed (a completed booking cannot be
    /// disputed), or cancelled or a no-show with its own frozen dispute window closed — and in either case no
    /// dispute live. Whether a refund is still on its way does not change what the office is owed, only when it
    /// may be paid, so it is the ledger's to judge, not this.
    /// </para>
    /// <para>
    /// The office's money, by outcome: a rental, everything paid online; a rental a dispute decided, what was paid
    /// above the deposit and its share of the deposit; an ending a dispute decided, its share; a customer's penalty
    /// kept when the window closed with no dispute, the penalty (owner, 2026-09-29: pre-launch item 164, amending
    /// spec 3.3 for this one case); the deposit released or the whole payment returned, nothing. Khadra's commission
    /// is the frozen figure, never more than that money (owner, 2026-09-29). Every charge a resolved dispute
    /// assessed on the office is taken from it, whatever the outcome, which can leave the office owing.
    /// </para>
    /// <para>
    /// A customer's penalty is kept only when it is the whole deposit held: anything less would leave the rest owed
    /// back to the customer, and nothing can return it yet. Such an outcome is final and undetermined, and the
    /// ledger holds it (pre-launch item 205).
    /// </para>
    /// </remarks>
    private static OfficePosition Office(Booking booking, List<DisputeTicket> decided, bool hasLiveDispute, DateTimeOffset now, string currency)
    {
        var zero = Money.ZeroIn(currency);
        if (booking.DepositPaymentId is null)
            return new OfficePosition(OfficeStates.NotApplicable, null, null, [], zero, Money.ZeroIn(currency), Money.ZeroIn(currency), 0m, null);

        var finalAt =
            booking.Status == BookingStatus.Completed ? booking.FinishedAt
            : booking.Status == BookingStatus.Cancelled || booking.Status == BookingStatus.NoShow ? booking.DisputeWindowEndsAt
            : null;
        if (finalAt is not { } final || now < final || hasLiveDispute)
            return new OfficePosition(OfficeStates.Open, null, null, [], zero, Money.ZeroIn(currency), Money.ZeroIn(currency), 0m, null);

        var lines = new List<PayableLineDraft>();
        PayableOutcome outcome;
        if (booking.Status == BookingStatus.Completed)
        {
            if (decided.Count == 0)
            {
                outcome = PayableOutcome.Rental;
                AddLine(lines, PayableLineKind.RentalRevenue, booking.OnlinePaid.Amount, null);
            }
            else
            {
                outcome = PayableOutcome.RentalAfterDispute;
                AddLine(lines, PayableLineKind.RentalRevenue, booking.PaidAboveDeposit.Amount, null);
                AddShares(lines, decided);
            }
        }
        else if (booking.ReturnsWholePayment)
            outcome = PayableOutcome.PaymentReturned;
        else if (decided.Count > 0)
        {
            outcome = PayableOutcome.DisputeDecided;
            AddShares(lines, decided);
        }
        else if (booking.HasPenaltyAgainstCustomer)
        {
            var penalty = booking.Penalty!;
            var held = BookingDisputeSettlement.DepositHeldFor(booking);
            // The booking's own assessment says whether it needs a ticket, so what a customer is told and
            // what the ledger keeps cannot part: nothing is kept that the server says a dispute must decide.
            if (penalty.RequiresTicketToEnforce ||
                !string.Equals(penalty.MaxAmount.CurrencyCode, currency, StringComparison.Ordinal) ||
                penalty.MaxAmount.Amount != held.Amount)
            {
                return new OfficePosition(
                    OfficeStates.Undetermined, PayableOutcome.PenaltyKept, final, [], zero, Money.ZeroIn(currency), Money.ZeroIn(currency), 0m,
                    PayableHoldReason.PenaltyNotWholeDeposit.Name);
            }

            outcome = PayableOutcome.PenaltyKept;
            AddLine(lines, PayableLineKind.PenaltyKept, penalty.MaxAmount.Amount, null);
        }
        else
            outcome = PayableOutcome.DepositReleased;

        var money = lines.Sum(line => line.Amount);
        var commission = Math.Min(booking.Pricing.CommissionAmount.Amount, money);
        AddLine(lines, PayableLineKind.Commission, commission, null);
        foreach (var ticket in decided)
        {
            if (ticket.Resolution!.DealerCharge is { } charge)
                AddLine(lines, PayableLineKind.DisputeCharge, charge.Amount, ticket.Id);
        }

        var charges = lines.Where(line => line.Kind == PayableLineKind.DisputeCharge).Sum(line => line.Amount);
        return new OfficePosition(
            OfficeStates.Final,
            outcome,
            final,
            lines,
            Money.Create(money, currency),
            Money.Create(commission, currency),
            Money.Create(charges, currency),
            money - commission - charges,
            null);
    }

    /// <summary>The office's share of the deposit, one line per dispute that transferred any.</summary>
    private static void AddShares(List<PayableLineDraft> lines, List<DisputeTicket> decided)
    {
        foreach (var ticket in decided)
            AddLine(lines, PayableLineKind.DisputeShare, ticket.Resolution!.Deposit.TransferredToDealer.Amount, ticket.Id);
    }

    /// <summary>A line only for money that exists: a payable carries no zero lines.</summary>
    private static void AddLine(List<PayableLineDraft> lines, PayableLineKind kind, decimal amount, Id? source)
    {
        if (amount > 0m)
            lines.Add(new PayableLineDraft(kind, amount, source));
    }

    /// <summary>Whether everything the payment will ever return is refunded or owed back.</summary>
    private static bool WholePaymentReturned(Payment? payment) =>
        payment?.WholePaymentRefundAmount is { } whole &&
        !whole.IsZero &&
        !whole.IsGreaterThan(payment.RefundedOrOwed);

    // ── Consistency ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The checks that catch a rule that moved: every refund's amount against the rule it was recorded
    /// under, refunds that cannot coexist, an ending whose refund is missing, and dispute shares that
    /// do not add up to the deposit they decided.
    /// </summary>
    private static void Check(
        Booking booking,
        IReadOnlyList<Payment> payments,
        Payment? confirming,
        List<DisputeTicket> decided,
        List<string> issues)
    {
        foreach (var payment in payments)
        {
            foreach (var refund in payment.Refunds)
            {
                if (ExpectedAmount(booking, payment, refund, decided) is { } expected && expected != refund.Amount)
                    issues.Add(FinancialIssues.RefundAmountUnexpected);
            }

            var wholeRefunds = payment.Refunds.Count(refund => refund.Reason.ReturnsWholePayment);
            if (wholeRefunds > 1 ||
                (wholeRefunds == 1 && payment.Refunds.Any(refund =>
                    refund.Reason == RefundReason.EndedBeforePickup || refund.Reason == RefundReason.DisputeWindowClosed)))
            {
                issues.Add(FinancialIssues.RefundsConflict);
            }
        }

        if (confirming is not null)
        {
            if (booking.ReturnsWholePayment && confirming.WholePaymentRefund is null)
                issues.Add(FinancialIssues.EndingRefundMissing);
            if (!booking.RefundableAboveDeposit.IsZero && confirming.RefundFor(RefundReason.EndedBeforePickup) is null)
                issues.Add(FinancialIssues.EndingRefundMissing);

            // A rental that happened owes nothing back but what one of its own resolved disputes decided (payments
            // Phase 8): anything else would be paid to the office AND returned to the customer.
            if (booking.Status == BookingStatus.Completed &&
                confirming.Refunds.Any(refund =>
                    refund.Reason != RefundReason.DisputeResolution ||
                    !decided.Exists(ticket => ticket.Id == refund.DisputeTicketId)))
            {
                issues.Add(FinancialIssues.RefundWithoutCause);
            }

            // The deposit is the customer's OR the office's, never both (payments Phase 8): a release beside a penalty
            // the ledger would keep, or a whole-payment refund on an ending that does not return the whole payment,
            // is a contradiction — the deposit state reads the refund, the office's position reads the booking.
            if (booking.HasPenaltyAgainstCustomer && confirming.RefundFor(RefundReason.DisputeWindowClosed) is not null)
                issues.Add(FinancialIssues.RefundsConflict);
            if (!booking.ReturnsWholePayment && confirming.WholePaymentRefund is not null)
                issues.Add(FinancialIssues.RefundsConflict);

            // What the booking says was paid online is what the office's money is priced from; the payment that
            // confirmed it says what it applied. They are one fact written twice, and must agree.
            if (!string.Equals(confirming.AppliedToBooking.CurrencyCode, booking.OnlinePaid.CurrencyCode, StringComparison.Ordinal) ||
                confirming.AppliedToBooking.Amount != booking.OnlinePaid.Amount)
            {
                issues.Add(FinancialIssues.PaidOnlineDisagrees);
            }
        }

        if (decided.Count > 0)
        {
            var shares = decided.Sum(ticket =>
                ticket.Resolution!.Deposit.RefundToCustomer.Amount +
                ticket.Resolution.Deposit.RetainedByPlatform.Amount +
                ticket.Resolution.Deposit.TransferredToDealer.Amount);
            var released = confirming?.RefundFor(RefundReason.DisputeWindowClosed) is not null;
            if (shares != BookingDisputeSettlement.DepositHeldFor(booking, released).Amount)
                issues.Add(FinancialIssues.DisputeSharesUnbalanced);
        }
    }

    /// <summary>What the rule a refund was recorded under says it must be, or null when no rule fixes it.</summary>
    private static Money? ExpectedAmount(Booking booking, Payment payment, Refund refund, List<DisputeTicket> decided)
    {
        if (refund.Reason == RefundReason.OrphanedCapture)
            return payment.AmountCaptured;
        if (payment.Status != PaymentStatus.Applied)
            return null;
        if (refund.Reason.ReturnsWholePayment)
            return payment.WholePaymentRefundAmount;
        if (refund.Reason == RefundReason.EndedBeforePickup)
            return payment.AmountReturnedFor(booking.PaidAboveDeposit);
        if (refund.Reason == RefundReason.DisputeWindowClosed)
            return payment.HeldDepositRefundAmount(booking.Pricing.DepositAmount);
        if (refund.Reason == RefundReason.DisputeResolution)
        {
            // The share of a resolved ticket of THIS booking — anything else is a refund with no decision behind it.
            var share = decided.FirstOrDefault(ticket => ticket.Id == refund.DisputeTicketId)?.Resolution?.Deposit.RefundToCustomer;
            return share ?? Money.ZeroIn(refund.Amount.CurrencyCode);
        }

        return null;
    }

    private static Money Fresh(Money money) => Money.Create(money.Amount, money.CurrencyCode);
}
