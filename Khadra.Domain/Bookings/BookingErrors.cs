using Khadra.Domain.Common;

namespace Khadra.Domain.Bookings;

public static class BookingErrors
{
    public static readonly Error PeriodInThePast =
        Error.Validation("booking.period_in_past", "A booking must start in the future.");

    /// <summary>
    /// Carries the figure, because the customer cannot act on this without knowing it and the app
    /// must not repeat a number of its own beside the server's.
    /// </summary>
    public static Error TooSoon(TimeSpan minimumLeadTime) =>
        Error.Validation(
            "booking.too_soon",
            minimumLeadTime.TotalHours >= 1
                ? $"A rental must start at least {minimumLeadTime.TotalHours:0.#} hours from now."
                : $"A rental must start at least {minimumLeadTime.TotalMinutes:0} minutes from now.");

    /// <summary>
    /// Judged on the BILLED day count, so the figure a customer is refused on is the same one they
    /// were quoted. Counting elapsed time here would refuse a rental the price said was shorter.
    /// </summary>
    public static Error RentalTooLong(int days, int maxRentalDays) =>
        Error.Validation(
            "booking.rental_too_long",
            $"A rental cannot run longer than {maxRentalDays} days, and this one is {days}.");

    public static Error BeyondBookingHorizon(int maxAdvanceDays) =>
        Error.Validation(
            "booking.beyond_horizon",
            $"A rental cannot be booked more than {maxAdvanceDays} days ahead.");

    /// <summary>
    /// The gallery is shut when the customer means to collect. Self-pickup only — a delivery is the
    /// gallery driving out, which it may do outside counter hours (owner, 2026-09-07).
    /// </summary>
    /// <remarks>
    /// Carries the gallery's hours for that day, because "outside opening hours" alone tells somebody
    /// they are wrong without telling them what would be right.
    /// </remarks>
    public static Error PickupOutsideOpeningHours(string schedule) =>
        Error.Validation(
            "booking.pickup_outside_opening_hours",
            $"This gallery cannot hand over a car at that time — it is {schedule}. Choose delivery, or a time while they are open.");

    public static Error ReturnOutsideOpeningHours(string schedule) =>
        Error.Validation(
            "booking.return_outside_opening_hours",
            $"This gallery cannot take the car back at that time — it is {schedule}. Choose a return while they are open.");

    /// <summary>
    /// Spec 5.1. UPLOADED, not verified: nothing can move a document out of PendingReview yet, so
    /// requiring verification would mean nobody could book at all. See pre-launch checklist item 63,
    /// which the owner has made a hard requirement before real launch.
    /// </summary>
    public static readonly Error RenterDocumentsIncomplete =
        Error.Forbidden(
            "booking.documents_incomplete",
            "Upload both sides of your driving licence and an ID or passport before booking.");

    /// <summary>
    /// The signed-in account cannot make bookings: it is not a customer's, or it is not active.
    /// </summary>
    /// <remarks>
    /// One error for both, and no detail about which. Both sit behind the security-stamp check --
    /// suspending or deleting an account rotates the stamp, so its tokens stop working immediately
    /// -- which makes this defence in depth rather than a path anyone reaches. It said
    /// <c>booking.not_a_party</c> ("only a customer or a rental office has bookings of their own")
    /// until 2026-09-07, which described a different situation entirely.
    /// </remarks>
    public static readonly Error AccountCannotBook =
        Error.Forbidden(
            "booking.account_cannot_book",
            "This account cannot make bookings.");

    /// <summary>
    /// Until push notifications exist, an approval reaches a customer only by email. An unverified
    /// address is a booking that expires unread, with the dealer's decision wasted and the car held
    /// for nothing in the meantime.
    /// </summary>
    public static readonly Error EmailNotVerified =
        Error.Forbidden(
            "booking.email_not_verified",
            "Verify your email address before booking. We send your booking updates there.");

    /// <summary>
    /// Someone else holds this car for these dates. Distinct from a generic conflict on purpose: a
    /// phone can turn this into "that car was taken while you were deciding" and offer the dates
    /// again, which it cannot do with data.conflict.
    /// </summary>
    public static readonly Error VehicleUnavailable =
        Error.Conflict(
            "booking.vehicle_unavailable",
            "That car is no longer free for those dates.");

    public static readonly Error DeliveryLocationRequired =
        Error.Validation("booking.delivery_location_required", "A delivery booking needs a drop-off location.");

    public static readonly Error DeliveryLocationNotAllowed =
        Error.Validation("booking.delivery_location_not_allowed", "A self-pickup booking cannot carry a delivery location.");

    public static readonly Error DeliveryFeeNotAllowed =
        Error.Validation("booking.delivery_fee_not_allowed", "A self-pickup booking cannot carry a delivery fee.");

