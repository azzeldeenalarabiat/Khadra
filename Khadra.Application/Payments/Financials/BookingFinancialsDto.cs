using Khadra.Application.Common.Dtos;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;

namespace Khadra.Application.Payments.Financials;

/// <summary>
/// A booking's financial state as ONE reader may see it (payments Phase 4): the customer, the rental
/// office, or the administrator. Served by <c>GET /api/v1/bookings/{id}/financials</c> and
/// <c>GET /api/v1/admin/bookings/{id}/financials</c>; a client renders it and never computes a figure.
/// </summary>
/// <remarks>
/// <para>
/// One answer, three projections (owner, 2026-09-26): the customer never sees Khadra's commission, the
/// office's or the platform's dispute share, provider references, failure codes or the sandbox marker;
/// the office never sees a processing fee, the customer's dispute share, or a capture that never
/// applied; the administrator sees all of it, every attempt included. A field a reader may not see is
/// null for that reader.
/// </para>
/// <para>
/// A NEW resource, not part of <c>BookingDto</c>: that is the contract installed apps read, and this
/// one grows in Phases 5–8 (documents, payables). <see cref="BookingStatus"/> and
/// <see cref="GeneratedAt"/> let a client that read the booking and this answer on either side of a
/// transition notice, and read both again.
/// </para>
/// </remarks>
public sealed record BookingFinancialsDto(
    Guid BookingId,
    string BookingStatus,
    string Currency,
    DateTimeOffset GeneratedAt,
    int CalculatorVersion,
    /// <summary>The records contradict one another; the figures are what they say, and somebody will look.</summary>
    bool NeedsReview,
    FinancialSummaryDto Summary,
    FinancialBalanceDto Balance,
    FinancialDepositDto Deposit,
    /// <summary>Khadra's commission. Null for the customer.</summary>
    FinancialCommissionDto? Commission,
    IReadOnlyList<FinancialPaymentDto> Payments,
    /// <summary>What the records contradict (<see cref="FinancialIssues"/>). Administrator only.</summary>
    IReadOnlyList<string>? Issues)
{
    public static BookingFinancialsDto For(BookingFinancials financials, BookingParty reader)
    {
        ArgumentNullException.ThrowIfNull(financials);
        ArgumentNullException.ThrowIfNull(reader);
        var view = Reader.Of(reader);

        return new BookingFinancialsDto(
            financials.BookingId.Value,
            financials.BookingStatus.Name,
            financials.Currency,
            financials.GeneratedAt,
            BookingFinancials.CalculatorVersion,
            financials.NeedsReview,
            FinancialSummaryDto.For(financials.Summary, view),
            FinancialBalanceDto.From(financials.Balance),
            FinancialDepositDto.For(financials.Deposit, view),
            view.Customer ? null : FinancialCommissionDto.From(financials.Commission),
            financials.Payments
                .Where(payment => view.Admin || (view.Office ? payment.Status == PaymentStatus.Applied : payment.Status.IsCaptured))
                .Select(payment => FinancialPaymentDto.For(payment, view))
                .ToList(),
            view.Admin ? financials.Issues : null);
    }
}

/// <summary>Which of the three readers a projection is for.</summary>
internal sealed record Reader(bool Customer, bool Office, bool Admin)
{
    public static Reader Of(BookingParty party) =>
        party == BookingParty.Customer ? new Reader(true, false, false)
        : party == BookingParty.Dealer ? new Reader(false, true, false)
        : party == BookingParty.Admin ? new Reader(false, false, true)
        : throw new ArgumentOutOfRangeException(nameof(party), party.Name, "A financial state is read by the customer, the office or an administrator.");
}

/// <param name="Days">The billed calendar days, frozen on the booking (payments Phase 4b): never counted by a screen.</param>
/// <param name="DailyRate">The frozen daily rate (payments Phase 4b).</param>
/// <param name="DepositPercent">The frozen share of the rental the deposit is (payments Phase 4b).</param>
/// <param name="ProcessingFees">Processing fees on the payments that applied. Null for the office.</param>
/// <param name="ChargedOnline">Everything the card was charged, a capture that never applied included. Null for the office.</param>
/// <param name="Refunded">
/// Refunds back with the customer. For the office: booking money only, without a dispute decision's share.
/// </param>
public sealed record FinancialSummaryDto(
    MoneyDto RentalSubtotal,
    MoneyDto DeliveryFee,
    MoneyDto BookingTotal,
    MoneyDto RequiredDeposit,
    MoneyDto SecurityDeposit,
    MoneyDto PaidOnline,
    MoneyDto? ProcessingFees,
    MoneyDto? ChargedOnline,
    MoneyDto Refunded,
    MoneyDto RefundInProgress,
    MoneyDto RefundDelayed,
    int Days,
    MoneyDto DailyRate,
    decimal DepositPercent)
{
    internal static FinancialSummaryDto For(FinancialSummary summary, Reader view)
    {
        var refunds = view.Office ? summary.OfficeRefunds : summary.Refunds;
        return new FinancialSummaryDto(
            MoneyDto.From(summary.RentalSubtotal),
            MoneyDto.From(summary.DeliveryFee),
            MoneyDto.From(summary.BookingTotal),
            MoneyDto.From(summary.RequiredDeposit),
            MoneyDto.From(summary.SecurityDeposit),
            MoneyDto.From(summary.PaidOnline),
            view.Office ? null : MoneyDto.From(summary.ProcessingFees),
            view.Office ? null : MoneyDto.From(summary.ChargedOnline),
            MoneyDto.From(refunds.Settled),
            MoneyDto.From(refunds.InProgress),
            MoneyDto.From(refunds.Delayed),
            summary.Days,
            MoneyDto.From(summary.DailyRate),
            summary.DepositPercent);
    }
}

