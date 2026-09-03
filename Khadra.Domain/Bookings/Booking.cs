using CSharpFunctionalExtensions;
using Khadra.Domain.Bookings.Events;
using Khadra.Domain.Common;

namespace Khadra.Domain.Bookings;

// One rental, from request to settlement. This is the heart of the platform, so a few decisions are
// deliberate and worth stating:
//
// - The booking carries its own priced offer and its own copy of the business rules. Nothing an admin
//   or a dealer changes later can rewrite what a customer already agreed to.
// - Every exit is reachable. Abandoned checkouts and ignored requests expire on a timer instead of
//   holding a vehicle hostage for the whole rental period.
// - Penalties are assessed, never charged. Spec 3.3 and 5.5 make "no ticket, no penalty" the default,
//   so money only moves when an Admin resolves a dispute.
// - It is NOT soft-deletable. A rental is a financial record; Cancelled and Expired are its deletes.
public sealed class Booking : AggregateRoot
{
    private readonly List<HandoverRecord> _handovers = [];
    private readonly List<BookingStatusChange> _statusHistory = [];

    public BookingReference Reference { get; private set; } = null!;
    public Id CustomerId { get; private set; }
    public Id DealerId { get; private set; }
    public Id VehicleId { get; private set; }
    public DateRange Period { get; private set; } = null!;
    public PickupMethod PickupMethod { get; private set; } = null!;
    public GeoPoint? DeliveryLocation { get; private set; }
    public BookingPricing Pricing { get; private set; } = null!;
    public BookingTerms Terms { get; private set; } = null!;
    public PaymentOption PaymentOption { get; private set; } = null!;
    public BookingStatus Status { get; private set; } = null!;

    // Set once the deposit clears; also the idempotency key for a retried gateway webhook.
    public Id? DepositPaymentId { get; private set; }
    // The dealer owner or employee who approved or rejected (spec 4.2 accountability).
    public Id? ActedByUserId { get; private set; }
    public BookingParty? CancelledBy { get; private set; }
    public string? CancellationReason { get; private set; }
    public PenaltyAssessment? Penalty { get; private set; }
    // An extension is a separate booking that points back here, never a mutation of the original.
    public Id? ExtendedFromBookingId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset PaymentDeadline { get; private set; }
    public DateTimeOffset? RequestedAt { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    // Capped at the period start, so a booking approved just before pickup cannot be cancelled free
    // after the customer was due to collect the car.
    public DateTimeOffset? FreeCancellationDeadline { get; private set; }
    public DateTimeOffset? PickedUpAt { get; private set; }
    public DateTimeOffset? ReturnedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    public IReadOnlyCollection<HandoverRecord> Handovers => _handovers.AsReadOnly();
    public IReadOnlyCollection<BookingStatusChange> StatusHistory =>
        _statusHistory.OrderBy(change => change.OccurredAt).ToList();

    private Booking()
    {
    }

    private Booking(Id id) : base(id)
    {
    }

    public static Result<Booking, Error> Create(
        Id customerId,
        Id dealerId,
        Id vehicleId,
        DateRange period,
        PickupMethod pickupMethod,
        GeoPoint? deliveryLocation,
        BookingPricing pricing,
        BookingTerms terms,
        PaymentOption paymentOption,
        DateTimeOffset now,
        Id? extendedFromBookingId = null)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(pickupMethod);
        ArgumentNullException.ThrowIfNull(pricing);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(paymentOption);
        if (customerId.IsEmpty || dealerId.IsEmpty || vehicleId.IsEmpty)
            throw new DomainException("A booking requires a customer, a dealer and a vehicle.");

        if (period.Start <= now)
            return BookingErrors.PeriodInThePast;
        if (pricing.Days != period.WholeDays)
            return BookingErrors.PeriodTooShort;
        if (pickupMethod == PickupMethod.Delivery && deliveryLocation is null)
            return BookingErrors.DeliveryLocationRequired;
        if (pickupMethod == PickupMethod.SelfPickup && deliveryLocation is not null)
            return BookingErrors.DeliveryLocationNotAllowed;

        var booking = new Booking(Id.New())
        {
            Reference = BookingReference.New(),
            CustomerId = customerId,
            DealerId = dealerId,
            VehicleId = vehicleId,
            Period = period,
            PickupMethod = pickupMethod,
            DeliveryLocation = deliveryLocation,
            Pricing = pricing,
            Terms = terms,
            PaymentOption = paymentOption,
            Status = BookingStatus.PendingPayment,
            ExtendedFromBookingId = extendedFromBookingId,
            CreatedAt = now,
            PaymentDeadline = now.Add(terms.PaymentWindow)
        };
        booking.RecordTransition(null, BookingStatus.PendingPayment, BookingParty.Customer, customerId, null, now);
        booking.AddDomainEvent(new BookingCreated(booking.Id, customerId, dealerId, vehicleId, now));
        return booking;
    }