    public static readonly Error DeliveryOutOfRange =
        Error.Validation("booking.delivery_out_of_range", "The chosen location is outside this dealer's delivery area.");

    public static readonly Error VehicleNotDeliveryEligible =
        Error.Validation("booking.vehicle_not_delivery_eligible", "This vehicle is not available for delivery.");

    /// <summary>Pickup, no-show and non-delivery all need a deposit behind them.</summary>
    public static readonly Error NotConfirmed =
        Error.Conflict("booking.not_confirmed", "The deposit has not been paid on this booking.");

    public static readonly Error DecisionWindowNotElapsed =
        Error.Conflict("booking.decision_window_not_elapsed", "The dealer still has time to answer this request.");

    /// <summary>
    /// A dealer answering after the window closed. Rejecting late is allowed; approving is not,
    /// because the catalogue released the car at the deadline and somebody else may hold it now.
    /// </summary>
    public static readonly Error DecisionWindowElapsed =
        Error.Conflict("booking.decision_window_elapsed", "This request expired before it was answered.");

    public static readonly Error NotAwaitingPayment =
        Error.Conflict("booking.not_awaiting_payment", "This booking is not awaiting payment.");

    public static readonly Error NotAwaitingDecision =
        Error.Conflict("booking.not_awaiting_decision", "This booking is not awaiting a dealer decision.");

    public static readonly Error NotPickedUp =
        Error.Conflict("booking.not_picked_up", "The vehicle has not been picked up.");

    public static readonly Error NotReturned =
        Error.Conflict("booking.not_returned", "The vehicle has not been returned.");

    public static readonly Error AlreadyFinished =
        Error.Conflict("booking.already_finished", "This booking has already ended.");

    public static readonly Error CannotCancelNow =
        Error.Conflict("booking.cannot_cancel", "This booking can no longer be cancelled.");

    /// <summary>
    /// A cancellation reason code the platform does not publish.
    /// </summary>
    /// <remarks>
    /// The list is closed and travels on <c>GET /api/v1/app-config</c> so the chips a customer taps
    /// are the platform's words in both languages rather than literals in a phone binary. Free text
    /// alone produced "asdf"; a code is something the gallery and the owner can count.
    /// </remarks>
    public static readonly Error UnknownCancellationReason =
        Error.Validation("booking.unknown_cancellation_reason", "That is not one of the cancellation reasons.");

    public static readonly Error NoShowTooEarly =
        Error.Conflict("booking.no_show_too_early", "The no-show window has not elapsed yet.");

    /// <summary>
    /// A non-delivery report filed before the car was ever due. The mirror of
    /// <see cref="NoShowTooEarly"/>, and it exists for the same reason: neither party may accuse the
    /// other of missing a handover that has not arrived yet.
    /// </summary>
    public static readonly Error NonDeliveryTooEarly =
        Error.Conflict(
            "booking.non_delivery_too_early",
            "The rental has not started yet, so the gallery cannot have failed to hand the car over.");

    public static readonly Error SettlementTooEarly =
        Error.Conflict("booking.settlement_too_early", "The post-return settlement window has not elapsed yet.");

    // Distinct from AlreadyFinished on purpose: the settlement job declining a booking because someone
    // disputed it is not the same event as it declining one that already ended, and a log that calls
    // both "already ended" cannot tell you which happened.
    public static readonly Error DisputeOpen =
        Error.Conflict("booking.dispute_open", "This booking cannot settle while a dispute on it is open.");

    // Also the answer for a booking that exists but belongs to someone else. A 403 would confirm the
    // id is real, and booking ids are the one thing a stranger should not be able to enumerate.
    public static readonly Error NotFound =
        Error.NotFound("booking.not_found", "That booking was not found.");

    public static readonly Error NotAParty =
        Error.Forbidden("booking.not_a_party", "Only a customer or a rental office has bookings of their own.");

    public static readonly Error PaymentWindowNotElapsed =
        Error.Conflict("booking.payment_window_open", "The payment window has not elapsed yet.");

    public static readonly Error ActorCannotDecide =
        Error.Forbidden("booking.actor_cannot_decide", "You are not allowed to act on this booking.");

    public static readonly Error ReasonRequired =
        Error.Validation("booking.reason_required", "A reason is required.");

    public static readonly Error HandoverAlreadyRecorded =
        Error.Conflict("booking.handover_recorded", "This handover has already been recorded.");

    public static readonly Error CurrencyMismatch =
        Error.Validation("booking.currency_mismatch", "All amounts on a booking must use the same currency.");

    public static readonly Error ExtensionRequiresActiveRental =
        Error.Conflict("booking.extension_requires_active_rental", "A booking can only be extended while the vehicle is out on rental.");
}
