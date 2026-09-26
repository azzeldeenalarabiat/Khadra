using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Payments;
using Khadra.Application.Payments.Dtos;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;

namespace Khadra.Application.Bookings.Dtos;

/// <summary>
/// One booking in full: the aggregate's own state plus the labels it borrows from other contexts.
///
/// The pricing and terms are the FROZEN ones from the booking, never current settings (CLAUDE.md).
/// A screen that showed today's deposit rate against last month's booking would be quietly wrong in
/// exactly the way the snapshot exists to prevent.
/// </summary>
public sealed record BookingDto(
    Guid BookingId,
    string Reference,
    string Status,
    bool IsTerminal,
    Guid CustomerId,
    Guid DealerId,
    Guid VehicleId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string PickupMethod,
    GeoPointDto? DeliveryLocation,
    string PaymentOption,
    BookingPricingDto Pricing,
    BookingTermsDto Terms,
    // Khadra's commission, as frozen on the booking when it was made (Pricing.CommissionAmount), never
    // recomputed here. NULL for the customer: it is an internal figure between the platform and the
    // office, and a customer's copy of their booking carries none (see ForCustomer).
    MoneyDto? CommissionAmount,
    PenaltyAssessmentDto? Penalty,
    string? CancelledBy,
    /// The closed-set code, for a client that renders it in the reader's own language.
    string? CancellationReasonCode,
    /// Whatever the canceller typed, in whatever language they typed it.
    string? CancellationReason,
    DateTimeOffset CreatedAt,
    /// When the dealer must answer by.
    DateTimeOffset DecisionDeadline,
    /// Null until the dealer approves: there is no payment clock before there is a decision.
    DateTimeOffset? PaymentDeadline,
    /// Whether the deposit has actually cleared, rather than a status a screen would have to
    /// interpret. Confirmed onwards is paid, and so is every booking that ended after being paid.
    bool DepositPaid,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? FreeCancellationDeadline,
    DateTimeOffset? PickedUpAt,
    DateTimeOffset? ReturnedAt,
    DateTimeOffset? FinishedAt,
    // Whether a dispute can be opened RIGHT NOW, judged against this booking's own frozen window.
    bool CanBeDisputed,
    /// <summary>Whether the gallery can still answer this request, or its window has closed.</summary>
    /// <remarks>
    /// Status alone does not say. A request past its decision deadline is over -- the car went back
    /// on the market at that instant -- but the row still reads Requested until the settlement job
    /// reaches it. A client must not work this out from <c>DecisionDeadline</c> and its own clock: a
    /// phone whose time is wrong would show a dead booking as live, or the reverse.
    /// </remarks>
    bool IsAwaitingDecision,
    /// <summary>Whether the deposit can still be paid, judged the same way.</summary>
    bool IsAwaitingPayment,
    /// <summary>
    /// What cancelling right now would cost the CUSTOMER, and whether they may.
    /// </summary>
    /// <remarks>
    /// Always the customer's view, because this DTO is the customer's booking. The dealer console
    /// does not offer cancellation and the Admin's is a different action attributed to a different
    /// party. It is here for the same reason <c>CommissionAmount</c> is: so no screen multiplies a
    /// percentage by an amount to find out what somebody is about to agree to.
    /// </remarks>
    CancellationPreviewDto Cancellation,
    Guid? LiveDisputeId,
    /// <summary>
    /// Whether the deposit can be paid right now, and what is in the way if not. Null on a screen
    /// that never offers payment.
    /// </summary>
    PaymentAvailabilityDto? Payment,
    /// <summary>
    /// Whether the customer may report right now that the gallery never handed the car over.
    /// </summary>
    /// <remarks>
    /// Server-computed for the same reason <see cref="IsAwaitingDecision"/> is: the grace is frozen
    /// per booking and the comparison is against the server's clock, so a phone with a wrong time
    /// would otherwise offer the button early and be refused, or hide it when it was due.
    /// </remarks>
    bool CanReportNonDelivery,
    /// <summary>The instant that becomes true, so a screen can say when instead of just "not yet".</summary>
    DateTimeOffset NonDeliveryReportableFrom,
    /// <summary>Whether this booking may be rated right now: the aggregate's own rule, not a status check.</summary>
    bool CanBeReviewed,
    /// <summary>The customer's review of it, if they have already left one.</summary>
    Guid? MyReviewId,
    VehicleLabel? Vehicle,
    /// <summary>The dealership's name, or an English stand-in when <see cref="DealerRemoved"/> is true.</summary>
    /// <remarks>
    /// Kept a string, stand-in included, because shipped customer apps print it as it arrives. A client
    /// that words the case in its reader's language reads the flag instead and never shows the stand-in.
    /// </remarks>
    string DealerName,
    /// <summary>True exactly when the dealership no longer resolves: it is no longer on the platform.</summary>
    bool DealerRemoved,
    /// <summary>The city the dealership operates from, by lookup id. Null when it has none.</summary>
    /// <remarks>
    /// An id, as every other read model on this platform sends a city; the client names it in its
    /// own reader's language from <c>/api/v1/cities</c>.
    /// </remarks>
    Guid? DealerCityId,
    /// <summary>The customer's name, or an English stand-in when <see cref="CustomerAccountClosed"/> is true.</summary>
    string CustomerName,
    /// <summary>True exactly when the customer's account no longer resolves: it was closed.</summary>
    bool CustomerAccountClosed,
    IReadOnlyList<HandoverDto> Handovers,
    IReadOnlyList<BookingStatusChangeDto> History,
    /// <summary>
    /// The deposit's refund when a free cancellation returned it, or null. Added 2026-09-24, last so
    /// that no installed client's reading of the fields before it changes.
    /// </summary>
    DepositRefundDto? DepositRefund = null,
    /// <summary>
    /// What the customer has paid online towards the booking, fees excluded: zero until a payment
    /// confirms it, then the deposit or the whole total. Added 2026-09-24, last.
    /// </summary>
    MoneyDto? OnlinePaid = null,
    /// <summary>
    /// Whether the whole total has been paid online (Booking.IsPaidInFull), the verdict every "paid
    /// in full" sentence keys on. Added 2026-09-25, last; an installed app that does not read it
    /// keeps its deposit wording.
    /// </summary>
    bool IsPaidInFull = false,
    /// <summary>
    /// The payment that confirmed the booking — its purpose and what it charged — or null while none
    /// has. Added 2026-09-25, last.
    /// </summary>
    ConfirmingPaymentDto? ConfirmingPayment = null,
    /// <summary>
    /// Every refund against this booking's payments, oldest first: reason, amount and status (Phase 3,
    /// 2026-09-26). Empty when there is none. Added last.
    /// </summary>
    IReadOnlyList<RefundDto>? Refunds = null,
    /// <summary>
    /// What has actually reached the customer — the settled refunds — in the booking's currency. Zero
    /// when nothing has. Added 2026-09-26, last.
    /// </summary>
    MoneyDto? RefundedAmount = null,
    /// <summary>
    /// What is promised back and not there yet — requested, sent, or refused and being sent again — in
    /// the booking's currency. Zero when nothing is. Added 2026-09-26, last.
    /// </summary>
    MoneyDto? RefundOutstandingAmount = null)
{
    /// <summary>The customer's copy: the same booking without Khadra's commission on it.</summary>
    /// <remarks>
    /// The commission is between the platform and the office. The PERCENT stays on the terms, because
    /// installed customer apps parse it; the amount, which no customer client has ever read, does not
    /// travel to a customer at all.
    /// </remarks>
    public BookingDto ForCustomer() => this with { CommissionAmount = null };

    public static BookingDto From(Booking booking, BookingContext context, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(context);

        return new BookingDto(
            booking.Id.Value,
            booking.Reference.Value,
            booking.Status.Name,
            booking.Status.IsTerminal,
            booking.CustomerId.Value,
            booking.DealerId.Value,
            booking.VehicleId.Value,
            booking.Period.Start,
            booking.Period.End,
            booking.PickupMethod.Name,
            booking.DeliveryLocation is null
                ? null
                : new GeoPointDto(booking.DeliveryLocation.Latitude, booking.DeliveryLocation.Longitude),
            booking.PaymentOption.Name,
            // BalanceDue is served as what is STILL owed: the frozen cash balance until a payment
            // lands, then the total less what was paid online. Installed apps print this field as
            // "pay at handover", and a customer who paid in full must not be told to bring cash.
            BookingPricingDto.From(booking.Pricing) with { BalanceDue = MoneyDto.From(booking.RemainingBalance) },
            BookingTermsDto.From(booking.Terms),
            MoneyDto.From(booking.Pricing.CommissionAmount),
            PenaltyAssessmentDto.From(booking.Penalty),
            booking.CancelledBy?.Name,
            booking.CancellationReasonCode,
            booking.CancellationReason,
            booking.CreatedAt,
            booking.DecisionDeadline,
            booking.PaymentDeadline,
            booking.DepositPaymentId is not null,
            booking.RequestedAt,
            booking.ApprovedAt,
            booking.FreeCancellationDeadline,
            booking.PickedUpAt,
            booking.ReturnedAt,
            booking.FinishedAt,
            booking.CanBeDisputed(now),
            booking.IsAwaitingDecision(now),
            booking.IsAwaitingPayment(now),
            CancellationPreviewDto.From(
                booking.PreviewCancellation(BookingParty.Customer, now),
                booking.CancellationWouldReturnDeposit(BookingParty.Customer, now),
                CancellationRefund(booking, context.ConfirmingPayment, now)),
            context.LiveDisputeId,
            context.Payment,
            booking.CanReportNonDelivery(now),
            booking.NonDeliveryReportableFrom,
            booking.CanBeReviewed,
            context.MyReviewId,
            context.Vehicle,
            context.DealerName,
            context.DealerRemoved,
            context.DealerCityId,
            context.CustomerName,
            context.CustomerAccountClosed,
            booking.Handovers.OrderBy(handover => handover.RecordedAt).Select(HandoverDto.From).ToList(),
            booking.StatusHistory.OrderBy(change => change.OccurredAt).Select(BookingStatusChangeDto.From).ToList(),
            context.DepositRefund,
            MoneyDto.From(booking.OnlinePaid),
            booking.IsPaidInFull,
            context.ConfirmingPayment,
            context.Refunds ?? [],
            RefundTotal(context.Refunds, booking.Pricing.CurrencyCode, settled: true),
            RefundTotal(context.Refunds, booking.Pricing.CurrencyCode, settled: false));
    }

    /// <summary>
    /// What cancelling right now would return to the customer's card: the figure the cancel sheet
    /// states and <c>expectedRefund</c> sends back. The same rule the cancellation's own guard reads
    /// (<see cref="BookingEndingRefunds.PreviewForCustomer(Booking, Money?, Money, DateTimeOffset)"/>),
    /// fed from the payment's read model.
    /// </summary>
    private static MoneyDto? CancellationRefund(Booking booking, ConfirmingPaymentDto? payment, DateTimeOffset now)
    {
        if (payment is null)
            return null;

        var currency = payment.RefundOnFreeCancellation.Currency;
        var refund = BookingEndingRefunds.PreviewForCustomer(
            booking,
            Money.Create(payment.RefundOnFreeCancellation.Amount, currency),
            payment.RefundableFee is { } fee ? Money.Create(fee.Amount, fee.Currency) : Money.ZeroIn(currency),
            now);
        return refund is null ? null : MoneyDto.From(refund);
    }

    /// <summary>
    /// The settled refunds, or the ones still on their way, totalled in the booking's currency. A
    /// refund in another currency — only ever an orphaned capture, which may be why it was orphaned —
    /// is listed on its own and never added to a figure it cannot be part of.
    /// </summary>
    private static MoneyDto RefundTotal(IReadOnlyList<RefundDto>? refunds, string currency, bool settled)
    {
        var total = (refunds ?? [])
            .Where(refund => string.Equals(refund.Amount.Currency, currency, StringComparison.Ordinal))
            .Where(refund => (refund.Status == RefundStatus.Settled.Name) == settled)
            .Sum(refund => refund.Amount.Amount);
        return MoneyDto.From(Money.Create(total, currency));
    }
}