    // True while this booking must block any overlapping booking for the same vehicle.
    public bool OccupiesVehicle => Status.HoldsVehicle;

    public bool CanBeReviewed => Status == BookingStatus.Completed;

    /// <summary>
    /// Whether either party can still open a dispute on this booking (spec 3.3).
    ///
    /// The window is the one FROZEN on this booking, never the current setting, and it runs from the
    /// moment the booking finished: ReturnedAt for a car that came back, FinishedAt for one that was
    /// cancelled or never collected. Completed is deliberately excluded -- reaching Completed IS the
    /// settlement window having elapsed, so a dispute afterwards would reopen a closed financial
    /// record.
    /// </summary>
    public bool CanBeDisputed(DateTimeOffset now)
    {
        var finishedAt = Status == BookingStatus.Returned
            ? ReturnedAt
            : Status == BookingStatus.Cancelled || Status == BookingStatus.NoShow
                ? FinishedAt
                : null;

        if (finishedAt is null)
            return false;

        return now < finishedAt.Value.Add(Terms.PostReturnSettlementWindow);
    }

    // Idempotent: payment gateways retry their webhooks, and a retry must not fail or double-charge.
    public UnitResult<Error> ConfirmDepositPaid(Id depositPaymentId, DateTimeOffset now)
    {
        if (depositPaymentId.IsEmpty)
            throw new DomainException("A deposit confirmation requires a payment.");
        if (Status == BookingStatus.Requested && DepositPaymentId == depositPaymentId)
            return UnitResult.Success<Error>();
        if (Status != BookingStatus.PendingPayment)
            return UnitResult.Failure(BookingErrors.NotAwaitingPayment);

        DepositPaymentId = depositPaymentId;
        RequestedAt = now;
        Transition(BookingStatus.Requested, BookingParty.Customer, CustomerId, null, now);
        AddDomainEvent(new BookingRequested(Id, DealerId, now));
        return UnitResult.Success<Error>();
    }

    // The customer opened the checkout and walked away. Releases the vehicle; nobody is at fault.
    public UnitResult<Error> ExpireUnpaid(DateTimeOffset now)
    {
        if (Status != BookingStatus.PendingPayment)
            return UnitResult.Failure(BookingErrors.NotAwaitingPayment);
        if (now < PaymentDeadline)
            return UnitResult.Failure(BookingErrors.PaymentWindowNotElapsed);

        Penalty = PenaltyAssessment.None("The deposit was not paid within the payment window.", Pricing.CurrencyCode, now);
        FinishedAt = now;
        Transition(BookingStatus.Expired, BookingParty.System, null, "Payment window elapsed.", now);
        AddDomainEvent(new BookingExpired(Id, VehicleId, "PaymentWindowElapsed", now));
        return UnitResult.Success<Error>();
    }

    // The dealer never answered and the rental period has arrived. The deposit is refunded in full:
    // the customer did everything asked of them.
    public UnitResult<Error> ExpireUnanswered(DateTimeOffset now)
    {
        if (Status != BookingStatus.Requested)
            return UnitResult.Failure(BookingErrors.NotAwaitingDecision);
        if (now < Period.Start)
            return UnitResult.Failure(BookingErrors.PaymentWindowNotElapsed);

        Penalty = PenaltyAssessment.None("The dealer did not respond before the rental was due to start.", Pricing.CurrencyCode, now);
        FinishedAt = now;
        Transition(BookingStatus.Expired, BookingParty.System, null, "Dealer did not respond.", now);
        AddDomainEvent(new BookingExpired(Id, VehicleId, "DealerDidNotRespond", now));
        return UnitResult.Success<Error>();
    }

