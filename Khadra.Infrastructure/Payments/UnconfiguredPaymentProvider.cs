using CSharpFunctionalExtensions;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Payments;

/// <summary>
/// The payment provider this platform ships with: one that refuses.
/// </summary>
/// <remarks>
/// <para>
/// There is no merchant account, no API key and no webhook secret for Khadra, so there is nothing
/// this class could honestly do except say so. Every method returns
/// <c>payments.provider_unavailable</c>, which the API maps to 503 — the customer did nothing wrong,
/// and the same request will work the day a provider is wired in.
/// </para>
/// <para>
/// <b>This must never be replaced by something that succeeds — and was not.</b> The owner approved
/// <see cref="SandboxPaymentProvider"/> on 2026-09-21 as a recorded exception, SELECTABLE beside this
/// one and never in place of it: <c>Payments:Provider</c> still defaults to <c>None</c>, Production
/// still cannot hold any other value, and a database that has taken sandbox payments can never be
/// served by this class afterwards. Everything below is still the rule. A stub that captured and confirmed
/// would be indistinguishable, on every screen and in every table, from a real payment: bookings
/// would read Confirmed, the gallery would prepare a car, and nobody looking at the system could tell
/// which rentals had money behind them. The owner has already forbidden the same thing in its other
/// form (pre-launch item 2: no deposits taken out of band). Tests substitute
/// <see cref="IPaymentProvider"/> at the handler boundary and drive the domain directly; nothing
/// needs a fake that lies to the database.
/// </para>
/// <para>
/// What DOES need writing, the day a provider is chosen, is a sibling of this class implementing the
/// same four methods. Nothing above this line changes.
/// </para>
/// </remarks>
internal sealed class UnconfiguredPaymentProvider(IOptions<PaymentOptions> options) : IPaymentProvider
{
    public string Name => PaymentProviders.None;

    /// <summary>None: no provider, no money, and every screen is told so rather than guessing.</summary>
    public PaymentMode Mode => PaymentMode.None;

    public Task<Result<CheckoutSession, Error>> CreateCheckoutAsync(
        CheckoutRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<CheckoutSession, Error>(PaymentErrors.ProviderUnavailable));

    /// <summary>
    /// Refuses every body, whatever it contains.
    /// </summary>
    /// <remarks>
    /// Deliberately <see cref="PaymentErrors.UntrustedEvent"/> rather than "unavailable": a platform
    /// with no webhook secret cannot verify a signature, so it cannot distinguish a provider from
    /// anyone else who found the URL. Refusing to parse is the only safe answer, and the endpoint
    /// answers 401 rather than 200 so nothing upstream records a delivery as accepted.
    /// </remarks>
    public Result<ProviderEvent, Error> ParseEvent(string rawBody, IReadOnlyDictionary<string, string> headers) =>
        Result.Failure<ProviderEvent, Error>(PaymentErrors.UntrustedEvent);

    public Task<Result<ProviderPaymentState, Error>> QueryAsync(
        string providerReference,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<ProviderPaymentState, Error>(PaymentErrors.ProviderUnavailable));

    public Task<Result<ProviderRefund, Error>> RefundAsync(
        RefundRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<ProviderRefund, Error>(PaymentErrors.ProviderUnavailable));

    /// <summary>The configured name, for the startup line that says why nothing can be taken.</summary>
    internal string ConfiguredProvider => options.Value.Provider;
}

/// <summary>
/// Says at every start whether money can be taken, the way the mail probe says whether mail can be
/// sent.
/// </summary>
/// <remarks>
/// A platform that silently cannot take payments looks exactly like one that can, right up to the
/// first customer who tries. One line at boot is the difference between knowing and finding out.
/// </remarks>
internal sealed class PaymentProviderProbe(IPaymentProvider provider, IOptions<PaymentOptions> options)
    : IPaymentProviderProbe
{
    /// <summary>
    /// One line at boot, in the place people are told to look first — so it must not flatter.
    /// </summary>
    /// <remarks>
    /// The sandbox is called out by name rather than folded into "a provider is configured". It IS
    /// configured and deposits genuinely can be taken through it, so the ordinary sentence would be
    /// true and still misleading: it would print "Deposits can be taken" one line after the warning
    /// that no money moves, in the exact place the operator is looking to find out whether money
    /// moves. The mail probe learned this — "Email ready" for the transport that delivered nothing
    /// is what hid a real outage for days.
    /// </remarks>
    public Task<string> DescribeAsync(CancellationToken cancellationToken = default)
    {
        var configured = options.Value.Provider;
        return Task.FromResult(provider.Mode switch
        {
            PaymentMode.Sandbox =>
                "Provider is the SANDBOX. Checkouts complete and NO MONEY MOVES. Every payment row "
                + "is stamped SANDBOX for as long as it exists. This database has never held a "
                + "payment from any other provider, and this host is not Production — both checked.",
            PaymentMode.Live =>
                $"Provider '{provider.Name}' is configured. Deposits can be taken.",
            _ when string.Equals(configured, PaymentProviders.None, StringComparison.OrdinalIgnoreCase) =>
                "Payments:Provider is 'None', so no deposit can be paid and every approved booking "
                + "will expire. Customers are told this on their own booking.",
            _ =>
                $"Payments:Provider is '{configured}', which this build has no adapter for. "
                + "No deposit can be paid.",
        });
    }
}
