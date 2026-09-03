using Khadra.Domain.Common;

namespace Khadra.Domain.IdentityAccess;

// Stable machine codes for every expected Identity & Access failure. The API maps `Kind` to an HTTP
// status; clients map `Code` to a localised message.
public static class IdentityErrors
{
    public static readonly Error InvalidEmail =
        Error.Validation("auth.invalid_email", "The email address is not valid.");

    public static readonly Error InvalidPhone =
        Error.Validation("auth.invalid_phone", "The phone number must be a valid mobile number in international format (e.g. +9627XXXXXXXX or 07XXXXXXXX).");

    public static readonly Error InvalidName =
        Error.Validation("auth.invalid_name", "The full name must be between 2 and 150 characters.");

    public static Error WeakPassword(string detail) =>
        Error.Validation("auth.password_policy", detail);

    public static readonly Error DateOfBirthRequired =
        Error.Validation("auth.date_of_birth_required", "A date of birth is required to register as a renter.");

    public static readonly Error InvalidDateOfBirth =
        Error.Validation("auth.invalid_date_of_birth", "The date of birth is not valid.");

    // Carries the configured figure so the client can say WHY without hardcoding the platform's rule.
    public static Error UnderMinimumAge(int minimumAge) =>
        Error.Validation("auth.under_minimum_age", $"Renters must be at least {minimumAge} years old.");

    public static readonly Error UnsupportedDocumentType =
        Error.Validation("documents.unsupported_type", "That document type is not accepted.");

    public static readonly Error InvalidDocumentContent =
        Error.Validation("documents.invalid_content", "Upload a JPEG, PNG or PDF file.");

    public static readonly Error DocumentTooLarge =
        Error.Validation("documents.too_large", "The file is larger than the upload limit.");

    public static readonly Error DocumentNotFound =
        Error.NotFound("documents.not_found", "That document does not exist.");

    public static readonly Error EmailTaken =
        Error.Conflict("auth.email_taken", "An account with this email already exists.");

    public static readonly Error PhoneTaken =
        Error.Conflict("auth.phone_taken", "An account with this phone number already exists.");

    // Deliberately identical for unknown email, wrong password and deleted accounts (no enumeration).
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "The email or password is incorrect.");

    public static readonly Error EmailNotVerified =
        Error.Forbidden("auth.email_not_verified", "Verify your email address before signing in.");

    public static readonly Error AccountSuspended =
        Error.Forbidden("auth.account_suspended", "This account is suspended. Contact support.");

    public static readonly Error InvalidRefreshToken =
        Error.Unauthorized("auth.invalid_refresh_token", "The session is no longer valid. Sign in again.");

    public static readonly Error InvalidToken =
        Error.Validation("auth.invalid_token", "The link is invalid or has expired. Request a new one.");

    public static readonly Error UserNotFound =
        Error.NotFound("auth.user_not_found", "The user was not found.");

    public static readonly Error AlreadySuspended =
        Error.Conflict("auth.already_suspended", "The account is already suspended.");

    public static readonly Error NotSuspended =
        Error.Conflict("auth.not_suspended", "The account is not suspended.");

    public static readonly Error AlreadyDeleted =
        Error.Conflict("auth.already_deleted", "The account is already deleted.");
}