    /// <param name="note">
    /// An optional word to the customer -- pickup instructions, a delivery window. It travels as the
    /// transition's reason, so it sits in the status history both parties can read and needs no
    /// field of its own on the booking.
    /// </param>
    public UnitResult<Error> Approve(Id actedByUserId, DateTimeOffset now, string? note = null)
    {
        if (Status != BookingStatus.Requested)
            return UnitResult.Failure(BookingErrors.NotAwaitingDecision);
        if (actedByUserId.IsEmpty)
            return UnitResult.Failure(BookingErrors.ActorCannotDecide);

        ActedByUserId = actedByUserId;
        ApprovedAt = now;
        var freeUntil = now.Add(Terms.FreeCancellationWindow);
        FreeCancellationDeadline = freeUntil > Period.Start ? Period.Start : freeUntil;
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Transition(BookingStatus.Approved, BookingParty.Dealer, actedByUserId, trimmed, now);
        AddDomainEvent(new BookingApproved(Id, DealerId, actedByUserId, now, trimmed));
        return UnitResult.Success<Error>();
    }

    // Rejection is always free for the customer: the dealer declined before any commitment existed.
    public UnitResult<Error> Reject(Id actedByUserId, string reason, DateTimeOffset now)
    {
        if (Status != BookingStatus.Requested)
            return UnitResult.Failure(BookingErrors.NotAwaitingDecision);
        if (string.IsNullOrWhiteSpace(reason))
            return UnitResult.Failure(BookingErrors.ReasonRequired);

        ActedByUserId = actedByUserId;
        Penalty = PenaltyAssessment.None("The dealer rejected the request.", Pricing.CurrencyCode, now);
        FinishedAt = now;
        Transition(BookingStatus.Rejected, BookingParty.Dealer, actedByUserId, reason, now);
        AddDomainEvent(new BookingRejected(Id, DealerId, actedByUserId, reason.Trim(), now));
        return UnitResult.Success<Error>();
    }

    // Spec 2 and 5.5. Before approval nothing is owed by anyone. After approval the free window
    // decides, and past it the canceller is assessed. Nothing is charged here (see PenaltyAssessment).
    public UnitResult<Error> Cancel(BookingParty cancelledBy, Id? actorUserId, string? reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cancelledBy);

        if (Status != BookingStatus.PendingPayment &&
            Status != BookingStatus.Requested &&
            Status != BookingStatus.Approved)
        {
            return UnitResult.Failure(BookingErrors.CannotCancelNow);
        }

