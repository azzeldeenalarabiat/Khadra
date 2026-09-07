using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common.Dtos;
using Khadra.Domain.Bookings;

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
    // Terms.CommissionPercent of Pricing.RentalTotal, at the FROZEN rate, rounded the way a charge
    // would be. Computed here so no screen ever multiplies money in the browser.
    MoneyDto CommissionAmount,
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
    /// <summary>Whether this booking may be rated right now: the aggregate's own rule, not a status check.</summary>
    bool CanBeReviewed,
    /// <summary>The customer's review of it, if they have already left one.</summary>
    Guid? MyReviewId,
    VehicleLabel? Vehicle,
    string DealerName,
    string CustomerName,
    IReadOnlyList<HandoverDto> Handovers,
    IReadOnlyList<BookingStatusChangeDto> History)
{
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
            BookingPricingDto.From(booking.Pricing),
            BookingTermsDto.From(booking.Terms),
            MoneyDto.From(booking.Terms.CommissionPercent.Of(booking.Pricing.RentalTotal)),
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
            CancellationPreviewDto.From(booking.PreviewCancellation(BookingParty.Customer, now)),
            context.LiveDisputeId,
            booking.CanBeReviewed,
            context.MyReviewId,
            context.Vehicle,
            context.DealerName,
            context.CustomerName,
            booking.Handovers.OrderBy(handover => handover.RecordedAt).Select(HandoverDto.From).ToList(),
            booking.StatusHistory.OrderBy(change => change.OccurredAt).Select(BookingStatusChangeDto.From).ToList());
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
    double FreeCancellationWindowHours,
    double NoShowTimeoutHours,
    double PaymentWindowHours,
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
            terms.FreeCancellationWindow.TotalHours,
            terms.NoShowTimeout.TotalHours,
            terms.PaymentWindow.TotalHours,
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
    string Reason,
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
    DateTimeOffset RecordedAt)
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
            handover.RecordedAt);
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
public sealed record CancellationPreviewDto(bool CanCancel, bool IsFree, PenaltyAssessmentDto Penalty)
{
    public static CancellationPreviewDto From(CancellationPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return new CancellationPreviewDto(
            preview.CanCancel,
            preview.IsFree,
            PenaltyAssessmentDto.From(preview.Penalty)!);
    }
}
