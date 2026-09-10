using CSharpFunctionalExtensions;
using Khadra.Domain.Bookings.Events;
using Khadra.Domain.Common;
// Cross-context, and by VALUE only: CustomerDocumentType is IdentityAccess's smart enum, used here
// the way AuditEntry uses UserRole. No navigation, no EF relationship -- the document itself is
// referenced by Id, as the cross-context rule requires.
using Khadra.Domain.IdentityAccess;

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
    private readonly List<RenterDocumentReview> _renterDocumentReviews = [];

    public BookingReference Reference { get; private set; } = null!;
    public Id CustomerId { get; private set; }
    public Id DealerId { get; private set; }
    public Id VehicleId { get; private set; }
    public DateRange Period { get; private set; } = null!;
    /// <summary>
    /// The instant from which this booking claims the vehicle — the period's start, moved earlier by
    /// the turnaround buffer frozen on <see cref="Terms"/>.
    /// </summary>
    /// <remarks>
    /// The customer's period is what they pay for; this is what the gallery's calendar loses. The two
    /// differ by the time it takes to clean and check the car between renters.
    ///
    /// It is a stored column rather than something derived on read, because the database enforces it:
    /// an exclusion constraint over [HoldStart, Period.End) is what actually stops two bookings
    /// holding one car. Postgres will not index or exclude on a computed timestamp — adding an
    /// interval to a `timestamptz` is STABLE, not IMMUTABLE, since the answer depends on the session
    /// time zone — so the aggregate computes it once, here, and the row carries it.
    ///
    /// The pad is on the LEADING edge only. Padding both ends would double-count the gap between two
    /// bookings and refuse a gap exactly equal to the buffer. Padding the trailing edge instead would
    /// make an EXTENSION impossible to insert, since an extension starts precisely where its parent
    /// ends; with a leading pad the extension simply carries none, and the two claims touch without
    /// overlapping.
    /// </remarks>
    public DateTimeOffset HoldStart { get; private set; }
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
    /// <summary>
    /// The closed-set code the canceller chose, or null for a cancellation nobody attributed a reason
    /// to. Kept beside <see cref="CancellationReason"/> so the sentence a customer reads is chosen in
    /// their own language rather than frozen in English on the record.
    /// </summary>
    public string? CancellationReasonCode { get; private set; }

    /// <summary>Whatever the canceller typed beside the code. Their own words.</summary>
    public string? CancellationReason { get; private set; }
    public PenaltyAssessment? Penalty { get; private set; }
    // An extension is a separate booking that points back here, never a mutation of the original.
    public Id? ExtendedFromBookingId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the dealer must have answered by, or the request expires.</summary>
    /// <remarks>
    /// A real column rather than something derived on read, because both the availability query and
    /// the expiry job filter on it and an index has to exist. Capped at the rental start: a request
    /// for a car due out in an hour cannot sit unanswered for two days.
    /// </remarks>
    public DateTimeOffset DecisionDeadline { get; private set; }

    /// <summary>When the deposit must have been paid by. Null until the dealer approves.</summary>
    /// <remarks>
    /// Nullable because the clock does not exist before approval. Under the old order the customer
    /// paid first and this was set at creation; the owner reversed that on 2026-09-07, so a booking
    /// spends its whole Requested life with no payment deadline at all.
    /// </remarks>
    public DateTimeOffset? PaymentDeadline { get; private set; }

    public DateTimeOffset? RequestedAt { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    // Set when the DEPOSIT clears, not when the dealer approves, and capped at the period start so
    // a booking paid just before pickup cannot be cancelled free after the customer was due to
    // collect the car. Null until then: nothing has been paid, so there is nothing to be free of.
    public DateTimeOffset? FreeCancellationDeadline { get; private set; }
    public DateTimeOffset? PickedUpAt { get; private set; }
    public DateTimeOffset? ReturnedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    public IReadOnlyCollection<HandoverRecord> Handovers => _handovers.AsReadOnly();
    public IReadOnlyCollection<BookingStatusChange> StatusHistory =>
        _statusHistory.OrderBy(change => change.OccurredAt).ToList();

    /// <summary>What this dealership has recorded looking at, for this booking's renter (spec 5.1).</summary>
    public IReadOnlyCollection<RenterDocumentReview> RenterDocumentReviews =>
        _renterDocumentReviews.AsReadOnly();

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
        // The pricing must belong to the period being booked. The exact check — that the frozen dates
        // are the period's instants seen through the platform's calendar — needs a time zone, which
        // the domain deliberately does not have. What it can assert without one is that no real zone
        // is more than a day from UTC, so a frozen date further than that from the UTC date of the
        // same instant means the handler priced one period and booked another. That is a bug in the
        // caller, never something a customer can provoke, so it throws rather than returning a Result.
        if (Math.Abs(pricing.PickupDate.DayNumber - DateOnly.FromDateTime(period.Start.UtcDateTime).DayNumber) > 1 ||
            Math.Abs(pricing.ReturnDate.DayNumber - DateOnly.FromDateTime(period.End.UtcDateTime).DayNumber) > 1)
        {
            throw new DomainException(
                "The pricing was calculated for different dates than the period being booked.");
        }
        if (pickupMethod == PickupMethod.Delivery && deliveryLocation is null)
            return BookingErrors.DeliveryLocationRequired;
        if (pickupMethod == PickupMethod.SelfPickup && deliveryLocation is not null)
            return BookingErrors.DeliveryLocationNotAllowed;
        // A customer collecting the car themselves is never charged for delivery. Worth stating here
        // rather than trusting the caller: the fee is now each gallery's own figure, so the handler
        // that creates a booking has to decide when it applies, and this is the aggregate refusing
        // the one combination that can only be a mistake.
        if (pickupMethod == PickupMethod.SelfPickup && !pricing.DeliveryFee.IsZero)
            return BookingErrors.DeliveryFeeNotAllowed;

        var booking = new Booking(Id.New())
        {
            Reference = BookingReference.New(),
            CustomerId = customerId,
            DealerId = dealerId,
            VehicleId = vehicleId,
            Period = period,
            // An extension continues a rental the customer never gave back, so there is no handover
            // to prepare for and no gap to keep. Anything else pads its start by the frozen buffer.
            HoldStart = extendedFromBookingId is null
                ? period.Start.Subtract(terms.TurnaroundBuffer)
                : period.Start,
            PickupMethod = pickupMethod,
            DeliveryLocation = deliveryLocation,
            Pricing = pricing,
            Terms = terms,
            PaymentOption = paymentOption,
            Status = BookingStatus.Requested,
            ExtendedFromBookingId = extendedFromBookingId,
            CreatedAt = now,
            RequestedAt = now,
            // The dealer's clock starts now, capped at the rental start: a request for a car due
            // out in an hour cannot sit unanswered for two days.
            DecisionDeadline = Cap(now.Add(terms.AnswerWindow), period.Start)
        };
        booking.RecordTransition(null, BookingStatus.Requested, BookingParty.Customer, customerId, null, now);
        booking.AddDomainEvent(new BookingCreated(booking.Id, customerId, dealerId, vehicleId, now));
        booking.AddDomainEvent(new BookingRequested(booking.Id, dealerId, now));
        return booking;
    }

    /// <summary>No window may outlive the rental it governs.</summary>
    /// <remarks>
    /// A booking approved twenty minutes before pickup gets twenty minutes to pay, not a day. The
    /// same is true of the answer window and of free cancellation: a deadline past the moment the
    /// customer was due to collect the car is not a deadline.
    /// </remarks>
    private static DateTimeOffset Cap(DateTimeOffset deadline, DateTimeOffset periodStart) =>
        deadline > periodStart ? periodStart : deadline;

    // True while this booking must block any overlapping booking for the same vehicle.
    public bool OccupiesVehicle => Status.HoldsVehicle;

    public bool CanBeReviewed => Status == BookingStatus.Completed;

    /// <summary>
    /// Whether this request is still genuinely waiting for the gallery's answer.
    /// </summary>
    /// <remarks>
    /// Status alone cannot answer it. A request whose decision deadline has passed is over -- the
    /// availability predicate released the car at that instant -- but nothing has rewritten the row,
    /// so it still reads <c>Requested</c> until the settlement job gets to it. The server decides
    /// this, because a client comparing a deadline against its own clock would show a dead booking
    /// as live on any phone whose clock is wrong.
    /// </remarks>
    public bool IsAwaitingDecision(DateTimeOffset now) =>
        Status == BookingStatus.Requested && now < DecisionDeadline;

    /// <summary>Whether the deposit can still be paid on this booking.</summary>
    public bool IsAwaitingPayment(DateTimeOffset now) =>
        Status == BookingStatus.Approved && PaymentDeadline is { } deadline && now < deadline;

    /// <summary>
    /// Whether a window closed on this booking without the row having caught up.
    /// </summary>
    /// <remarks>
    /// The clock has already decided; only the status is behind. Anything acting on the booking must
    /// settle this first, or it writes a decision over an outcome that was no longer anyone's to
    /// make -- "you cancelled this" onto a booking the platform had already released.
    /// </remarks>
    public bool HasLapsed(DateTimeOffset now) =>
        (Status == BookingStatus.Requested && now >= DecisionDeadline) ||
        (Status == BookingStatus.Approved && PaymentDeadline is { } paymentDeadline && now >= paymentDeadline);

    /// <summary>
    /// Whether this booking is still LIVE: it holds the vehicle and no window has closed on it.
    /// </summary>
    /// <remarks>
    /// The in-memory twin of <c>BookingHolds.Live</c>, which asks the same question in SQL. Stated
    /// once, here, because it is now load-bearing for more than availability: it is the predicate a
    /// gallery's access to a customer is granted on -- what they may see about the person they are
    /// deciding about, for as long as they are deciding, and no longer. Two copies of that rule would
    /// drift, and the way it would drift is a gallery keeping access after the booking ended.
    /// </remarks>
    public bool IsLive(DateTimeOffset now) => Status.HoldsVehicle && !HasLapsed(now);

    // ── The renter's paperwork, as this dealership checked it (spec 5.1) ─────────────────────────

    /// <summary>
    /// What this dealership recorded about one particular UPLOAD, or null.
    /// </summary>
    /// <remarks>
    /// Keyed on the upload instant as well as the document id, because a document row is a SLOT --
    /// "your licence front" -- whose file is replaced in place when the renter re-photographs it. A
    /// review found by id alone would answer for a file that no longer exists.
    /// </remarks>
    public RenterDocumentReview? FindRenterDocumentReview(Id documentId, DateTimeOffset documentUploadedAt) =>
        _renterDocumentReviews.SingleOrDefault(review =>
            review.DocumentId == documentId && review.DocumentUploadedAt == documentUploadedAt);

    /// <summary>
    /// Records that this dealership looked at one of the renter's documents.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Gated on <see cref="IsLive"/>, the same predicate that decides whether the gallery may SEE the
    /// document at all. Stating it here rather than only in the handler is what stops the two drifting
    /// -- a dealership able to record a review of something it can no longer open would be writing a
    /// claim it cannot support.
    /// </para>
    /// <para>
    /// Deliberately does NOT settle a booking whose window has lapsed, which is what
    /// <c>CancelMyBookingCommand</c> does on its own path. A gallery opening a licence must not be
    /// able to expire a booking as a side effect of looking at it; the clock's job stays the clock's.
    /// </para>
    /// <para>
    /// Strict about repeats: the caller asks <see cref="FindRenterDocumentReview"/> first and answers
    /// with what is already there. Recording twice would move the timestamp, and "when did this
    /// dealership first check the licence" is the whole value of the record.
    /// </para>
    /// </remarks>
    public Result<RenterDocumentReview, Error> RecordRenterDocumentReview(
        Id documentId,
        CustomerDocumentType documentType,
        DateTimeOffset documentUploadedAt,
        Id reviewedByUserId,
        string reviewedByName,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(documentType);

        if (!IsLive(now))
            return BookingErrors.RenterDocumentsNotAvailable;

        if (FindRenterDocumentReview(documentId, documentUploadedAt) is not null)
            return BookingErrors.RenterDocumentAlreadyReviewed;

        var review = RenterDocumentReview.Record(
            Id, documentId, documentType, documentUploadedAt, reviewedByUserId, reviewedByName, now);
        _renterDocumentReviews.Add(review);
        return review;
    }

    /// <summary>Whether <see cref="Cancel"/> would succeed right now.</summary>
    public bool CanBeCancelled(DateTimeOffset now) =>
        (Status == BookingStatus.Requested ||
         Status == BookingStatus.Approved ||
         Status == BookingStatus.Confirmed) &&
        !HasLapsed(now);

    /// <summary>
    /// What cancelling right now would cost the named party, without cancelling.
    /// </summary>
    /// <remarks>
    /// Shares <see cref="AssessCancellation"/> with the real thing, so the figure on the confirmation
    /// sheet is the figure that would be recorded. It exists for the same reason
    /// <c>BookingDto.CommissionAmount</c> does: no screen may multiply a percentage by an amount to
    /// find out what a customer is about to agree to.
    /// </remarks>
    public CancellationPreview PreviewCancellation(BookingParty cancelledBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cancelledBy);

        return CanBeCancelled(now)
            ? CancellationPreview.Allowed(AssessCancellation(cancelledBy, now))
            : CancellationPreview.NotAllowed(Pricing.CurrencyCode, now);
    }

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
        // Idempotent regardless of status: a webhook retried after the car was collected must
        // still be a success, not a refusal that makes a gateway keep retrying.
        if (DepositPaymentId == depositPaymentId)
            return UnitResult.Success<Error>();
        if (Status != BookingStatus.Approved)
            return UnitResult.Failure(BookingErrors.NotAwaitingPayment);

        DepositPaymentId = depositPaymentId;
        // The free-cancellation window starts at PAYMENT, not at approval. Spec 5.5 measures it
        // from approval because under the old order payment came first, so approval was the moment
        // of commitment. It is not any more: a customer who pays at hour 23 of a 24-hour window
        // would otherwise have a free window that closed 22 hours before they committed anything.
        FreeCancellationDeadline = Cap(now.Add(Terms.FreeCancellationWindow), Period.Start);
        Transition(BookingStatus.Confirmed, BookingParty.Customer, CustomerId, null, now);
        AddDomainEvent(new BookingConfirmed(Id, DealerId, VehicleId, now));
        return UnitResult.Success<Error>();
    }

    /// <summary>The dealer approved and the customer never paid. Nobody is at fault.</summary>
    /// <remarks>
    /// The car is already free by this point: the availability predicate stops counting an approved
    /// booking the moment its deadline passes, without waiting for anything to run. This settles the
    /// status afterwards so both parties can read what happened.
    /// </remarks>
    public UnitResult<Error> ExpireUnpaid(DateTimeOffset now, Id? actorUserId = null)
    {
        if (Status != BookingStatus.Approved)
            return UnitResult.Failure(BookingErrors.NotAwaitingPayment);
        if (PaymentDeadline is not { } deadline || now < deadline)
            return UnitResult.Failure(BookingErrors.PaymentWindowNotElapsed);

        Penalty = PenaltyAssessment.None("The deposit was not paid within the payment window.", Pricing.CurrencyCode, now);
        FinishedAt = now;
        Transition(BookingStatus.Expired, PartyFor(actorUserId), actorUserId, "Payment window elapsed.", now);
        AddDomainEvent(new BookingExpired(Id, VehicleId, "PaymentWindowElapsed", now));
        return UnitResult.Success<Error>();
    }

    /// <summary>The dealer let the answer window close. Nothing is owed by anyone.</summary>
    /// <remarks>
    /// This used to wait for the rental period to arrive, which was harmless while a deposit gated
    /// the hold. It is not now: a request costs nothing, so without a real window one account could
    /// hold a car for the whole booking horizon. Spec 3.1 always promised an answer; this keeps it.
    /// </remarks>
    public UnitResult<Error> ExpireUnanswered(DateTimeOffset now, Id? actorUserId = null)
    {
        if (Status != BookingStatus.Requested)
            return UnitResult.Failure(BookingErrors.NotAwaitingDecision);
        if (now < DecisionDeadline)
            return UnitResult.Failure(BookingErrors.DecisionWindowNotElapsed);

        Penalty = PenaltyAssessment.None("The dealer did not answer within the agreed window.", Pricing.CurrencyCode, now);
        FinishedAt = now;
        Transition(BookingStatus.Expired, PartyFor(actorUserId), actorUserId, "Dealer did not respond.", now);
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
        // Past the answer window the catalogue has already released the car, so an approval now can
        // only collide with whoever took it. Rejecting late is still allowed: it costs nobody
        // anything and closes the record honestly.
        if (now >= DecisionDeadline)
            return UnitResult.Failure(BookingErrors.DecisionWindowElapsed);

        ActedByUserId = actedByUserId;
        ApprovedAt = now;
        // The customer now owes a deposit, and this is their window to pay it. Capped at the rental
        // start, so an approval twenty minutes before pickup gives twenty minutes, not a day.
        PaymentDeadline = Cap(now.Add(Terms.PaymentWindow), Period.Start);
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Transition(BookingStatus.Approved, BookingParty.Dealer, actedByUserId, trimmed, now);
        AddDomainEvent(new BookingApproved(Id, DealerId, actedByUserId, now, trimmed));
        return UnitResult.Success<Error>();
    }

    // Rejection is always free for the customer: the dealer declined before any commitment existed.
    /// <param name="reasonCode">
    /// One of <see cref="BookingRejectionReason"/>. Stored as the code, never as a sentence composed
    /// from it: the customer reading this booking may not read English.
    /// </param>
    /// <param name="details">The gallery's own words. Required, and shown to the customer as typed.</param>
    public UnitResult<Error> Reject(Id actedByUserId, BookingRejectionReason reasonCode, string details, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reasonCode);

        if (Status != BookingStatus.Requested)
            return UnitResult.Failure(BookingErrors.NotAwaitingDecision);
        if (string.IsNullOrWhiteSpace(details))
            return UnitResult.Failure(BookingErrors.ReasonRequired);

        ActedByUserId = actedByUserId;
        Penalty = PenaltyAssessment.None("The dealer rejected the request.", Pricing.CurrencyCode, now);
        FinishedAt = now;
        Transition(BookingStatus.Rejected, BookingParty.Dealer, actedByUserId, details, now, reasonCode.Name);
        AddDomainEvent(new BookingRejected(Id, DealerId, actedByUserId, details.Trim(), now));
        return UnitResult.Success<Error>();
    }

    // Spec 2 and 5.5, as amended 2026-09-07. Before the DEPOSIT clears nothing is owed by anyone --
    // no money has moved, so there is nothing a penalty could bite on, and that now covers an
    // approval the customer has not paid for as well as a request nobody has answered. Once it has
    // cleared the free window decides, and past it the canceller is assessed. Nothing is charged
    // here (see PenaltyAssessment).
    public UnitResult<Error> Cancel(
        BookingParty cancelledBy,
        Id? actorUserId,
        string? reason,
        DateTimeOffset now,
        BookingCancellationReason? reasonCode = null)
    {
        ArgumentNullException.ThrowIfNull(cancelledBy);

        if (Status != BookingStatus.Requested &&
            Status != BookingStatus.Approved &&
            Status != BookingStatus.Confirmed)
        {
            return UnitResult.Failure(BookingErrors.CannotCancelNow);
        }

        var assessment = AssessCancellation(cancelledBy, now);
        CancelledBy = cancelledBy;
        CancellationReasonCode = reasonCode?.Name;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Penalty = assessment;
        FinishedAt = now;
        Transition(BookingStatus.Cancelled, cancelledBy, actorUserId, reason, now, reasonCode?.Name);
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

    /// <summary>
    /// The instant from which the customer may report that the gallery never handed the car over:
    /// the rental start plus the grace frozen on this booking.
    /// </summary>
    /// <remarks>
    /// Exposed so a screen can say WHEN rather than offering a button the server refuses. A client
    /// must not add the grace itself: the grace is frozen per booking, so two bookings made either
    /// side of a settings change have different answers, and only the record knows which.
    /// </remarks>
    public DateTimeOffset NonDeliveryReportableFrom => Period.Start.Add(Terms.NonDeliveryGrace);

    /// <summary>Whether the customer may report non-delivery right now.</summary>
    /// <remarks>
    /// The same three conditions <see cref="ReportDealerNonDelivery"/> enforces, minus the reason,
    /// which the customer has not typed yet. Kept beside it so the button and the command cannot
    /// drift: a screen that enables on this can never be refused for a reason it could have known.
    /// </remarks>
    public bool CanReportNonDelivery(DateTimeOffset now) =>
        Status == BookingStatus.Confirmed && now >= NonDeliveryReportableFrom;

    // Spec 5.5: the dealer approved and then failed to hand the car over. The penalty is a RANGE
    // because the owner has not settled on a tier (spec 2.2); an Admin picks inside it on a ticket.
    public UnitResult<Error> ReportDealerNonDelivery(Id customerUserId, string reason, DateTimeOffset now)
    {
        if (Status != BookingStatus.Confirmed)
            return UnitResult.Failure(BookingErrors.NotConfirmed);
        if (string.IsNullOrWhiteSpace(reason))
            return UnitResult.Failure(BookingErrors.ReasonRequired);
        // A gallery cannot have failed to hand over a car that was not yet due. Without this, a
        // customer facing a cancellation penalty could report non-delivery days ahead of the rental
        // instead, flipping a 25-50% assessment onto the gallery, who would then have to open a
        // dispute to clear a record written without them. MarkNoShow -- the mirror-image claim, that
        // the CUSTOMER never appeared -- has always been guarded this way. This is the other half of
        // the same rule, and its grace is frozen on the booking for the same reason every other
        // window is: a rule the owner changes tomorrow must not re-judge a rental agreed today.
        if (now < Period.Start.Add(Terms.NonDeliveryGrace))
            return UnitResult.Failure(BookingErrors.NonDeliveryTooEarly);

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
    public UnitResult<Error> MarkNoShow(DateTimeOffset now, Id? actorUserId = null)
    {
        if (Status != BookingStatus.Confirmed)
            return UnitResult.Failure(BookingErrors.NotConfirmed);

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
        Transition(BookingStatus.NoShow, PartyFor(actorUserId), actorUserId, "No-show window elapsed.", now);
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
        if (Status != BookingStatus.Confirmed)
            return BookingErrors.NotConfirmed;
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

    /// <summary>
    /// Who moved the booking: a named administrator, or the platform on a timer.
    /// </summary>
    /// <remarks>
    /// These three transitions are the job's work, and the job does not exist yet (pre-launch item
    /// 4), so an Admin triggers them by hand today. The status history has to say which it was --
    /// "System" against an action a person took would misattribute it to a timer that never ran.
    /// Neither party changes the outcome: the penalty each of these assesses is decided by the
    /// booking's own frozen terms, not by who asked.
    /// </remarks>
    private static BookingParty PartyFor(Id? actorUserId) =>
        actorUserId is null ? BookingParty.System : BookingParty.Admin;

    private PenaltyAssessment AssessCancellation(BookingParty cancelledBy, DateTimeOffset now)
    {
        // No money exists until the deposit is paid, so every exit before Confirmed is free -- for
        // EITHER party. A dealer who approves and then cancels before payment is assessed nothing,
        // which is a late rejection in all but name. Assessing a percentage of a deposit nobody has
        // paid would be an assessment with nothing behind it and no rail to collect it on.
        if (Status != BookingStatus.Confirmed)
            return PenaltyAssessment.None("Cancelled before the deposit was paid.", Pricing.CurrencyCode, now);

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
        DateTimeOffset now,
        string? reasonCode = null)
    {
        RecordTransition(Status, to, actorParty, actorUserId, reason, now, reasonCode);
        Status = to;
    }

    private void RecordTransition(
        BookingStatus? from,
        BookingStatus to,
        BookingParty actorParty,
        Id? actorUserId,
        string? reason,
        DateTimeOffset now,
        string? reasonCode = null) =>
        _statusHistory.Add(
            BookingStatusChange.Record(Id, from, to, actorParty, actorUserId, reason, now, reasonCode));
}