        var assessment = AssessCancellation(cancelledBy, now);
        CancelledBy = cancelledBy;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Penalty = assessment;
        FinishedAt = now;
        Transition(BookingStatus.Cancelled, cancelledBy, actorUserId, reason, now);
        AddDomainEvent(new BookingCancelled(
            Id,
            VehicleId,
            cancelledBy.Name,
            assessment.AttributedTo.Name,
            assessment.MaxAmount.Amount,
            assessment.MaxAmount.CurrencyCode,
            assessment.RequiresTicketToEnforce,
            now));
        return UnitResult.Success<Error>();
    }

    // Spec 5.5: the dealer approved and then failed to hand the car over. The penalty is a RANGE
    // because the owner has not settled on a tier (spec 2.2); an Admin picks inside it on a ticket.
    public UnitResult<Error> ReportDealerNonDelivery(Id customerUserId, string reason, DateTimeOffset now)
    {
        if (Status != BookingStatus.Approved)
            return UnitResult.Failure(BookingErrors.NotApproved);
        if (string.IsNullOrWhiteSpace(reason))
            return UnitResult.Failure(BookingErrors.ReasonRequired);

        CancelledBy = BookingParty.Customer;
        CancellationReason = reason.Trim();
        Penalty = PenaltyAssessment.Range(
            BookingParty.Dealer,
            Terms.DealerPenaltyMinPercent,
            Terms.DealerPenaltyMaxPercent,
            Pricing.RentalTotal,
            "The dealer did not hand over the vehicle after approving the booking.",
            now);
        FinishedAt = now;
        Transition(BookingStatus.Cancelled, BookingParty.Customer, customerUserId, reason, now);
        AddDomainEvent(new BookingCancelled(
            Id,
            VehicleId,
            BookingParty.Customer.Name,
            BookingParty.Dealer.Name,
            Penalty.MaxAmount.Amount,
            Penalty.MaxAmount.CurrencyCode,
            Penalty.RequiresTicketToEnforce,
            now));
        return UnitResult.Success<Error>();
    }

    // Spec 2 and 5.5: the timeout after the start with no pickup recorded.
    //
    // Blame is only assigned for self-pickup, where the customer was the one who had to show up. On a
    // delivery booking the dealer was supposed to travel to the customer, so the system refuses to
    // accuse either side and leaves it to an Admin if anyone opens a ticket.
    public UnitResult<Error> MarkNoShow(DateTimeOffset now)
    {
        if (Status != BookingStatus.Approved)
            return UnitResult.Failure(BookingErrors.NotApproved);

        var deadline = Period.Start.Add(Terms.NoShowTimeout);
        if (now < deadline)
            return UnitResult.Failure(BookingErrors.NoShowTooEarly);

        Penalty = PickupMethod == PickupMethod.SelfPickup
            ? PenaltyAssessment.Fixed(
                BookingParty.Customer,
                Percentage.FromValidated(100m),
                Pricing.DepositAmount,
                "The customer did not collect the vehicle within the no-show window.",
                now)
            : PenaltyAssessment.None(
                "The vehicle was never handed over on a delivery booking; responsibility is undetermined.",
                Pricing.CurrencyCode,
                now);

        FinishedAt = now;
        Transition(BookingStatus.NoShow, BookingParty.System, null, "No-show window elapsed.", now);
        AddDomainEvent(new BookingMarkedNoShow(Id, VehicleId, Penalty.AttributedTo.Name, now));
        return UnitResult.Success<Error>();
    }

    public Result<HandoverRecord, Error> RecordPickup(
        BookingParty recordedBy,
        Id recordedByUserId,
        DateTimeOffset now,
        IEnumerable<string>? photoStorageKeys = null,
        int? odometerKm = null,
        decimal? fuelLevel = null,
        string? notes = null,
        Money? cashCollected = null)
    {
        if (Status != BookingStatus.Approved)
            return BookingErrors.NotApproved;
        if (_handovers.Any(handover => handover.Type == HandoverType.Pickup))
            return BookingErrors.HandoverAlreadyRecorded;

        var record = HandoverRecord.Create(
            Id, HandoverType.Pickup, recordedBy, recordedByUserId, now,
            photoStorageKeys, odometerKm, fuelLevel, notes, cashCollected);
        if (record.IsFailure)
            return record.Error;

        _handovers.Add(record.Value);
        PickedUpAt = now;
        Transition(BookingStatus.PickedUp, recordedBy, recordedByUserId, null, now);
        AddDomainEvent(new BookingPickedUp(Id, DealerId, VehicleId, now));
        return record.Value;
    }

    public Result<HandoverRecord, Error> RecordReturn(
        BookingParty recordedBy,
        Id recordedByUserId,
        DateTimeOffset now,
        IEnumerable<string>? photoStorageKeys = null,
        int? odometerKm = null,
        decimal? fuelLevel = null,
        string? notes = null,
        Money? cashCollected = null)
    {
        if (Status != BookingStatus.PickedUp)
            return BookingErrors.NotPickedUp;
        if (_handovers.Any(handover => handover.Type == HandoverType.Return))
            return BookingErrors.HandoverAlreadyRecorded;

        var record = HandoverRecord.Create(
            Id, HandoverType.Return, recordedBy, recordedByUserId, now,
            photoStorageKeys, odometerKm, fuelLevel, notes, cashCollected);
        if (record.IsFailure)
            return record.Error;

        _handovers.Add(record.Value);
        ReturnedAt = now;
        Transition(BookingStatus.Returned, recordedBy, recordedByUserId, null, now);
        AddDomainEvent(new BookingReturned(Id, VehicleId, now));
        return record.Value;
    }

    // The quiet path to Completed: the settlement window passed and nobody raised a dispute.
    // `hasOpenDispute` is supplied by the application layer, because tickets live in another context.
    public UnitResult<Error> Settle(DateTimeOffset now, bool hasOpenDispute)
    {
        if (Status != BookingStatus.Returned)
            return UnitResult.Failure(BookingErrors.NotReturned);
        if (hasOpenDispute)
            return UnitResult.Failure(BookingErrors.DisputeOpen);
        if (now < ReturnedAt!.Value.Add(Terms.PostReturnSettlementWindow))
            return UnitResult.Failure(BookingErrors.SettlementTooEarly);

        return CompleteInternal(BookingParty.System, null, "Settlement window elapsed with no dispute.", now);
    }

    /// <summary>
    /// Closes the booking out after an Admin resolved a dispute on it.
    ///
    /// Named "close", not "settle", because for most disputes there is nothing to transition: a
    /// booking can be disputed once it is Returned, Cancelled or NoShow, and the last two are already
    /// terminal. Only a Returned booking moves, skipping the rest of its settlement window because the
    /// question the window existed to answer has now been answered.
    ///
    /// A terminal booking succeeds as a no-op rather than failing. The alternative made the resolve
    /// handler's success depend on which way the booking happened to end, which is not something an
    /// Admin resolving a ticket should have to think about.
    /// </summary>
    public UnitResult<Error> CloseAfterDisputeResolved(Id adminUserId, DateTimeOffset now)
    {
        if (Status.IsTerminal)
            return UnitResult.Success<Error>();
        if (Status != BookingStatus.Returned)
            return UnitResult.Failure(BookingErrors.NotReturned);

        return CompleteInternal(BookingParty.System, adminUserId, "Dispute resolved.", now);
    }

    private UnitResult<Error> CompleteInternal(BookingParty actorParty, Id? actorUserId, string reason, DateTimeOffset now)
    {
        FinishedAt = now;
        Transition(BookingStatus.Completed, actorParty, actorUserId, reason, now);
        AddDomainEvent(new BookingCompleted(Id, CustomerId, DealerId, now));
        return UnitResult.Success<Error>();
    }

    private PenaltyAssessment AssessCancellation(BookingParty cancelledBy, DateTimeOffset now)
    {
        // Nothing has been promised until the dealer approves, so cancelling before that is free.
        if (Status != BookingStatus.Approved)
            return PenaltyAssessment.None("Cancelled before the dealer approved the booking.", Pricing.CurrencyCode, now);

        if (FreeCancellationDeadline is not null && now <= FreeCancellationDeadline.Value)
            return PenaltyAssessment.None("Cancelled inside the free cancellation window.", Pricing.CurrencyCode, now);

        if (cancelledBy == BookingParty.Customer)
        {
            return PenaltyAssessment.Fixed(
                BookingParty.Customer,
                Terms.CustomerCancellationPenaltyPercent,
                Pricing.DepositAmount,
                "The customer cancelled after the free cancellation window.",
                now);
        }

        if (cancelledBy == BookingParty.Dealer)
        {
            return PenaltyAssessment.Range(
                BookingParty.Dealer,
                Terms.DealerPenaltyMinPercent,
                Terms.DealerPenaltyMaxPercent,
                Pricing.RentalTotal,
                "The dealer cancelled after the free cancellation window.",
                now);
        }

        return PenaltyAssessment.None("Cancelled by the platform.", Pricing.CurrencyCode, now);
    }

    private void Transition(
        BookingStatus to,
        BookingParty actorParty,
        Id? actorUserId,
        string? reason,
        DateTimeOffset now)
    {
        RecordTransition(Status, to, actorParty, actorUserId, reason, now);
        Status = to;
    }

    private void RecordTransition(
        BookingStatus? from,
        BookingStatus to,
        BookingParty actorParty,
        Id? actorUserId,
        string? reason,
        DateTimeOffset now) =>
        _statusHistory.Add(BookingStatusChange.Record(Id, from, to, actorParty, actorUserId, reason, now));
}
