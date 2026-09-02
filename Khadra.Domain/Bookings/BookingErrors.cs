using Khadra.Domain.Common;

namespace Khadra.Domain.Bookings;

public static class BookingErrors
{
    public static readonly Error PeriodInThePast =
        Error.Validation("booking.period_in_past", "A booking must start in the future.");

    public static readonly Error PeriodTooShort =
        Error.Validation("booking.period_too_short", "A booking must cover at least one day.");

    public static readonly Error DeliveryLocationRequired =
        Error.Validation("booking.delivery_location_required", "A delivery booking needs a drop-off location.");

    public static readonly Error DeliveryLocationNotAllowed =
        Error.Validation("booking.delivery_location_not_allowed", "A self-pickup booking cannot carry a delivery location.");

    public static readonly Error DeliveryOutOfRange =
        Error.Validation("booking.delivery_out_of_range", "The chosen location is outside this dealer's delivery area.");

    public static readonly Error VehicleNotDeliveryEligible =
        Error.Validation("booking.vehicle_not_delivery_eligible", "This vehicle is not available for delivery.");

    public static readonly Error NotAwaitingPayment =
        Error.Conflict("booking.not_awaiting_payment", "This booking is not awaiting payment.");

    public static readonly Error NotAwaitingDecision =
        Error.Conflict("booking.not_awaiting_decision", "This booking is not awaiting a dealer decision.");

    public static readonly Error NotApproved =
        Error.Conflict("booking.not_approved", "This booking is not in an approved state.");

    public static readonly Error NotPickedUp =
        Error.Conflict("booking.not_picked_up", "The vehicle has not been picked up.");

    public static readonly Error NotReturned =
        Error.Conflict("booking.not_returned", "The vehicle has not been returned.");

    public static readonly Error AlreadyFinished =
        Error.Conflict("booking.already_finished", "This booking has already ended.");

    public static readonly Error CannotCancelNow =
        Error.Conflict("booking.cannot_cancel", "This booking can no longer be cancelled.");

    public static readonly Error NoShowTooEarly =
        Error.Conflict("booking.no_show_too_early", "The no-show window has not elapsed yet.");

    public static readonly Error SettlementTooEarly =
        Error.Conflict("booking.settlement_too_early", "The post-return settlement window has not elapsed yet.");

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
