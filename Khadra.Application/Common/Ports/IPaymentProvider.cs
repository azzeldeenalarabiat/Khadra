using CSharpFunctionalExtensions;
using Khadra.Domain.Common;

namespace Khadra.Application.Common.Ports;

/// <summary>What a provider event turned out to be, once the adapter had normalised it.</summary>
public enum ProviderEventKind
{
    /// <summary>Money moved. The only kind that confirms anything.</summary>
    Captured = 1,

    /// <summary>The attempt ended without money moving: declined, abandoned, or the session lapsed.</summary>
    Failed = 2,

    /// <summary>A refund this platform asked for has completed.</summary>
    RefundSettled = 3,

    /// <summary>A refund this platform asked for was refused.</summary>
    RefundFailed = 4,

    /// <summary>A kind the platform does not act on. Acknowledged and recorded, nothing done.</summary>
    Other = 99
}

/// <param name="ExpiresAt">
/// Capped BELOW the booking's payment deadline by the caller, so the provider's own session dies
/// before a capture could land too late to be applied. It is cheaper to refuse a customer at the
/// card form than to take their money and give it back.
/// </param>
public sealed record CheckoutRequest(
    Id PaymentId,
    Money Amount,
    string BookingReference,
    string CustomerEmail,
    DateTimeOffset ExpiresAt,
    Uri ReturnUrl);

public sealed record CheckoutSession(string ProviderReference, string CheckoutUrl);

/// <param name="ProviderEventId">
/// The provider's own id for THIS DELIVERY, not for the payment. It is the replay key, so an adapter
/// that cannot supply a stable one has to synthesise a deterministic one from the payload and say so.
/// </param>
public sealed record ProviderEvent(
    string ProviderEventId,
    string ProviderReference,
    ProviderEventKind Kind,
    Money? Amount,
    string? FailureCode,
    DateTimeOffset OccurredAt);

public sealed record ProviderPaymentState(ProviderEventKind Kind, Money? Amount, string? FailureCode);

public sealed record RefundRequest(Id RefundId, string PaymentProviderReference, Money Amount);

public sealed record ProviderRefund(string ProviderReference);

/// <summary>
/// The one place this platform touches a payment provider.
/// </summary>
/// <remarks>
/// <para>
/// Everything a provider needs to know is a parameter, and everything it answers is a return value:
/// no implementation may reach into the database, and no handler may reach into a provider SDK. That
/// is what makes the dependency isolable, and isolating it is the point — there is no provider
/// account for this platform yet, and until there is, the only implementation is
/// <c>UnconfiguredPaymentProvider</c>, which refuses everything with
/// <c>payments.provider_unavailable</c>.
/// </para>
/// <para>
/// <b>Nothing may be added here that simulates success.</b> A stub that confirms a booking would be
/// indistinguishable on screen from a real payment, would be trusted within a day, and is exactly the
/// cash path the owner has already forbidden (pre-launch item 2). Tests substitute this interface;
/// the shipped build refuses.
/// </para>
/// </remarks>
public interface IPaymentProvider
{
    /// <summary>The name stored on every payment row, so a later provider swap can still refund old captures.</summary>
    string Name { get; }

    /// <summary>Whether this platform can actually take money right now.</summary>
    bool IsConfigured { get; }

    Task<Result<CheckoutSession, Error>> CreateCheckoutAsync(
        CheckoutRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the signature and normalises the body, before any database read.
    /// </summary>
    /// <remarks>
    /// Synchronous and pure on purpose: it must be impossible for an unverified body to reach a query.
    /// An adapter that needs the network to verify a signature is verifying it wrong.
    /// </remarks>
    Result<ProviderEvent, Error> ParseEvent(string rawBody, IReadOnlyDictionary<string, string> headers);

    /// <summary>
    /// Asks the provider what actually became of a session.
    /// </summary>
    /// <remarks>
    /// The only way to close a payment whose webhook never arrived. Without it a delivery lost in the
    /// network leaves a row live forever, and the customer cannot open a second checkout inside their
    /// own payment window.
    /// </remarks>
    Task<Result<ProviderPaymentState, Error>> QueryAsync(
        string providerReference,
        CancellationToken cancellationToken = default);

    Task<Result<ProviderRefund, Error>> RefundAsync(
        RefundRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Says at startup whether money can be taken, the way <see cref="IEmailTransportProbe"/> says
/// whether mail can be sent.
/// </summary>
/// <remarks>
/// A platform that silently cannot take payments looks identical to one that can until the first
/// customer tries. One line in the log at boot is the difference.
/// </remarks>
public interface IPaymentProviderProbe
{
    Task<string> DescribeAsync(CancellationToken cancellationToken = default);
}
