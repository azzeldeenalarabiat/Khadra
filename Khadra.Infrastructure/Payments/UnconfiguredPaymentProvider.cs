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
/// <b>This must never be replaced by something that succeeds.</b> A stub that captured and confirmed
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
    public string Name => PaymentOptions.NoProvider;

    public bool IsConfigured => false;

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
    public Task<string> DescribeAsync(CancellationToken cancellationToken = default)
    {
        var configured = options.Value.Provider;
        return Task.FromResult(provider.IsConfigured
            ? $"Provider '{provider.Name}' is configured. Deposits can be taken."
            : string.Equals(configured, PaymentOptions.NoProvider, StringComparison.OrdinalIgnoreCase)
                ? "Payments:Provider is 'None', so no deposit can be paid and every approved booking "
                  + "will expire. Customers are told this on their own booking."
                : $"Payments:Provider is '{configured}', which this build has no adapter for. "
                  + "No deposit can be paid.");
    }
}
