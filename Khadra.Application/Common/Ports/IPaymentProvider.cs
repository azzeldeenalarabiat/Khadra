using CSharpFunctionalExtensions;
using Khadra.Domain.Common;

namespace Khadra.Application.Common.Ports;

/// <summary>
/// What kind of money this platform is moving right now. Published to every client.
/// </summary>
/// <remarks>
/// <para>
/// One value, not a name and a flag. A client's rule is "show the test banner when this reads
/// <see cref="Sandbox"/>, and not otherwise" — and getting that wrong in the wrong direction tells a
/// paying customer their payment was fake, which is far worse than missing a banner on a test host.
/// A single field cannot disagree with itself.
/// </para>
/// <para>
/// It is deliberately NOT the provider's name. A client comparing against "HyperPay" would be
/// hard-coding an infrastructure detail into a screen, and would need a release every time the
/// platform changed processor. What a screen needs to know is the CLASS of the answer, and there are
/// only three.
/// </para>
/// </remarks>
public enum PaymentMode
{
    /// <summary>No provider. Every checkout is refused, and the screens say so.</summary>
    None = 0,

    /// <summary>A provider that completes a checkout and moves no money. Never Production.</summary>
    Sandbox = 1,

    /// <summary>A real processor. Money moves.</summary>
    Live = 2
}

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
/// <b>Nothing may be added here that simulates success, except the one thing that was.</b> A stub
/// that confirms a booking is indistinguishable on screen from a real payment, would be trusted
/// within a day, and is exactly the cash path the owner has already forbidden (pre-launch item 2).
/// That rule stands. The owner approved ONE recorded exception on 2026-09-21 —
/// <c>SandboxPaymentProvider</c>, reporting <see cref="PaymentMode.Sandbox"/> — on the condition that
/// it be structurally impossible to operate in Production, which three mechanisms enforce and
/// <c>SandboxPaymentGuardTests</c> proves. A second one does not get to point at the first as
/// precedent: what made that one allowable was the guards, not the intention.
/// </para>
/// </remarks>
public interface IPaymentProvider
{
    /// <summary>The name stored on every payment row, so a later provider swap can still refund old captures.</summary>
    string Name { get; }

    /// <summary>
    /// What class of money this provider moves. The one fact the clients are told.
    /// </summary>
    /// <remarks>
    /// Answered by the adapter rather than worked out from <see cref="Name"/> by whoever is asking:
    /// a caller that compared the name against a constant would be deciding, in its own layer, a
    /// question the adapter already knows the answer to.
    /// </remarks>
    PaymentMode Mode { get; }

    /// <summary>
    /// Whether this platform can actually take a deposit right now.
    /// </summary>
    /// <remarks>
    /// True for the sandbox, which really does complete a checkout. It answers "will the Pay button
    /// work", not "is this real" — <see cref="Mode"/> is the one that answers that, and conflating
    /// them would make the lifecycle untestable, since a screen that hides Pay can never be driven
    /// through to Confirmed.
    /// </remarks>
    bool IsConfigured => Mode != PaymentMode.None;

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