/// <param name="State">One of <see cref="BalanceStates"/>.</param>
/// <param name="CashRecorded">Cash the office recorded on a handover, shown beside the balance, never compared with it.</param>
public sealed record FinancialBalanceDto(string State, MoneyDto Amount, IReadOnlyList<FinancialCashRecordDto> CashRecorded)
{
    internal static FinancialBalanceDto From(BalancePosition balance) =>
        new(
            balance.State,
            MoneyDto.From(balance.Amount),
            balance.CashRecorded
                .Select(cash => new FinancialCashRecordDto(cash.Handover.Name, MoneyDto.From(cash.Amount), cash.RecordedAt))
                .ToList());
}

/// <param name="Handover">"Pickup" or "Return".</param>
public sealed record FinancialCashRecordDto(string Handover, MoneyDto Amount, DateTimeOffset RecordedAt);

/// <param name="State">One of <see cref="DepositStates"/>.</param>
/// <param name="WindowEndsAt">When the dispute window closes, for a deposit that waits for it.</param>
/// <param name="Refund">The refund that returned or released the deposit.</param>
/// <param name="Decision">What resolved disputes decided, each reader seeing only its own share.</param>
public sealed record FinancialDepositDto(
    string State,
    MoneyDto Amount,
    DateTimeOffset? WindowEndsAt,
    FinancialRefundDto? Refund,
    FinancialDisputeDecisionDto? Decision)
{
    internal static FinancialDepositDto For(DepositPosition deposit, Reader view) =>
        new(
            deposit.State,
            MoneyDto.From(deposit.Amount),
            deposit.WindowEndsAt,
            deposit.Refund is null ? null : FinancialRefundDto.For(deposit.Refund, view),
            deposit.Decision is null ? null : FinancialDisputeDecisionDto.For(deposit.Decision, view));
}

/// <summary>
/// What resolved disputes decided about the deposit, by reader (owner, 2026-09-26): the customer sees
/// only their own share, the office its own share and any charge assessed to it, the administrator all.
/// </summary>
/// <param name="ToCustomerRefundStatus">Where the refund carrying the customer's share is. Customer and administrator.</param>
public sealed record FinancialDisputeDecisionDto(
    IReadOnlyList<Guid> TicketIds,
    DateTimeOffset DecidedAt,
    MoneyDto? ToCustomer,
    string? ToCustomerRefundStatus,
    MoneyDto? ToOffice,
    MoneyDto? ChargedToOffice,
    MoneyDto? KeptByPlatform)
{
    internal static FinancialDisputeDecisionDto For(DisputeDecision decision, Reader view)
    {
        var customerSees = view.Customer || view.Admin;
        var officeSees = view.Office || view.Admin;
        return new FinancialDisputeDecisionDto(
            decision.TicketIds.Select(id => id.Value).ToList(),
            decision.DecidedAt,
            customerSees ? MoneyDto.From(decision.ToCustomer) : null,
            customerSees ? RefundStatusOf(decision.CustomerRefunds) : null,
            officeSees ? MoneyDto.From(decision.ToOffice) : null,
            officeSees ? MoneyDto.FromOptional(decision.ChargedToOffice) : null,
            view.Admin ? MoneyDto.From(decision.KeptByPlatform) : null);
    }

    /// <summary>The status that most needs saying: a refused refund, then one on its way, then settled.</summary>
    private static string? RefundStatusOf(IReadOnlyList<RefundRecord> refunds) =>
        refunds.Count == 0 ? null
        : refunds.FirstOrDefault(refund => refund.Status == RefundStatus.Failed)?.Status.Name
          ?? refunds.FirstOrDefault(refund => refund.Status.IsOutstanding)?.Status.Name
          ?? RefundStatus.Settled.Name;
}

/// <param name="State">One of <see cref="CommissionStates"/>.</param>
/// <param name="Basis">What <paramref name="Percent"/> is a percent of: "OneDay" or "RentalTotal".</param>
public sealed record FinancialCommissionDto(MoneyDto Amount, decimal Percent, string Basis, string State)
{
    internal static FinancialCommissionDto From(CommissionPosition commission) =>
        new(MoneyDto.From(commission.Amount), commission.Percent, commission.Basis, commission.State);
}

