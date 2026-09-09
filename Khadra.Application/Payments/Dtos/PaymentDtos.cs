using Khadra.Application.Common.Dtos;
using Khadra.Domain.Payments;

namespace Khadra.Application.Payments.Dtos;

/// <summary>
/// One checkout attempt, as a customer's screen sees it.
/// </summary>
/// <remarks>
/// Carries no provider reference and no failure detail beyond a code. A reference is the key that
/// resolves a webhook to a payment, and a screen has no use for it; a provider's own prose is written
/// for an English-speaking developer, not for a customer in Amman.
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
    DateTimeOffset CreatedAt)
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
            payment.CreatedAt);
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
