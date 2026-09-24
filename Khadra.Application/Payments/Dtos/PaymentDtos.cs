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
    bool IsSandbox,
    /// <summary>What the attempt is for: "Deposit" or "FullPayment". Added 2026-09-24.</summary>
    string Purpose,
    /// <summary>The processing fee inside <see cref="Amount"/>; zero unless the fee is on. Added 2026-09-24.</summary>
    MoneyDto ProcessingFee)
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
            payment.IsSandbox,
            payment.Purpose.Name,
            MoneyDto.From(payment.ProcessingFee));
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
    PaymentDto? LiveAttempt,
    /// <summary>
    /// The ways this booking can be paid right now, each fully worked out by the server (deposit, then
    /// full amount). Empty when it cannot be paid. Added 2026-09-24: a client renders these figures and
    /// never computes one.
    /// </summary>
    IReadOnlyList<PaymentOptionDto>? Options = null);

/// <summary>One way of paying, with every figure the choice screen shows.</summary>
/// <param name="Purpose">"Deposit" or "FullPayment" — what the checkout is opened with.</param>
/// <param name="SelectedPaymentAmount">What this payment puts towards the booking.</param>
/// <param name="ProcessingFee">The optional card-processing fee on top; zero for a deposit or when off.</param>
/// <param name="TotalChargedNow">What the card is charged: amount + fee. The Pay button's figure.</param>
/// <param name="RemainingBalanceAfter">What is still owed on the booking once this succeeds.</param>
public sealed record PaymentOptionDto(
    string Purpose,
    MoneyDto SelectedPaymentAmount,
    MoneyDto ProcessingFee,
    MoneyDto TotalChargedNow,
    MoneyDto RemainingBalanceAfter)
{
    public static PaymentOptionDto From(PaymentChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        return new PaymentOptionDto(
            choice.Purpose.Name,
            MoneyDto.From(choice.BookingAmount),
            MoneyDto.From(choice.ProcessingFee),
            MoneyDto.From(choice.ChargedNow),
            MoneyDto.From(choice.RemainingAfter));
    }
}

/// <summary>
/// Everything the payment screen needs about an approved booking's money, in one answer: the booking's
/// own figures, what has been paid, and both ways of paying the rest.
/// </summary>
public sealed record PaymentOptionsDto(
    Guid BookingId,
    bool CanPay,
    string? UnavailableReason,
    DateTimeOffset? PayBy,
    MoneyDto RentalSubtotal,
    MoneyDto DeliveryFee,
    MoneyDto BookingTotal,
    MoneyDto RequiredDeposit,
    MoneyDto FullPayableAmount,
    MoneyDto AmountPaid,
    MoneyDto RemainingBalance,
    IReadOnlyList<PaymentOptionDto> Options,
    PaymentDto? LiveAttempt);
