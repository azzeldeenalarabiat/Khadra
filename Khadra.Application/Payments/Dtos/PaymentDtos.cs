using Khadra.Application.Common.Dtos;
using Khadra.Domain.Payments;

namespace Khadra.Application.Payments.Dtos;

/// <summary>
/// One checkout attempt, as a customer's screen sees it.
/// </summary>
/// <remarks>
/// <para>
/// Carries no provider REFERENCE and no failure detail beyond a code. A reference is the key that
/// resolves a webhook to a payment, and a screen has no use for it; a provider's own prose is written
/// for an English-speaking developer, not for a customer in Amman.
/// </para>
/// <para>
/// It does carry <see cref="IsSandbox"/>, which is a different thing and not a softening of that
/// rule: the verdict, not the provider's name. The name would be an infrastructure literal for a
/// screen to compare against; this is the platform having already compared it. And it is derived at
/// read time from the one stored value rather than persisted beside it, so there is still exactly one
/// marker and nothing that can disagree with it.
/// </para>
/// </remarks>
public sealed record PaymentDto(
    Guid PaymentId,
    Guid BookingId,
    string Status,
    MoneyDto Amount,
    /// <summary>Where to send the customer. Null while the provider has not answered yet.</summary>
    string? CheckoutUrl,
    DateTimeOffset ExpiresAt,
    /// <summary>The provider's code for a refusal, for a client that renders it in its own language.</summary>
    string? FailureCode,
    DateTimeOffset CreatedAt,
    /// <summary>
    /// True when no money moved for this attempt and none ever could have.
    /// </summary>
    /// <remarks>
    /// A property of the RECORD, not of the deployment. The platform-wide mode on <c>/app-config</c>
    /// says what this host is doing now; this says what was true when this particular attempt was
    /// opened, and the two can differ — which is the whole point of the marker outliving the setting.
    /// </remarks>
    bool IsSandbox)
{
    public static PaymentDto From(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return new PaymentDto(
            payment.Id.Value,
            payment.BookingId.Value,
            payment.Status.Name,
            MoneyDto.From(payment.Amount),
            payment.CheckoutUrl,
            payment.ExpiresAt,
            payment.FailureCode,
            payment.CreatedAt,
            payment.IsSandbox);
    }
}

/// <summary>
/// Whether this booking can be paid right now, and what is standing in the way if not.
/// </summary>
/// <remarks>
/// <para>
/// Server-judged, like every other verdict this platform sends a client. The app must not work out
/// "can I pay" from a status and a deadline: the answer also depends on whether a provider exists at
/// all, which is not on the booking and never will be.
/// </para>
/// <para>
/// <see cref="UnavailableReason"/> is what makes the screen honest. There is a real difference
/// between "your window has closed" and "this platform cannot take cards yet", and a customer who is
/// shown one when the other is true will either give up or complain about the wrong thing.
/// </para>
/// </remarks>
public sealed record PaymentAvailabilityDto(
    bool CanPay,
    string? UnavailableReason,
    MoneyDto? AmountDue,
    DateTimeOffset? PayBy,
    /// <summary>The attempt already in flight, if the customer has one open.</summary>
    PaymentDto? LiveAttempt);
