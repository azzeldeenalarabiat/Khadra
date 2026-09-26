using Khadra.Application.Bookings;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Payments;

namespace Khadra.Application.Payments.Financials;

/// <summary>
/// The ONE calculator of a booking's financial state (payments Phase 4, owner 2026-09-26). Every screen
/// — the customer's website and app, the rental office's console, the administrator's — reads its
/// answer; none of them computes a figure of its own.
/// </summary>
/// <remarks>
/// <para>
/// A pure function of the booking, its payments (with their refunds) and its resolved disputes, and it
/// adds NO rule of its own: every state is a reading of a verdict the aggregates already give —
/// <c>Booking.ReturnsWholePayment</c>, <c>RefundableAboveDeposit</c>, <c>HasPenaltyAgainstCustomer</c>,
/// <c>DisputeWindowEndsAt</c>, <c>EndedBeforePickup</c>, <c>IsPaidInFull</c>; <c>Payment.FeeInside</c>,
/// <c>WholePaymentRefundAmount</c>, <c>IsRefundedInFull</c>; the dispute resolutions' own shares. A rule
/// that is missing belongs on the aggregate, not here.
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
    public static BookingFinancials Calculate(
        Booking booking,
        IEnumerable<Payment> payments,
        IEnumerable<DisputeTicket> resolvedTickets,
        bool hasLiveDispute,
        DateTimeOffset now)
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

        var records = own.Select(Record).ToList();

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
            Deposit(booking, confirming, records, decided, hasLiveDispute, now, currency),
            Commission(booking, confirming, decided),
            records,
            issues.Distinct(StringComparer.Ordinal).ToList());
    }

    // ── One payment ────────────────────────────────────────────────────────────────────────────────

    private static PaymentRecord Record(Payment payment)
    {
        var refunds = payment.Refunds
            .OrderBy(refund => refund.RequestedAt)
            .ThenBy(refund => refund.Id.Value)
            .Select(refund => RefundOf(payment, refund))
            .ToList();
        // The office is shown every refund but a dispute decision's share, which is the customer's.
        var officeRefunds = refunds.Where(refund => refund.Reason != RefundReason.DisputeResolution).ToList();

        return new PaymentRecord(
            payment.Id,
            payment.Purpose,
            payment.Status,
            ProgressOf(refunds, WhollyReturned(payment)),
            ProgressOf(officeRefunds, BookingMoneyReturned(payment, officeRefunds)),
            payment.AppliedAt ?? payment.CapturedAt ?? payment.FailedAt ?? payment.CreatedAt,
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
    /// One reading of the statuses of the refunds a reader is shown. A refused refund outranks one on
    /// its way, because it needs a human; whether the rest is complete is the caller's to say.
    /// </summary>
    private static string ProgressOf(List<RefundRecord> refunds, bool complete)
    {
        if (refunds.Count == 0)
            return RefundProgresses.None;
        if (refunds.Any(refund => refund.Status == RefundStatus.Failed))
            return RefundProgresses.Delayed;
        if (refunds.Any(refund => refund.Status.IsOutstanding))
            return RefundProgresses.InProgress;
        return complete ? RefundProgresses.Complete : RefundProgresses.Partial;
    }

    /// <summary>
    /// Whether everything the payment will EVER return has arrived: an orphan returns its whole
    /// capture, an applied payment its <c>WholePaymentRefundAmount</c>.
    /// </summary>
    private static bool WhollyReturned(Payment payment) =>
        payment.Status == PaymentStatus.Orphaned
            ? payment.AmountCaptured is { } captured && !captured.IsGreaterThan(payment.RefundSettled)
            : payment.IsRefundedInFull;

    /// <summary>
    /// The office's "complete": the booking money in the refunds the office is shown covers all the
    /// payment applied to the booking. Judged from those rows alone — never from
    /// <see cref="Payment.IsRefundedInFull"/>, which counts a dispute decision's share too — so the
    /// customer's share can neither complete the office's badge nor be read back out of it.
    /// </summary>
    private static bool BookingMoneyReturned(Payment payment, List<RefundRecord> shown)
    {
        if (payment.Status != PaymentStatus.Applied)
            return false;
        var applied = payment.AppliedToBooking;
        var returned = shown
            .Where(refund => refund.Status == RefundStatus.Settled &&
                string.Equals(refund.BookingPart.CurrencyCode, applied.CurrencyCode, StringComparison.Ordinal))
            .Sum(refund => refund.BookingPart.Amount);
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
        string currency)
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

    /// <summary>The commission's state, by the owner's rules of 2026-09-26 (docs/payments-programme.md).</summary>
    private static CommissionPosition Commission(Booking booking, Payment? confirming, List<DisputeTicket> decided)
    {
        string state;
        if (booking.DepositPaymentId is null)
            state = booking.Status.IsTerminal ? CommissionStates.NotApplicable : CommissionStates.Projected;
        else if (booking.Status == BookingStatus.Completed)
            // Earned only when nothing affects settlement: completed through a dispute, or with money
            // sent back from the payment, is the ledger's (Phase 8) to decide.
            state = decided.Count > 0 || confirming?.Refunds.Count > 0 ? CommissionStates.Undecided : CommissionStates.Earned;
        else if (booking.Status == BookingStatus.Confirmed || booking.Status == BookingStatus.PickedUp || booking.Status == BookingStatus.Returned)
            state = CommissionStates.Expected;
        else
            // Ended before pickup, paid: nothing is earned once the whole payment is going back; a
            // deposit still held (a customer penalty, a window not yet closed) is Phase 8's.
            state = WholePaymentReturned(confirming) ? CommissionStates.NotEarned : CommissionStates.Undecided;

        return new CommissionPosition(
            Fresh(booking.Pricing.CommissionAmount),
            booking.Terms.CommissionPercent.Value,
            booking.Terms.CommissionBasis.Name,
            state);
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
