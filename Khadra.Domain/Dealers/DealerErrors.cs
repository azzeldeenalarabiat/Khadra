using Khadra.Domain.Common;

namespace Khadra.Domain.Dealers;

public static class DealerErrors
{
    public static readonly Error InvalidBusinessName =
        Error.Validation("dealer.invalid_business_name", "The business name must be between 2 and 150 characters.");

    public static readonly Error InvalidCommercialRegistration =
        Error.Validation("dealer.invalid_commercial_registration", "The commercial registration number is not valid.");

    public static readonly Error InvalidDeliveryRadius =
        Error.Validation("dealer.invalid_delivery_radius", "The delivery radius must be greater than 0 and at most 200 km.");

    // A ceiling, not a price: what a delivery is worth is the gallery’s decision.
    public static readonly Error InvalidDeliveryFee =
        Error.Validation("dealer.invalid_delivery_fee", "The delivery fee must be between 0 and 1000 JOD.");

    public static readonly Error InvalidOperatingHours =
        Error.Validation("dealer.invalid_operating_hours", "Operating hours must cover all seven days, and closing time must be after opening time.");

    public static readonly Error NotAwaitingReview =
        Error.Conflict("dealer.not_awaiting_review", "This dealer application is not awaiting review.");

    public static readonly Error AlreadyApproved =
        Error.Conflict("dealer.already_approved", "This dealer is already approved.");

    public static readonly Error NothingToResubmit =
        Error.Conflict("dealer.nothing_to_resubmit", "Only a rejected application or one needing clarification can be resubmitted.");

    public static readonly Error AlreadyRegistered =
        Error.Conflict("dealer.already_registered", "This account already has a dealer application.");

    public static readonly Error CommercialRegistrationTaken =
        Error.Conflict("dealer.commercial_registration_taken", "A dealer is already registered with that commercial registration number.");

    public static readonly Error UnsupportedDocumentType =
        Error.Validation("dealer.unsupported_document_type", "That document type is not accepted.");

    public static readonly Error InvalidDocumentContent =
        Error.Validation("dealer.invalid_document_content", "Upload a JPEG, PNG or PDF file.");

    public static readonly Error DocumentTooLarge =
        Error.Validation("dealer.document_too_large", "The file is larger than the upload limit.");

    public static readonly Error NotRegistered =
        Error.NotFound("dealer.not_registered", "This account has not submitted a dealer application.");

    public static readonly Error MissingRequiredDocuments =
        Error.Validation("dealer.missing_documents", "The commercial registration, vehicle registration and owner identity documents are all required before approval.");

    public static readonly Error ReasonRequired =
        Error.Validation("dealer.reason_required", "A reason is required.");

    public static readonly Error DocumentsLocked =
        Error.Conflict("dealer.documents_locked", "Documents can only be attached while the application is pending or awaiting clarification.");

    public static readonly Error EmployeeAlreadyExists =
        Error.Conflict("dealer.employee_exists", "This user is already an employee of this dealer.");

    public static readonly Error EmployeeNotFound =
        Error.NotFound("dealer.employee_not_found", "The employee was not found.");

    public static readonly Error EmployeeAlreadyInactive =
        Error.Conflict("dealer.employee_inactive", "The employee is already deactivated.");

    public static readonly Error OwnerCannotBeEmployee =
        Error.Validation("dealer.owner_cannot_be_employee", "The dealer owner cannot also be hired as an employee.");

    public static readonly Error DeliveryNotOffered =
        Error.Conflict("dealer.delivery_not_offered", "This dealer does not offer delivery.");

    public static readonly Error NotApproved =
        Error.Forbidden("dealer.not_approved", "This dealer is not approved yet and cannot trade on the platform.");

    public static readonly Error AlreadyDeleted =
        Error.Conflict("dealer.already_deleted", "This dealer is already deleted.");

    // Spec 4.2: staff management and dealer settings belong to the owner alone.
    public static readonly Error OwnerOnly =
        Error.Forbidden("dealer.owner_only", "Only the dealer owner can do this.");

    public static readonly Error InvitationAlreadyAccepted =
        Error.Conflict("dealer.invitation_accepted", "This employee has already accepted their invitation.");

    public static readonly Error EmployeeAlreadyActive =
        Error.Conflict("dealer.employee_already_active", "This employee is already active.");

    // Spec 3.1: the licence was verified against this name. Renaming after approval would change what
    // that verification meant. (Owner decision pending; locked by default.)
    public static readonly Error BusinessNameLocked =
        Error.Conflict("dealer.business_name_locked", "The business name cannot be changed after approval. Contact the platform if it has legally changed.");

    public static readonly Error InvalidBrandingKind =
        Error.Validation("dealer.invalid_branding_kind", "Branding is either the logo or the cover image.");

    public static readonly Error InvalidBrandingType =
        Error.Validation("dealer.invalid_branding_type", "A logo or cover must be a JPEG, PNG or WebP image.");

    public static readonly Error BrandingNotUploaded =
        Error.Validation("dealer.branding_not_uploaded", "That image was never uploaded.");

    public static readonly Error BrandingOutsideDealer =
        Error.Validation("dealer.branding_outside_dealer", "That image does not belong to this dealer.");

    // Spec 4.2: financial reports are off for an employee unless the owner grants them.
    public static readonly Error ReportsNotGranted =
        Error.Forbidden("dealer.reports_not_granted", "Only the dealer owner can see financial reports, unless they grant you access.");
}
