using Khadra.Domain.Common;

namespace Khadra.Domain.Disputes;

public static class DisputeErrors
{
    public static readonly Error ReasonRequired =
        Error.Validation("dispute.reason_required", "A reason is required to open a ticket.");

    public static readonly Error AlreadyResolved =
        Error.Conflict("dispute.already_resolved", "This ticket has already been resolved.");

    public static readonly Error AlreadyWithdrawn =
        Error.Conflict("dispute.already_withdrawn", "This ticket has been withdrawn.");

    public static readonly Error NotOpen =
        Error.Conflict("dispute.not_open", "This ticket is no longer open.");

    public static readonly Error OnlyOpenerCanWithdraw =
        Error.Forbidden("dispute.only_opener_can_withdraw", "Only the party who opened the ticket can withdraw it.");

    public static readonly Error ResolutionNoteRequired =
        Error.Validation("dispute.resolution_note_required", "A resolution note is required so both parties can see the reasoning.");

    public static readonly Error DispositionDoesNotBalance =
        Error.Validation("dispute.disposition_unbalanced", "The refund, platform and dealer amounts must add up to exactly the deposit held.");

    public static readonly Error DispositionCurrencyMismatch =
        Error.Validation("dispute.disposition_currency_mismatch", "All amounts in a resolution must use the same currency.");

    public static readonly Error StatementRequired =
        Error.Validation("dispute.statement_required", "A statement cannot be empty.");

    public static readonly Error DealerChargeWithoutAssessment =
        Error.Validation(
            "dispute.dealer_charge_unassessed",
            "This booking carries no penalty attributed to the dealer, so no dealer charge can be applied.");

    public static readonly Error DealerChargeOutsideAssessment =
        Error.Validation(
            "dispute.dealer_charge_out_of_range",
            "A dealer charge must fall inside the penalty range assessed on the booking.");

    // Also the answer for a ticket that exists but is not yours to see: a 403 would confirm the id.
    public static readonly Error NotFound =
        Error.NotFound("dispute.not_found", "That dispute was not found.");

    public static readonly Error BookingNotDisputable =
        Error.Conflict(
            "dispute.booking_not_disputable",
            "This booking cannot be disputed: it has not finished, or its dispute window has closed.");

    public static readonly Error AlreadyOpen =
        Error.Conflict("dispute.already_open", "A dispute is already open on this booking. Add to it instead.");

    public static readonly Error InvalidEvidenceType =
        Error.Validation("dispute.invalid_evidence_type", "Evidence must be a photo or a PDF.");

    public static readonly Error EvidenceNotUploaded =
        Error.Validation("dispute.evidence_not_uploaded", "One of the evidence files was never uploaded.");

    public static readonly Error EvidenceOutsideBooking =
        Error.Validation("dispute.evidence_outside_booking", "Evidence must be uploaded for this booking.");

    // A ticket pointing at a booking that no longer loads is a data-integrity failure, not a user
    // error. It must never be resolved as if the money were known.
    public static readonly Error BookingMissing =
        Error.Conflict("dispute.booking_missing", "The booking behind this dispute could not be loaded.");

    public static readonly Error DealerChargeCurrencyMismatch =
        Error.Validation(
            "dispute.dealer_charge_currency_mismatch",
            "A dealer charge must use the same currency as the booking's assessed penalty.");
}