public sealed record GeoPointDto(double Latitude, double Longitude);

public sealed record BookingPricingDto(
    MoneyDto DailyRate,
    // The Amman calendar dates the rental was priced between, and the days they produced. All three
    // are frozen on the booking. A client renders these; it never counts days of its own.
    DateOnly PickupDate,
    DateOnly ReturnDate,
    int Days,
    MoneyDto RentalTotal,
    MoneyDto DeliveryFee,
    MoneyDto TotalPrice,
    decimal DepositPercent,
    MoneyDto DepositAmount,
    MoneyDto BalanceDue,
    MoneyDto SecurityDeposit,
    bool MileageUnlimited,
    int? MileageDailyLimitKm,
    MoneyDto? MileageExcessFeePerKm,
    string FuelPolicy)
{
    public static BookingPricingDto From(BookingPricing pricing)
    {
        ArgumentNullException.ThrowIfNull(pricing);
        return new BookingPricingDto(
            MoneyDto.From(pricing.DailyRate),
            pricing.PickupDate,
            pricing.ReturnDate,
            pricing.Days,
            MoneyDto.From(pricing.RentalTotal),
            MoneyDto.From(pricing.DeliveryFee),
            MoneyDto.From(pricing.TotalPrice),
            pricing.DepositPercent.Value,
            MoneyDto.From(pricing.DepositAmount),
            MoneyDto.From(pricing.BalanceDue),
            MoneyDto.From(pricing.SecurityDeposit),
            pricing.Mileage.IsUnlimited,
            pricing.Mileage.DailyLimitKm,
            MoneyDto.FromOptional(pricing.Mileage.ExcessFeePerKm),
            pricing.FuelPolicy.Name);
    }
}

