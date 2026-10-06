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

    /// <summary>
    /// The earliest moment a pickup may be recorded: the instant this booking claims the car,
    /// <see cref="HoldStart"/> (owner, 2026-10-05; E2E F51, pre-launch item 225).
    /// </summary>
    /// <remarks>
    /// Before it the car is not yet this booking's, so a pickup recorded then would describe a rental
    /// that has not begun — and, followed by a return, would complete the booking and earn the office a
    /// rental payable for a rental that never happened. No new frozen number: the turnaround buffer is
    /// already frozen on <see cref="Terms"/>, and an extension, whose hold carries no buffer, may be
    /// collected from its own start. Not mapped; it is <see cref="HoldStart"/> under the name a
    /// handover reads.
    /// </remarks>
    public DateTimeOffset PickupAvailableFrom => HoldStart;

    /// <summary>The earliest moment a return may be recorded: the rental's own start.</summary>
    public DateTimeOffset ReturnAvailableFrom => Period.Start;

    public PickupMethod PickupMethod { get; private set; } = null!;
    public GeoPoint? DeliveryLocation { get; private set; }
    public BookingPricing Pricing { get; private set; } = null!;
    public BookingTerms Terms { get; private set; } = null!;
    public PaymentOption PaymentOption { get; private set; } = null!;
    public BookingStatus Status { get; private set; } = null!;

    // Set once the deposit clears; also the idempotency key for a retried gateway webhook.
    /// <summary>
    /// The payment that CONFIRMED this booking — its deposit, or its full amount. The name predates the
    /// full-payment option (2026-09-24) and is kept because the column and its idempotency are unchanged.
    /// </summary>
    public Id? DepositPaymentId { get; private set; }

    // What the customer has paid online towards this booking, in its own currency. A bare figure, like
    // a payment's fee: the currency is the booking's and a second column could only disagree with it.
    private decimal _onlinePaid;

    /// <summary>
    /// What the customer has paid ONLINE towards the booking (processing fees excluded): zero until a
    /// payment confirms it, then the deposit or the full total.
    /// </summary>
    /// <remarks>
    /// It lives on the booking, not only in the payment rows, because three of the booking's own rules
    /// read it: the cash still owed at handover, the amount at stake in a dispute, and the refund a
    /// cancellation returns.
    /// </remarks>
    public Money OnlinePaid => Money.Create(_onlinePaid, Pricing.CurrencyCode);

    /// <summary>
    /// What is still owed on the booking itself: its total less what was paid online. On a deposit-only
    /// booking that is the cash the office collects at handover; on a fully paid one it is nothing.
    /// </summary>
    public Money RemainingBalance =>
        OnlinePaid.IsZero
            ? Money.Create(Pricing.BalanceDue.Amount, Pricing.CurrencyCode)
            : Money.Create(Math.Max(0m, Pricing.TotalPrice.Amount - _onlinePaid), Pricing.CurrencyCode);

    /// <summary>
    /// Whether the booking's whole total has been paid online, so nothing is left to hand over at
    /// pickup or delivery.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The verdict every "paid in full" sentence keys on (owner, 2026-09-25), so no screen decides it
    /// by comparing two amounts of its own. Compared on the amounts directly, not through
    /// <see cref="RemainingBalance"/>, whose unpaid branch returns the frozen cash balance for a
    /// reason that has nothing to do with this question.
    /// </para>
    /// <para>
    /// A fact about money received, so it stays true after a cancellation: the money was paid and is
    /// now being returned. What CONFIRMED the booking (a deposit or a full payment) is a different
    /// fact, carried by the confirming payment's purpose; the two differ only when a deposit happens
    /// to cover the whole total.
    /// </para>
    /// </remarks>
    public bool IsPaidInFull => DepositPaymentId is not null && _onlinePaid >= Pricing.TotalPrice.Amount;
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
    /// <para>
    /// A real column rather than something derived on read, because both the availability query and
    /// the expiry job filter on it and an index has to exist.
    /// </para>
    /// <para>
    /// Capped at <c>LastDecisionInstant</c> — the rental start LESS the payment window, not the
    /// rental start itself. Since 2026-09-11 a gallery may not approve unless the customer can still
    /// have the whole window to pay, and putting that rule in this one column is what makes every
    /// consumer of it right without being touched: the availability predicate releases the car at the
    /// last approvable instant, the settlement sweep closes the row there, the customer's screen
    /// stops saying the office is still deciding there, and the console's countdown reaches zero
    /// there. A guard in <c>Approve</c> alone would have left the car held, and the gallery counting
    /// down, on a request nobody could accept.
    /// </para>
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
    // Set when a PAYMENT confirms the booking (the deposit or the whole amount), not when the dealer
    // approves, and capped at the period start so a booking paid just before pickup cannot be
    // cancelled free after the customer was due to collect the car. Null until then: nothing has
    // been paid, so there is nothing to be free of. Every screen words the window from this rule:
    // "within N hours after payment", never "after approval".
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
        // A request nobody could ever accept must not be created. BookingWindowPolicy keeps the
        // rental start far enough out that this cannot happen — the lead time is validated at
        // startup to exceed the payment window — but the aggregate does not take that on trust, and
        // the day business rules become admin-editable (pre-launch item 25) that validation stops
        // being the only way a pair of numbers can arrive.
        //
        // A Result rather than a throw, unlike the pricing-date check below: this one is reachable
        // from a configuration change, and a customer deserves a sentence rather than a 500.
        if (LastDecisionInstant(period, terms) <= now)
            return BookingErrors.NoTimeToDecide;
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
            // The dealer's clock starts now, capped at the last instant an approval could still give
            // the customer their whole payment window.
            DecisionDeadline = Cap(now.Add(terms.AnswerWindow), LastDecisionInstant(period, terms))
        };
        booking.RecordTransition(null, BookingStatus.Requested, BookingParty.Customer, customerId, null, now);
        booking.AddDomainEvent(new BookingCreated(booking.Id, customerId, dealerId, vehicleId, now));
        booking.AddDomainEvent(new BookingRequested(booking.Id, dealerId, now));
        return booking;
    }

    /// <summary>
    /// The last instant an approval can still give the customer their whole payment window.
    /// </summary>
    /// <remarks>
    /// One expression, used by <c>Create</c> to trim the stored deadline and by <c>Approve</c> to
    /// state the invariant, so what is stored and what is enforced cannot drift apart.
    ///
    /// EXCLUSIVE, like every other deadline on this aggregate: the availability predicate releases
    /// the car at <c>DecisionDeadline</c>, so an approval landing exactly on it would be racing
    /// whoever took the car a microsecond later.
    /// </remarks>
    private static DateTimeOffset LastDecisionInstant(DateRange period, BookingTerms terms) =>
        period.Start.Subtract(terms.PaymentWindow);

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
    /// Whether this booking's deposit goes back to the customer in full because they cancelled it
    /// inside the free-cancellation window after paying (owner, 2026-09-24).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from the penalty reason FROZEN when the booking was cancelled, never recomputed from the
    /// deadline: a later clock would call "not free" a cancellation that was. Only the customer's own
    /// cancellation qualifies — the owner's rule names them — so a gallery or an admin cancelling in
    /// that hour does not, and neither does a non-delivery report (its reason is not the free window).
    /// </para>
    /// <para>
    /// True from the moment of cancellation, whatever the provider has said about the refund since:
    /// a refused refund is still owed, and the sweep keeps sending it. That is why a dispute reads this
    /// and not the refund's status — see <c>BookingDisputeSettlement.DepositHeldFor</c>.
    /// </para>
    /// </remarks>
    public bool ReturnsDepositOnCancellation =>
        Status == BookingStatus.Cancelled &&
        CancelledBy == BookingParty.Customer &&
        DepositPaymentId is not null &&
        Penalty?.ReasonCode == PenaltyReason.CancelledInFreeWindow;

    /// <summary>
    /// Whether this booking ended returning the WHOLE payment, deposit included: the customer
    /// cancelled inside the free window (owner, 2026-09-24), or an administrator cancelled it before
    /// pickup with no penalty on the customer (owner, 2026-09-26).
    /// </summary>
    /// <remarks>
    /// Read from the state the cancellation FROZE — who cancelled, the penalty assessed, whether the
    /// car was ever collected — so it stays true after the fact, whatever the provider has since said
    /// about the refund. A gallery cancelling is not an administrator cancelling, and a non-delivery
    /// report (the customer's own cancellation with the penalty on the office) is not a free one.
    /// </remarks>
    public bool ReturnsWholePayment =>
        Status == BookingStatus.Cancelled &&
        DepositPaymentId is not null &&
        PickedUpAt is null &&
        (ReturnsDepositOnCancellation ||
         (CancelledBy == BookingParty.Admin && Penalty?.AttributedTo != BookingParty.Customer));

    /// <summary>
    /// The booking money this booking owes back ABOVE its deposit because it ended before the car was
    /// collected (owner, 2026-09-24): a cancellation after the free window, a no-show, the office never
    /// handing the car over. Zero for every deposit-only booking, for one that returns the whole
    /// payment instead, and for any booking that has not ended or whose rental happened.
    /// </summary>
    /// <remarks>
    /// Decided HERE, by state, rather than by whichever command ended the booking, so a way of ending a
    /// booking added later cannot forget it (Phase 3, 2026-09-26). The deposit is never part of it:
    /// penalties and disputes stay deposit-based, and full online payment must not turn the office's
    /// rental revenue into a disputable deposit. The payment adds its own processing-fee rule on top.
    /// </remarks>
    public Money RefundableAboveDeposit =>
        (Status == BookingStatus.Cancelled || Status == BookingStatus.NoShow) &&
        PickedUpAt is null &&
        !ReturnsWholePayment
            ? PaidAboveDeposit
            : Money.ZeroIn(Pricing.CurrencyCode);

    /// <summary>
    /// What the customer paid online ABOVE the frozen deposit, whatever has happened since: the rest
    /// of the total for a booking paid in full, zero for a deposit-only one.
    /// </summary>
    /// <remarks>
    /// Asked BEFORE a booking ends as well as after — the settlement sweep uses it to know that marking
    /// a no-show will owe a refund, and so must find the payment first.
    /// </remarks>
    public Money PaidAboveDeposit =>
        DepositPaymentId is not null && _onlinePaid > Pricing.DepositAmount.Amount
            ? Money.Create(_onlinePaid - Pricing.DepositAmount.Amount, Pricing.CurrencyCode)
            : Money.ZeroIn(Pricing.CurrencyCode);

    /// <summary>
    /// The deposit this booking holds that its dispute window has now released CLEANLY (owner,
    /// 2026-09-26; spec 3.3: with no ticket, no penalty applies). Zero otherwise — including while the
    /// frozen window is still open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Released only when ALL hold: a paid booking that ended before the car was collected; it did not
    /// already return its whole payment; nothing claims its deposit (<paramref name="depositClaimed"/>:
    /// a dispute that was not withdrawn, which the Disputes context answers — a withdrawn ticket
    /// settles the booking "as if no dispute was raised", and a resolved one already decided the
    /// deposit, including by keeping it); no penalty is assessed against the CUSTOMER; and the
    /// booking's own frozen dispute window has closed.
    /// </para>
    /// <para>
    /// "No penalty" is read as no CUSTOMER-attributable penalty — the owner's own wording for the
    /// administrator's cancellation. A penalty against the office (it never handed the car over; it
    /// cancelled late) is a claim against the office, never against the customer's deposit, and
    /// holding the deposit for it would strand the money of the customer who was let down.
    /// </para>
    /// </remarks>
    public Money DepositReleasedOnCleanClose(DateTimeOffset now, bool depositClaimed) =>
        (Status == BookingStatus.Cancelled || Status == BookingStatus.NoShow) &&
        PickedUpAt is null &&
        DepositPaymentId is not null &&
        !ReturnsWholePayment &&
        !depositClaimed &&
        !HasPenaltyAgainstCustomer &&
        FinishedAt is not null &&
        now >= FinishedAt.Value.Add(Terms.PostReturnSettlementWindow)
            ? Money.Create(Math.Min(Pricing.DepositAmount.Amount, _onlinePaid), Pricing.CurrencyCode)
            : Money.ZeroIn(Pricing.CurrencyCode);

    /// <summary>
    /// The booking money cancelling RIGHT NOW would return, as <paramref name="cancelledBy"/>, and
    /// whether it would be the whole payment. Nothing when the booking is unpaid or can no longer be
    /// cancelled. The same rules the cancellation applies, asked before the fact, so the figure on the
    /// cancel sheet — and the <c>expectedRefund</c> a client sends back — cannot disagree with the
    /// refund recorded.
    /// </summary>
    public RefundOnCancellation PreviewRefundOnCancellation(BookingParty cancelledBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cancelledBy);
        var none = new RefundOnCancellation(Money.ZeroIn(Pricing.CurrencyCode), WholePayment: false);
        if (DepositPaymentId is null || !CanBeCancelled(now) || PickedUpAt is not null)
            return none;

        var assessment = AssessCancellation(cancelledBy, now);
        var whole =
            (cancelledBy == BookingParty.Customer && assessment.ReasonCode == PenaltyReason.CancelledInFreeWindow) ||
            (cancelledBy == BookingParty.Admin && assessment.AttributedTo != BookingParty.Customer);
        if (whole)
            return new RefundOnCancellation(Money.Create(_onlinePaid, Pricing.CurrencyCode), WholePayment: true);

        return _onlinePaid > Pricing.DepositAmount.Amount
            ? new RefundOnCancellation(Money.Create(_onlinePaid - Pricing.DepositAmount.Amount, Pricing.CurrencyCode), WholePayment: false)
            : none;
    }

    /// <summary>
    /// Whether cancelling right now, as the named party, would return the paid deposit in full. The
    /// same rule as <see cref="ReturnsDepositOnCancellation"/>, asked before the fact, so the promise on
    /// the confirmation sheet and the refund the cancellation records cannot disagree.
    /// </summary>
    public bool CancellationWouldReturnDeposit(BookingParty cancelledBy, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cancelledBy);
        return cancelledBy == BookingParty.Customer &&
               DepositPaymentId is not null &&
               CanBeCancelled(now) &&
               AssessCancellation(cancelledBy, now).ReasonCode == PenaltyReason.CancelledInFreeWindow;
    }

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
    public bool CanBeDisputed(DateTimeOffset now) =>
        DisputeWindowEndsAt is { } end && now < end;

    /// <summary>
    /// When this booking's own frozen dispute window closes: its return plus the settlement window for
    /// a car that came back, its ending plus the window for one cancelled or never collected. Null for
    /// every other status — no window is running.
    /// </summary>
    /// <remarks>
    /// The one statement of where the window starts, read by <see cref="CanBeDisputed"/> and by the
    /// booking's financial state (payments Phase 4), so "can it still be disputed" and "until when is
    /// the deposit held" cannot drift apart.
    /// </remarks>
    public DateTimeOffset? DisputeWindowEndsAt =>
        Status == BookingStatus.Returned
            ? ReturnedAt?.Add(Terms.PostReturnSettlementWindow)
            : Status == BookingStatus.Cancelled || Status == BookingStatus.NoShow
                ? FinishedAt?.Add(Terms.PostReturnSettlementWindow)
                : null;

    /// <summary>
    /// Whether the booking assessed a penalty that the CUSTOMER owes: the claim that keeps a paid
    /// deposit from being released when the window closes (owner, 2026-09-26). A penalty against the
    /// office, or one that owes nothing, is not a claim on the customer's deposit.
    /// </summary>
    public bool HasPenaltyAgainstCustomer =>
        Penalty is { IsNothingOwed: false } && Penalty.AttributedTo == BookingParty.Customer;

    /// <summary>
    /// Whether the booking ended without the rental taking place: cancelled, a no-show, expired or
    /// rejected. Nothing further is due on such a booking; what was paid is refunded or held by the
    /// rules that ending froze.
    /// </summary>
    public bool EndedBeforePickup =>
        PickedUpAt is null &&
        (Status == BookingStatus.Cancelled ||
         Status == BookingStatus.NoShow ||
         Status == BookingStatus.Expired ||
         Status == BookingStatus.Rejected);

    /// <summary>The deposit was paid. Shorthand for <see cref="ConfirmPayment"/> with the frozen deposit.</summary>
    public UnitResult<Error> ConfirmDepositPaid(Id depositPaymentId, DateTimeOffset now) =>
        ConfirmPayment(depositPaymentId, Money.Create(Pricing.DepositAmount.Amount, Pricing.CurrencyCode), now);

    /// <summary>
    /// A payment of at least the deposit cleared: the booking is confirmed and records what was paid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Idempotent: payment gateways retry their webhooks, and a retry must not fail or double-charge.
    /// </para>
    /// <para>
    /// <paramref name="appliedToBooking"/> is what the payment put towards the booking, fees excluded.
    /// Anything from the deposit up to the whole total confirms — the deposit is the minimum, not the
    /// only amount — and anything outside that range is a programming error upstream, because the
    /// checkout only ever opens one of the two.
    /// </para>
    /// </remarks>
    public UnitResult<Error> ConfirmPayment(Id depositPaymentId, Money appliedToBooking, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(appliedToBooking);
        if (depositPaymentId.IsEmpty)
            throw new DomainException("A payment confirmation requires a payment.");
        // Idempotent regardless of status: a webhook retried after the car was collected must
        // still be a success, not a refusal that makes a gateway keep retrying.
        if (DepositPaymentId == depositPaymentId)
            return UnitResult.Success<Error>();
        if (Status != BookingStatus.Approved)
            return UnitResult.Failure(BookingErrors.NotAwaitingPayment);
        // Refused rather than thrown: this runs inside a provider's webhook, where a throw is a 5xx the
        // provider retries forever. A refusal lets the caller orphan the capture and refund it.
        if (!string.Equals(appliedToBooking.CurrencyCode, Pricing.CurrencyCode, StringComparison.Ordinal) ||
            appliedToBooking.Amount < Pricing.DepositAmount.Amount ||
            appliedToBooking.Amount > Pricing.TotalPrice.Amount)
        {
            return UnitResult.Failure(BookingErrors.PaymentOutOfRange);
        }

        DepositPaymentId = depositPaymentId;
        _onlinePaid = appliedToBooking.Amount;
        // The free-cancellation window starts at PAYMENT, not at approval. Spec 5.5 measures it
        // from approval because under the old order payment came first, so approval was the moment
        // of commitment. It is not any more: a customer who pays near the end of the payment window
        // would otherwise have a free window that closed before they committed anything. That gap
        // was a whole day while the window was twenty-four hours; at two it is minutes, and the
        // reasoning is the same either way, which is why this does not read the window's length.
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

        Penalty = PenaltyAssessment.None(PenaltyReason.PaymentWindowLapsed, Pricing.CurrencyCode, now);
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

        Penalty = PenaltyAssessment.None(PenaltyReason.DealerAnswerWindowLapsed, Pricing.CurrencyCode, now);
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
        // The same rule the stored deadline already encodes, stated again — and it is NOT redundant.
        // A booking requested BEFORE 2026-09-11 carries a deadline capped at the rental start, so
        // approving one of those in its last two hours would hand the customer a payment deadline
        // past the moment they were due to collect the car. Same error code, because from the
        // gallery's side it is the same fact: the time to answer this request has gone.
        if (now.Add(Terms.PaymentWindow) > Period.Start)
            return UnitResult.Failure(BookingErrors.DecisionWindowElapsed);

        ActedByUserId = actedByUserId;
        ApprovedAt = now;
        // The customer now owes a deposit, and this is their window to pay it. NOT capped: the two
        // guards above are what guarantee the whole window fits before the rental starts, and a cap
        // here would tell the next reader that the window can still be shortened — which is exactly
        // the behaviour the owner removed on 2026-09-11.
        PaymentDeadline = now.Add(Terms.PaymentWindow);
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
        Penalty = PenaltyAssessment.None(PenaltyReason.DealerRejected, Pricing.CurrencyCode, now);
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
            PenaltyReason.DealerDidNotHandOver,
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
                PenaltyReason.CustomerNoShow,
                now)
            : PenaltyAssessment.None(
                PenaltyReason.DeliveryNoShowUndetermined,
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
        Money? cashCollected = null,
        HandoverProof? proof = null)
    {
        if (Status != BookingStatus.Confirmed)
            return BookingErrors.NotConfirmed;
        if (_handovers.Any(handover => handover.Type == HandoverType.Pickup))
            return BookingErrors.HandoverAlreadyRecorded;
        // Whatever proved it: a code, an unverified handover with a reason, or none required. The
        // window is about time, not about who was believed.
        var window = PickupWindowOpenAt(now);
        if (window.IsFailure)
            return window.Error;

        var record = HandoverRecord.Create(
            Id, HandoverType.Pickup, recordedBy, recordedByUserId, now,
            photoStorageKeys, odometerKm, fuelLevel, notes, cashCollected, proof);
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
        Money? cashCollected = null,
        HandoverProof? proof = null)
    {
        if (Status != BookingStatus.PickedUp)
            return BookingErrors.NotPickedUp;
        if (_handovers.Any(handover => handover.Type == HandoverType.Return))
            return BookingErrors.HandoverAlreadyRecorded;
        var window = ReturnWindowOpenAt(now);
        if (window.IsFailure)
            return window.Error;

        var record = HandoverRecord.Create(
            Id, HandoverType.Return, recordedBy, recordedByUserId, now,
            photoStorageKeys, odometerKm, fuelLevel, notes, cashCollected, proof);
        if (record.IsFailure)
            return record.Error;

        _handovers.Add(record.Value);
        ReturnedAt = now;
        Transition(BookingStatus.Returned, recordedBy, recordedByUserId, null, now);
        AddDomainEvent(new BookingReturned(Id, VehicleId, now));
        return record.Value;
    }

    /// <summary>
    /// Whether a pickup may be recorded at <paramref name="now"/>: refused before
    /// <see cref="PickupAvailableFrom"/> with <c>booking.pickup_too_early</c>, which names that moment.
    /// </summary>
    /// <remarks>
    /// <see cref="RecordPickup"/> applies it, and the office's handler asks it BEFORE a handover code
    /// is checked, so a code typed too early is refused without costing the customer one of their
    /// attempts. One rule, read in both places, so the two cannot drift apart.
    /// </remarks>
    public UnitResult<Error> PickupWindowOpenAt(DateTimeOffset now) =>
        now < PickupAvailableFrom
            ? UnitResult.Failure(BookingErrors.PickupTooEarly(PickupAvailableFrom))
            : UnitResult.Success<Error>();

    /// <summary>
    /// Whether a return may be recorded at <paramref name="now"/>: refused before the rental starts,
    /// with <c>booking.return_too_early</c>.
    /// </summary>
    public UnitResult<Error> ReturnWindowOpenAt(DateTimeOffset now) =>
        now < ReturnAvailableFrom
            ? UnitResult.Failure(BookingErrors.ReturnTooEarly(ReturnAvailableFrom))
            : UnitResult.Success<Error>();

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
            return PenaltyAssessment.None(PenaltyReason.CancelledBeforeDeposit, Pricing.CurrencyCode, now);

        if (FreeCancellationDeadline is not null && now <= FreeCancellationDeadline.Value)
            return PenaltyAssessment.None(PenaltyReason.CancelledInFreeWindow, Pricing.CurrencyCode, now);

        if (cancelledBy == BookingParty.Customer)
        {
            return PenaltyAssessment.Fixed(
                BookingParty.Customer,
                Terms.CustomerCancellationPenaltyPercent,
                Pricing.DepositAmount,
                PenaltyReason.CustomerCancelledAfterFreeWindow,
                now);
        }

        if (cancelledBy == BookingParty.Dealer)
        {
            return PenaltyAssessment.Range(
                BookingParty.Dealer,
                Terms.DealerPenaltyMinPercent,
                Terms.DealerPenaltyMaxPercent,
                Pricing.RentalTotal,
                PenaltyReason.DealerCancelledAfterFreeWindow,
                now);
        }

        return PenaltyAssessment.None(PenaltyReason.CancelledByPlatform, Pricing.CurrencyCode, now);
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
