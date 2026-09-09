using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Configuration;

/// <summary>
/// How the platform's own checkout behaves, and which provider — if any — it talks to.
/// </summary>
/// <remarks>
/// None of these are business rules. A business rule is something the owner decides about the
/// rental, and every number here is about the mechanics of a card form: none of them changes what
/// anybody is charged. They live apart from <c>BusinessRules</c> for that reason.
/// </remarks>
public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary>
    /// The only value this platform ships with. It refuses every checkout with
    /// <c>payments.provider_unavailable</c>.
    /// </summary>
    public const string NoProvider = "None";

    /// <summary>
    /// Which provider to talk to. <see cref="NoProvider"/> until there is an account to talk to.
    /// </summary>
    /// <remarks>
    /// There is deliberately no "Simulated" or "Development" value, and nobody should add one. A
    /// provider that confirms bookings without money would be indistinguishable on screen from a real
    /// one, would be trusted within a day, and is the cash path the owner has already forbidden
    /// (pre-launch item 2). Tests substitute <c>IPaymentProvider</c> directly.
    /// </remarks>
    [Required]
    public string Provider { get; init; } = NoProvider;

    /// <summary>
    /// Credentials. Empty in every tracked appsettings; real values live ONLY in user-secrets or the
    /// environment.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>The secret the provider signs its webhooks with.</summary>
    public string WebhookSecret { get; init; } = string.Empty;

    /// <summary>How long a checkout session may stay open.</summary>
    [Range(1, 1440)]
    public int CheckoutSessionMinutes { get; init; } = 30;

    /// <summary>
    /// How far BEFORE the booking's payment deadline a session must already have closed.
    /// </summary>
    /// <remarks>
    /// This is what turns the sharpest race in the feature from a refund into a refusal. Without it a
    /// customer can start a checkout with four seconds left, and the capture lands after the platform
    /// has released their car. Refusing them at the card form costs nothing; refunding them needs a
    /// provider that may be down.
    /// </remarks>
    [Range(0, 1440)]
    public int CheckoutClosesBeforeDeadlineMinutes { get; init; } = 5;

    /// <summary>
    /// How long past its own expiry an attempt is left alone before a sweep closes it.
    /// </summary>
    /// <remarks>
    /// A grace, not a delay for its own sake: closing a row the instant it expires would race an
    /// expiry event already in flight from the provider, and the sweep would win with a worse answer.
    /// </remarks>
    [Range(1, 1440)]
    public int StaleAttemptGraceMinutes { get; init; } = 15;

    /// <summary>
    /// Where the provider sends the customer afterwards. Falls back to <c>App:ClientBaseUrl</c>.
    /// </summary>
    /// <remarks>
    /// The return confirms nothing — only a signed provider event does — so this is where the app
    /// resumes, not where a booking is decided.
    /// </remarks>
    public string ReturnUrlBase { get; init; } = string.Empty;
}

/// <summary>Reads <see cref="PaymentOptions"/> as the application layer's port.</summary>
internal sealed class PaymentSettings(IOptions<PaymentOptions> options, IOptions<AppOptions> app) : IPaymentSettings
{
    public TimeSpan CheckoutSessionLifetime => TimeSpan.FromMinutes(options.Value.CheckoutSessionMinutes);

    public TimeSpan CheckoutClosesBeforeDeadline =>
        TimeSpan.FromMinutes(options.Value.CheckoutClosesBeforeDeadlineMinutes);

    public TimeSpan StaleAttemptGrace => TimeSpan.FromMinutes(options.Value.StaleAttemptGraceMinutes);

    /// <summary>
    /// Where the provider drops the customer once the card form is done.
    /// </summary>
    /// <remarks>
    /// The booking's own page, because that page reads the booking from the server and will show
    /// whatever the webhook has or has not yet done. A dedicated "payment succeeded" page would be
    /// claiming an outcome this platform does not know at that moment.
    /// </remarks>
    public Uri ReturnUrlFor(Id bookingId)
    {
        var root = string.IsNullOrWhiteSpace(options.Value.ReturnUrlBase)
            ? app.Value.ClientBaseUrl
            : options.Value.ReturnUrlBase;
        return new Uri($"{root.TrimEnd('/')}/bookings/{bookingId.Value}");
    }
}
