using Khadra.Domain.Common;

namespace Khadra.Domain.Payments;

public static class PaymentErrors
{
    /// <summary>The customer asked to pay for something this release does not offer.</summary>
    public static readonly Error PurposeUnavailable =
        Error.Validation("payments.purpose_unavailable", "That way of paying is not offered for this booking.");

    /// <summary>
    /// No payment provider is configured, so no money can be taken.
    /// </summary>
    /// <remarks>
    /// <see cref="ErrorKind.Unavailable"/> and therefore 503, not 422: the customer did nothing
    /// wrong, and the same request will work once the platform is wired to a provider. It is the one
    /// answer the platform gives today, and it is deliberately impossible to mistake for a success.
    /// </remarks>
    public static readonly Error ProviderUnavailable = Error.Unavailable(
        "payments.provider_unavailable",
        "Card payments are not available yet. Nothing has been charged.");

    public static readonly Error ProviderRefused = Error.Unavailable(
        "payments.provider_refused",
        "The payment service could not start a checkout. Nothing has been charged.");

    public static readonly Error NotFound =
        Error.NotFound("payments.not_found", "That payment was not found.");

    /// <summary>The signature did not verify, or the body was not a shape this provider sends.</summary>
    public static readonly Error UntrustedEvent = Error.Unauthorized(
        "payments.untrusted_event",
        "That notification did not come from the payment provider.");

    public static readonly Error AmountMismatch = Error.Conflict(
        "payments.amount_mismatch",
        "The amount captured does not match the amount that was due.");

    public static readonly Error NotLive = Error.Conflict(
        "payments.not_live",
        "That payment attempt has already finished.");

    public static readonly Error AlreadyCaptured = Error.Conflict(
        "payments.already_captured",
        "That payment has already been captured.");

    public static readonly Error RefundExceedsCapture = Error.Conflict(
        "payments.refund_exceeds_capture",
        "A refund cannot be larger than what was captured.");

    /// <summary>The capture incident an administrator named does not exist on that payment (Wave 4, B1).</summary>
    public static readonly Error IncidentNotFound =
        Error.NotFound("payments.incident_not_found", "That payment incident was not found.");

    /// <summary>An administrator, or two at once, marked a capture incident handled that already is.</summary>
    public static readonly Error IncidentAlreadyHandled = Error.Conflict(
        "payments.incident_already_handled",
        "That payment incident has already been marked as handled.");
}