/// <summary>One checkout attempt in the booking's history.</summary>
/// <param name="Purpose">"Deposit" or "FullPayment".</param>
/// <param name="Status">"Applied" or "Orphaned" (a capture that could not be applied); an administrator also sees attempts that took no money.</param>
/// <param name="RefundProgress">A <see cref="Khadra.Domain.Payments.RefundProgress"/> name: the payment's own verdict, read for the office over the refunds it is shown.</param>
/// <param name="AppliedToBooking">What went towards the booking, fees excluded; zero for a capture that never applied.</param>
/// <param name="AmountCharged">What the card was charged, the fee included. Null for the office.</param>
/// <param name="ProcessingFee">The fee inside the charge. Null for the office.</param>
/// <param name="FeeRefundable">Whether the fee goes back with the payment. Null for the office.</param>
public sealed record FinancialPaymentDto(
    Guid PaymentId,
    string Purpose,
    string Status,
    string RefundProgress,
    DateTimeOffset OccurredAt,
    MoneyDto AppliedToBooking,
    MoneyDto? AmountCharged,
    MoneyDto? ProcessingFee,
    bool? FeeRefundable,
    IReadOnlyList<FinancialRefundDto> Refunds,
    /// <summary>When the attempt was opened. Administrator only.</summary>
    DateTimeOffset? CreatedAt,
    /// <summary>Whether no real money moved. Administrator only (owner, 2026-09-23: customers are never told).</summary>
    bool? IsSandbox,
    /// <summary>The provider's own reference. Administrator only.</summary>
    string? ProviderReference,
    /// <summary>The provider's code for a failed attempt. Administrator only.</summary>
    string? FailureCode,
    /// <summary>Why a capture could not be applied. Administrator only.</summary>
    string? OrphanReason)
{
    internal static FinancialPaymentDto For(PaymentRecord payment, Reader view)
    {
        var seesFees = !view.Office;
        return new FinancialPaymentDto(
            payment.PaymentId.Value,
            payment.Purpose.Name,
            payment.Status.Name,
            (view.Office ? payment.OfficeRefundProgress : payment.RefundProgress).Name,
            payment.OccurredAt,
            MoneyDto.From(payment.AppliedToBooking),
            seesFees ? MoneyDto.From(payment.AmountCharged) : null,
            seesFees ? MoneyDto.From(payment.ProcessingFee) : null,
            seesFees ? payment.FeeRefundable : null,
            payment.Refunds
                // The office reads booking money only: a dispute decision's share is the customer's.
                .Where(refund => !view.Office || refund.Reason != RefundReason.DisputeResolution)
                .Select(refund => FinancialRefundDto.For(refund, view))
                .ToList(),
            view.Admin ? payment.CreatedAt : null,
            view.Admin ? payment.IsSandbox : null,
            view.Admin ? payment.ProviderReference : null,
            view.Admin ? payment.FailureCode : null,
            view.Admin ? payment.OrphanReason : null);
    }
}

/// <summary>One refund in the booking's history.</summary>
/// <param name="Reason">
/// <c>FreeCancellation</c>, <c>PlatformCancellation</c>, <c>EndedBeforePickup</c>, <c>DisputeWindowClosed</c>,
/// <c>DisputeResolution</c> or <c>OrphanedCapture</c>; a client words one it does not know as a plain refund.
/// </param>
/// <param name="Status"><c>Requested</c>, <c>Sent</c>, <c>Settled</c> or <c>Failed</c> (refused, still owed, being sent again).</param>
/// <param name="Amount">What goes back — for the office, the booking's money only.</param>
/// <param name="FeePart">The processing fee inside <paramref name="Amount"/>. Null for the office.</param>
public sealed record FinancialRefundDto(
    Guid RefundId,
    Guid PaymentId,
    string Reason,
    string Status,
    MoneyDto Amount,
    MoneyDto? FeePart,
    DateTimeOffset RequestedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? SettledAt,
    DateTimeOffset? FailedAt,
    Guid? DisputeTicketId,
    /// <summary>The provider's reference for the refund. Administrator only.</summary>
    string? ProviderReference,
    /// <summary>The provider's code for a refusal. Administrator only.</summary>
    string? FailureCode,
    /// <summary>
    /// The booking money inside the refund, beside <c>FeePart</c> (payments Phase 4b), so no screen
    /// subtracts one from the other. For the office it equals <c>Amount</c>, which is already booking
    /// money only.
    /// </summary>
    MoneyDto BookingPart)
{
    internal static FinancialRefundDto For(RefundRecord refund, Reader view) =>
        new(
            refund.RefundId.Value,
            refund.PaymentId.Value,
            refund.Reason.Name,
            refund.Status.Name,
            MoneyDto.From(view.Office ? refund.BookingPart : refund.Amount),
            view.Office ? null : MoneyDto.From(refund.FeePart),
            refund.RequestedAt,
            refund.SentAt,
            refund.SettledAt,
            refund.FailedAt,
            refund.DisputeTicketId?.Value,
            view.Admin ? refund.ProviderReference : null,
            view.Admin ? refund.FailureCode : null,
            MoneyDto.From(refund.BookingPart));
}
