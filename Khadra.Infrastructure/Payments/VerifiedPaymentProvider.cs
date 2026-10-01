using CSharpFunctionalExtensions;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;

namespace Khadra.Infrastructure.Payments;

/// <summary>
/// The configured provider, held until <see cref="PaymentVerification"/> says this database's payments are its kind of
/// money (pre-launch item 221). Every provider is wrapped, so there is one gate whichever is configured.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gate is <see cref="IsConfigured"/>, not only the four calls.</b> Handlers decide on it BEFORE they reach
/// the provider: opening a checkout writes its <c>payments</c> row — stamped with this provider's name — before it
/// asks the provider for a session, and the payment sweep closes unanswered attempts and records a refund the
/// provider refuses as failed. A held process must do none of that on a database it has not verified, so it reads as
/// unconfigured until it has, and each handler's existing "no provider" path is the one taken. The four calls refuse
/// as well, belt and braces, with <see cref="PaymentErrors.ProviderUnavailable"/> — 503, so a real provider
/// delivers its webhook again rather than giving up on a 4xx.
/// </para>
/// <para>
/// <see cref="Name"/> and <see cref="Mode"/> pass through untouched: they say what money this process WOULD move,
/// which <c>/app-config</c>, the consoles' Sandbox banner, the finance summary and every reminder read, and that is
/// still true while it waits.
/// </para>
/// </remarks>
internal sealed class VerifiedPaymentProvider(IPaymentProvider inner, PaymentVerification verification) : IPaymentProvider
{
    /// <summary>The provider configuration chose. Asked only by <see cref="PaymentDatabaseCheck"/>.</summary>
    internal IPaymentProvider Inner => inner;

    public string Name => inner.Name;

    public PaymentMode Mode => inner.Mode;

    public bool IsConfigured => verification.IsVerified && inner.IsConfigured;

    public Task<Result<CheckoutSession, Error>> CreateCheckoutAsync(
        CheckoutRequest request,
        CancellationToken cancellationToken = default) =>
        verification.IsVerified
            ? inner.CreateCheckoutAsync(request, cancellationToken)
            : Task.FromResult(Result.Failure<CheckoutSession, Error>(PaymentErrors.ProviderUnavailable));

    public Result<ProviderEvent, Error> ParseEvent(string rawBody, IReadOnlyDictionary<string, string> headers) =>
        verification.IsVerified
            ? inner.ParseEvent(rawBody, headers)
            : Result.Failure<ProviderEvent, Error>(PaymentErrors.ProviderUnavailable);

    public Task<Result<ProviderPaymentState, Error>> QueryAsync(
        string providerReference,
        CancellationToken cancellationToken = default) =>
        verification.IsVerified
            ? inner.QueryAsync(providerReference, cancellationToken)
            : Task.FromResult(Result.Failure<ProviderPaymentState, Error>(PaymentErrors.ProviderUnavailable));

    public Task<Result<ProviderRefund, Error>> RefundAsync(
        RefundRequest request,
        CancellationToken cancellationToken = default) =>
        verification.IsVerified
            ? inner.RefundAsync(request, cancellationToken)
            : Task.FromResult(Result.Failure<ProviderRefund, Error>(PaymentErrors.ProviderUnavailable));
}
