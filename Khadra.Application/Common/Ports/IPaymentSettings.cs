using Khadra.Domain.Common;

namespace Khadra.Application.Common.Ports;

/// <summary>
/// The platform's own numbers for how a checkout behaves. Not business rules: a business rule is
/// something the owner decides about the rental, and none of these change what anybody is charged.
/// </summary>
public interface IPaymentSettings
{
    /// <summary>How long a provider session may stay open before it is dead to us.</summary>
    TimeSpan CheckoutSessionLifetime { get; }

    /// <summary>
    /// The margin between a session's own expiry and the booking's payment deadline.
    /// </summary>
    /// <remarks>
    /// The cheap half of the late-capture problem. A customer refused at the card form has lost
    /// nothing; one whose card is charged three seconds after their deadline has to be given their
    /// money back, which needs a provider that may itself be unavailable. Closing the door early
    /// makes the expensive path rare — it does not make it unnecessary, and the webhook still handles
    /// a late capture correctly.
    /// </remarks>
    TimeSpan CheckoutClosesBeforeDeadline { get; }

    /// <summary>
    /// How long after a session should have expired before a sweep gives up waiting for the
    /// provider's own notice and closes the row itself.
    /// </summary>
    TimeSpan StaleAttemptGrace { get; }

    /// <summary>
    /// Where the provider sends the customer back to.
    /// </summary>
    /// <remarks>
    /// It confirms NOTHING. A customer can close the tab, lose signal, or open the URL twice; the
    /// only thing that confirms a booking is a signed provider event. The app polls the booking after
    /// returning, and the return page says so.
    /// </remarks>
    Uri ReturnUrlFor(Id bookingId);
}