public sealed record BookingTermsDto(
    decimal DepositPercent,
    decimal CommissionPercent,
    /// <summary>What CommissionPercent is a percent of: "OneDay" or "RentalTotal". Added 2026-09-24.</summary>
    string CommissionBasis,
    double FreeCancellationWindowHours,
    double NoShowTimeoutHours,
    double PaymentWindowHours,
    /// <summary>How long the gallery had to answer this request.</summary>
    /// <remarks>
    /// Frozen on the booking like every other figure here, and sent because a client otherwise has
    /// to DERIVE it from `DecisionDeadline - CreatedAt` to say "they have 48 hours to answer" — a
    /// second source for a number the server already owns, and one that rounds differently.
    /// </remarks>
    double AnswerWindowHours,
    double PostReturnSettlementWindowHours,
    decimal CustomerCancellationPenaltyPercent,
    decimal DealerPenaltyMinPercent,
    decimal DealerPenaltyMaxPercent,
    int RulesVersion)
{
    public static BookingTermsDto From(BookingTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        return new BookingTermsDto(
            terms.DepositPercent.Value,
            terms.CommissionPercent.Value,
            terms.CommissionBasis.Name,
            terms.FreeCancellationWindow.TotalHours,
            terms.NoShowTimeout.TotalHours,
            terms.PaymentWindow.TotalHours,
            terms.AnswerWindow.TotalHours,
            terms.PostReturnSettlementWindow.TotalHours,
            terms.CustomerCancellationPenaltyPercent.Value,
            terms.DealerPenaltyMinPercent.Value,
            terms.DealerPenaltyMaxPercent.Value,
            terms.RulesVersion);
    }
}

