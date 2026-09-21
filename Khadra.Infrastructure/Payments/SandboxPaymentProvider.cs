using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Payments;

/// <summary>
/// A provider that behaves like a real one and moves no money. NEVER in Production.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is an explicitly recorded exception to a standing rule, not a repeal of it.</b> CLAUDE.md
/// and <see cref="IPaymentProvider"/> both say never to register a provider that simulates success,
/// and the reasoning stands: a simulated capture writes the same <c>Confirmed</c> status, the same
/// row and the same dealer screen as a real one, so within a day nobody can tell which rentals have
/// money behind them. The owner asked for a clickable end-to-end lifecycle on 2026-09-21 and
/// approved this exception on the condition that it is made STRUCTURALLY impossible to operate in
/// Production. Two guards do that, and neither is a comment:
/// </para>
/// <list type="number">
/// <item>
/// <b>Environment.</b> <c>Program.cs</c> throws at boot, beside the mail and document guards, if this
/// value is configured while the host is Production. An environment variable is a property of the
/// process — something somebody can forget, copy or override — which is why it is not the only guard.
/// </item>
/// <item>
/// <b>Database.</b> <c>PaymentsStartupCheck</c> throws if the <c>payments</c> table has ever held a
/// row from any other provider. A database that has taken real money never runs a fake, whatever the
/// environment claims, and this is the one that catches a staging process pointed at the production
/// connection string.
/// </item>
/// </list>
/// <para>
/// A third thing keeps them honest rather than guarding the boot: the configuration validation in
/// <c>DependencyInjection.AddOptions</c> refuses an unrecognised provider name outright, so a typo
/// cannot quietly resolve to "None" and look deliberate.
/// </para>
/// <para>
/// <b>The marker is <see cref="Name"/>, persisted on every payment row.</b> <c>Payment.Provider</c>
/// is already stored and already carried into every receipt and refund, so a sandbox capture is
/// permanently distinguishable from a real one by the record itself rather than by a flag somebody
/// has to remember to set. No <c>IsSandbox</c> column is added, because a second marker can disagree
/// with the first.
/// </para>
/// <para>
/// <b>No card data, ever.</b> This provider asks for nothing and stores nothing: the "checkout" is a
/// URL the tester opens, and the outcome is chosen there. There is no card form, so there is no PAN
/// to leak, and the shape is the same as a real hosted checkout where the card never touches this
/// platform either.
/// </para>
/// <para>
/// <b>Shaped like a real adapter on purpose.</b> It signs its own events with HMAC-SHA256 over the
/// raw body using the configured webhook secret, mints opaque references, and refuses an unsigned or
/// mis-signed body — because the webhook is anonymous, and on any internet-reachable host an
/// unsigned sandbox would let a customer confirm their own booking by posting a capture. It is still
/// a fake, and it will pass things a real provider fails: signature framing, event-id semantics and
/// JOD minor units are all guesses here. It is a scaffold for the lifecycle, and the day a real
/// adapter lands this class is deleted rather than kept as "the dev provider".
/// </para>
/// </remarks>
internal sealed class SandboxPaymentProvider(IOptions<PaymentOptions> options, IClock clock) : IPaymentProvider
{
    /// <summary>
    /// The value that selects this provider, and the string written to every row it touches.
    /// </summary>
    /// <remarks>
    /// Defined on <see cref="PaymentOptions"/>, which is public, because both startup guards live in
    /// <c>Khadra.WebAPI</c> and this class is <c>internal</c>. Aliased here so the switch in
    /// <c>DependencyInjection</c> and the reader below name the same constant.
    /// </remarks>
    public const string ProviderName = PaymentProviders.Sandbox;

    public string Name => ProviderName;

    /// <summary>
    /// Sandbox, which is the one fact every client is told and every banner keys on.
    /// </summary>
    /// <remarks>
    /// <c>IsConfigured</c> follows from it and is therefore true: the screens ask that to decide
    /// whether to offer Pay, and answering false would make the lifecycle untestable, which is the
    /// entire purpose. What stops this being mistaken for a real provider is this value, not that one.
    /// </remarks>
    public PaymentMode Mode => PaymentMode.Sandbox;

    private string Secret => options.Value.WebhookSecret;

    /// <summary>
    /// Mints a session and a URL the tester drives by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reference is random and opaque, like a provider's. It is NOT derived from the booking or
    /// the payment id: a guessable reference on an anonymous webhook is an invitation to post a
    /// capture for somebody else's booking.
    /// </para>
    /// <para>
    /// The URL is built from <c>Payments:SandboxConsoleBaseUrl</c> and from nothing else. Not
    /// <c>ReturnUrlBase</c>, which is where a provider drops the customer AFTERWARDS and falls back
    /// to the dealer console's own address — an origin that serves no checkout and that
    /// <c>AppOptions</c> says never to send a customer to. And not a relative path: a phone opens
    /// this in its own browser, so it needs the API's absolute address as that phone reaches it,
    /// which this process has no other way of knowing. Startup refuses the sandbox without it.
    /// </para>
    /// </remarks>
    public Task<Result<CheckoutSession, Error>> CreateCheckoutAsync(
        CheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(Secret) || string.IsNullOrWhiteSpace(options.Value.SandboxConsoleBaseUrl))
            return Task.FromResult(Result.Failure<CheckoutSession, Error>(PaymentErrors.ProviderUnavailable));

