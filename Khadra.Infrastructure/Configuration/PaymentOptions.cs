using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
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
    /// The default, and the only value Production may hold. It refuses every checkout with
    /// <c>payments.provider_unavailable</c>.
    /// </summary>
    public const string NoProvider = PaymentProviders.None;

    /// <summary>
    /// Selects the sandbox: a provider that completes a checkout and moves no money. NEVER Production.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value is also the string written to <c>Payment.Provider</c> on every row the sandbox
    /// touches, which is what makes a sandbox payment permanently tellable from a real one in the
    /// data itself rather than by a flag somebody has to remember. Deliberately shouted, so that it
    /// is unmistakable in a database dump, a log line and a support conversation.
    /// </para>
    /// <para>
    /// An alias for <see cref="PaymentProviders.Sandbox"/>, which is where it belongs: the meaning of
    /// the string is a property of the stored record, not of this configuration file. One symbol, so
    /// the guards, the registration, the adapter and the row cannot drift apart by a letter.
    /// </para>
    /// </remarks>
    public const string SandboxProvider = PaymentProviders.Sandbox;

    /// <summary>Every value <see cref="Provider"/> may hold. Anything else is refused at startup.</summary>
    public static readonly IReadOnlyList<string> KnownProviders = [NoProvider, SandboxProvider];

    /// <summary>
    /// <see cref="Provider"/> as the guards compare it: trimmed, upper-cased, never null.
    /// </summary>
    /// <remarks>
    /// One expression, because three places ask this question — the two startup validations and the
    /// registration switch — and a null here is not hypothetical. An ABSENT key leaves the property
    /// at its default, but a key set to an empty value binds null over it, and <c>.Trim()</c> on that
    /// is a <c>NullReferenceException</c> thrown from inside options validation, where it surfaces as
    /// a bare NRE with no mention of payments at all. <c>[Required]</c> is what reports the missing
    /// value; this only has to survive long enough to let it.
    /// </remarks>
    public string SelectedProvider => (Provider ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>
    /// Which provider to talk to. <see cref="NoProvider"/> until there is an account to talk to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The standing rule has not been repealed.</b> A provider that confirms bookings without
    /// money is indistinguishable on screen from a real one, would be trusted within a day, and is
    /// the cash path the owner has already forbidden (pre-launch item 2). That is still true, and it
    /// is still forbidden in Production.
    /// </para>
    /// <para>
    /// <b><see cref="SandboxProvider"/> is a recorded exception to it, not a loophole.</b> The owner
    /// asked for a clickable end-to-end lifecycle on 2026-09-21 and approved it on the condition that
    /// operating it in Production be made structurally impossible. Three things enforce that, and
    /// none of them is a comment: the environment guard in <c>Program.cs</c> refuses to start a
    /// Production host with this value; <c>PaymentsStartupCheck</c> refuses to start against any
    /// database that has ever held a payment from another provider; and every row the sandbox writes
    /// carries this string in <c>Payment.Provider</c> for as long as the row exists. Weakening any of
    /// the three turns the exception back into the thing that was forbidden.
    /// </para>
    /// <para>
    /// Do not add a third value for convenience. Tests substitute <c>IPaymentProvider</c> directly
    /// and need nothing here.
    /// </para>
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

    /// <summary>
    /// The API's own public address, for the sandbox checkout page. Required by the sandbox, unused
    /// by anything else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately NOT <see cref="ReturnUrlBase"/>, though both are URLs about a checkout. That one
    /// is where the provider drops the customer AFTERWARDS and falls back to the dealer console's
    /// address, which serves no checkout page and is somewhere a customer should never be sent. This
    /// one is where the fake checkout LIVES, which for the sandbox is this API itself.
    /// </para>
    /// <para>
    /// It has to be stated rather than derived, because an ASP.NET process has no reliable idea of
    /// the address a phone reaches it on: behind a proxy the host header is the proxy's, and on a LAN
    /// the binding is <c>0.0.0.0</c>. So on a developer machine this is the machine's LAN address —
    /// the same one the app's own base URL uses — and a relative path is not an option, because the
    /// phone opens the link in its own browser.
    /// </para>
    /// <para>
    /// Empty is the shipped value, and it is valid for every provider but the sandbox: startup
    /// refuses <c>SANDBOX</c> without it, next to the webhook-secret rule, rather than letting a
    /// checkout open onto a URL that cannot resolve.
    /// </para>
    /// </remarks>
    public string SandboxConsoleBaseUrl { get; init; } = string.Empty;
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