/// <summary>What a penalty WOULD be. Assessed, never charged (spec 3.3): only a resolved ticket moves money.</summary>
public sealed record PenaltyAssessmentDto(
    string AttributedTo,
    decimal MinPercent,
    decimal MaxPercent,
    MoneyDto MinAmount,
    MoneyDto MaxAmount,
    bool IsRange,
    bool IsNothingOwed,
    /// <summary>
    /// Always true today, and sent anyway so a screen states the rule from the server's answer rather
    /// than from a sentence typed into it. Spec 3.3: with no ticket, nothing is charged at all.
    /// </summary>
    bool RequiresTicketToEnforce,
    /// <summary>The sentence the platform wrote when it assessed this, frozen on the booking.</summary>
    string Reason,
    /// <summary>The stable code behind that sentence, so a client can word it in its reader's language.</summary>
    /// <remarks>
    /// Null on bookings assessed before codes existed: those keep their sentence, and a client falls
    /// back to it rather than guessing a code from the text.
    /// </remarks>
    string? ReasonCode,
    DateTimeOffset AssessedAt)
{
    public static PenaltyAssessmentDto? From(PenaltyAssessment? penalty) =>
        penalty is null
            ? null
            : new PenaltyAssessmentDto(
                penalty.AttributedTo.Name,
                penalty.MinPercent.Value,
                penalty.MaxPercent.Value,
                MoneyDto.From(penalty.MinAmount),
                MoneyDto.From(penalty.MaxAmount),
                penalty.MinAmount != penalty.MaxAmount,
                penalty.IsNothingOwed,
                penalty.RequiresTicketToEnforce,
                penalty.Reason,
                penalty.ReasonCode?.Name,
                penalty.AssessedAt);
}