        var reference = $"sbx_{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}";
        var url = $"{options.Value.SandboxConsoleBaseUrl.TrimEnd('/')}{SandboxEvents.ConsolePath}/{reference}";

        return Task.FromResult(Result.Success<CheckoutSession, Error>(new CheckoutSession(reference, url)));
    }

    /// <summary>
    /// Verifies the signature over the RAW bytes, then reads the body. In that order, always.
    /// </summary>
    /// <remarks>
    /// Synchronous and pure, as the port requires: an unverified body must not be able to reach a
    /// query. The signature covers the exact bytes, so anything that re-serialises on the way here
    /// breaks it — which is the same trap a real adapter has and the reason the port hands over
    /// <c>rawBody</c> rather than a parsed object.
    /// </remarks>
    public Result<ProviderEvent, Error> ParseEvent(string rawBody, IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        if (string.IsNullOrWhiteSpace(Secret) || string.IsNullOrWhiteSpace(rawBody))
            return Result.Failure<ProviderEvent, Error>(PaymentErrors.UntrustedEvent);

        if (!headers.TryGetValue(SandboxEvents.SignatureHeader, out var presented) || string.IsNullOrWhiteSpace(presented))
            return Result.Failure<ProviderEvent, Error>(PaymentErrors.UntrustedEvent);

        if (!IsSignatureValid(rawBody, presented))
            return Result.Failure<ProviderEvent, Error>(PaymentErrors.UntrustedEvent);

        try
        {
            var body = JsonSerializer.Deserialize<SandboxEvents.Body>(rawBody, SandboxEvents.Json);
            if (body is null || string.IsNullOrWhiteSpace(body.EventId) || string.IsNullOrWhiteSpace(body.Reference))
                return Result.Failure<ProviderEvent, Error>(PaymentErrors.UntrustedEvent);

            var kind = body.Kind?.ToLowerInvariant() switch
            {
                "captured" => ProviderEventKind.Captured,
                "failed" => ProviderEventKind.Failed,
                "refund_settled" => ProviderEventKind.RefundSettled,
                "refund_failed" => ProviderEventKind.RefundFailed,
                _ => ProviderEventKind.Other
            };

            // The amount travels in MINOR units, as every real provider sends it, and is converted
            // here rather than trusted as a decimal. JOD has three of them.
            //
            // Money.Create THROWS on a negative amount or a currency that is not three letters, and
            // this body is untrusted, so the guards are checked here rather than caught: a webhook
            // that answered 500 would be retried by a real provider forever.
            Money? amount = null;
            if (body.AmountMinor is { } minor && !string.IsNullOrWhiteSpace(body.Currency))
            {
                if (minor < 0 || body.Currency.Trim().Length != 3)
                    return Result.Failure<ProviderEvent, Error>(PaymentErrors.UntrustedEvent);
                amount = Money.Create(minor / SandboxEvents.MinorUnitsPerDinar, body.Currency.Trim());
            }

            return Result.Success<ProviderEvent, Error>(new ProviderEvent(
                body.EventId,
                body.Reference,
                kind,
                amount,
                body.FailureCode,
                body.OccurredAt ?? clock.UtcNow));
        }
        catch (JsonException)
        {
            return Result.Failure<ProviderEvent, Error>(PaymentErrors.UntrustedEvent);
        }
    }

    private bool IsSignatureValid(string rawBody, string presented)
    {
        var expected = SandboxEvents.Sign(rawBody, Secret);
        // Fixed-time compare. A sandbox does not need it; copying the real shape does, because this
        // file is what somebody will read when they write the MEPS or HyperPay adapter.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(presented.Trim().ToLowerInvariant()));
    }

    /// <summary>
    /// Answers "this one failed" for a session nobody ever completed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This provider keeps no session state — persisting it would mean a <c>sandbox_sessions</c>
    /// table in the production schema, which is the parallel payment system the owner ruled out — so
    /// it cannot report a capture it never saw. The question is what to answer instead.
    /// </para>
    /// <para>
    /// <b>Not "unavailable".</b> The sweep asks this only about rows already past their own expiry
    /// plus the stale grace, and an unavailable answer leaves such a row open forever: it would log a
    /// failure for every abandoned attempt on every one-minute tick, and the customer could not open
    /// a fresh checkout inside their own window. Failed is also the true answer — nothing was
    /// captured — and it loses nothing, because a late capture posted afterwards still lands, is
    /// recognised as money against a closed attempt, and is orphaned and refunded exactly as a real
    /// late capture would be. So the Pay-late path is exercised MORE by this answer, not less.
    /// </para>
    /// </remarks>
    public Task<Result<ProviderPaymentState, Error>> QueryAsync(
        string providerReference,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success<ProviderPaymentState, Error>(
            new ProviderPaymentState(ProviderEventKind.Failed, null, "sandbox_session_forgotten")));

    /// <summary>
    /// Accepts a refund instruction so the refund-ready path can be exercised end to end.
    /// </summary>
    /// <remarks>
    /// It records nothing and moves nothing; the platform's own <c>Refund</c> row is the record, and
    /// it settles when a signed <c>refund_settled</c> event is delivered — the same way a real one
    /// would.
    /// </remarks>
    public Task<Result<ProviderRefund, Error>> RefundAsync(
        RefundRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reference = $"sbxrf_{Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant()}";
        return Task.FromResult(Result.Success<ProviderRefund, Error>(new ProviderRefund(reference)));
    }

}