public sealed record HandoverDto(
    string Type,
    string RecordedBy,
    Guid RecordedByUserId,
    int? OdometerKm,
    decimal? FuelLevel,
    string? Notes,
    MoneyDto? CashCollected,
    int PhotoCount,
    DateTimeOffset RecordedAt,
    // ADDITIVE (2026-09-23): how the handover was proved. "Code", "Unverified" or "NotRequired";
    // null for handovers recorded before verification existed. An installed app ignores both.
    string? Verification = null,
    string? UnverifiedReason = null)
{
    public static HandoverDto From(HandoverRecord handover)
    {
        ArgumentNullException.ThrowIfNull(handover);
        return new HandoverDto(
            handover.Type.Name,
            handover.RecordedBy.Name,
            handover.RecordedByUserId.Value,
            handover.OdometerKm,
            handover.FuelLevel,
            handover.Notes,
            MoneyDto.FromOptional(handover.CashCollected),
            // A count, not the keys: handover photos are a private shared record between the two
            // parties (spec 5.6) and are served through their own signed path when that ships.
            handover.PhotoStorageKeys.Count,
            handover.RecordedAt,
            handover.Verification?.Name,
            handover.UnverifiedReason);
    }
}

public sealed record BookingStatusChangeDto(
    string? FromStatus,
    string ToStatus,
    string ActorParty,
    Guid? ActorUserId,
    /// <summary>
    /// The closed-set code behind the change, where the actor chose one -- a rejection reason, a
    /// cancellation reason. The label for it travels on <c>/app-config</c> in both languages, so the
    /// customer reads their own language on their own booking.
    /// </summary>
    string? ReasonCode,
    /// <summary>The actor's own words, unchanged. Shown beside the label, never instead of it.</summary>
    string? Reason,
    DateTimeOffset OccurredAt)
{
    public static BookingStatusChangeDto From(BookingStatusChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return new BookingStatusChangeDto(
            change.From?.Name,
            change.To.Name,
            change.ActorParty.Name,
            change.ActorUserId?.Value,
            change.ReasonCode,
            change.Reason,
            change.OccurredAt);
    }
}

/// <summary>
/// Whether this booking can be cancelled right now, and what it would cost.
/// </summary>
/// <remarks>
/// A screen enables its button from <see cref="CanCancel"/> and states the consequence from
/// <see cref="Penalty"/>. Neither is derivable on a client: the first needs the server's clock
/// against two frozen deadlines, the second a percentage applied to money.
/// </remarks>
/// <param name="WillRefundDeposit">
/// True when cancelling now returns the PAID deposit in full to the original payment method (owner,
/// 2026-09-24), from the same rule the cancellation itself applies, so the promise on the sheet and
/// the refund recorded cannot disagree. False on a free cancellation with nothing paid.
/// </param>
/// <param name="RefundAmount">
/// What cancelling now returns to the customer's card, all of it: the whole payment inside the free
/// window, everything above the deposit after it (Phase 3), null when nothing. The figure a client
/// states and sends back as <c>expectedRefund</c>, so a change while the sheet was open is refused
/// with <c>booking.refund_changed</c> instead of cancelling for less. Added 2026-09-26, last.
/// </param>
public sealed record CancellationPreviewDto(
    bool CanCancel,
    bool IsFree,
    PenaltyAssessmentDto Penalty,
    bool WillRefundDeposit = false,
    MoneyDto? RefundAmount = null)
{
    public static CancellationPreviewDto From(
        CancellationPreview preview,
        bool willRefundDeposit = false,
        MoneyDto? refundAmount = null)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return new CancellationPreviewDto(
            preview.CanCancel,
            preview.IsFree,
            PenaltyAssessmentDto.From(preview.Penalty)!,
            willRefundDeposit,
            preview.CanCancel ? refundAmount : null);
    }
}
