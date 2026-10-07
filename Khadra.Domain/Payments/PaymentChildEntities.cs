using Khadra.Domain.Common;

namespace Khadra.Domain.Payments;

/// <summary>
/// Money going back to the customer, and where in that journey it is.
/// </summary>
/// <remarks>
/// A child of <see cref="Payment"/> rather than an aggregate of its own: the rule that refunds never
/// exceed the capture spans the two, so they are one consistency boundary.
///
/// Its own id is the idempotency key sent to the provider, for the same reason the payment's is: a
/// sweep that crashes between "sent" and "recorded as sent" must be able to send again without
/// refunding twice.
/// </remarks>
public sealed class Refund : Entity
{
    public Id PaymentId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public RefundReason Reason { get; private set; } = null!;

    // The split of Amount into the booking's own money and the processing fee (owner, 2026-09-26:
    // stored in Phase 5). Written ONCE, by the payment that records the refund, from the fee rule the
    // payment froze; never recomputed. Bare figures in the refund's own currency, as the payment's fee
    // is: a second currency column could only agree with the first, or be wrong.
    private decimal _bookingPart;
    private decimal _feePart;

    /// <summary>What this refund returns of the booking's own money.</summary>
    public Money BookingPart => Money.Create(_bookingPart, Amount.CurrencyCode);

    /// <summary>What this refund returns of the payment's processing fee.</summary>
    public Money FeePart => Money.Create(_feePart, Amount.CurrencyCode);

    /// <summary>
    /// The ticket whose resolution ordered this, for a refund that came from one. Null for an
    /// orphaned capture, which nobody decided: it was owed the moment the money landed.
    /// </summary>
    public Id? DisputeTicketId { get; private set; }

    public RefundStatus Status { get; private set; } = null!;

    /// <summary>The provider's id for the refund itself, which is not the payment's.</summary>
    public string? ProviderReference { get; private set; }

    /// <summary>The provider's own word for a refusal. A code, never a sentence.</summary>
    public string? FailureCode { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? SettledAt { get; private set; }

    /// <summary>The LATEST refusal. The customer's app prints <c>settledAt ?? failedAt ?? requestedAt</c>, so this keeps that meaning.</summary>
    public DateTimeOffset? FailedAt { get; private set; }

    /// <summary>
    /// How many times the provider has refused this refund: once per refused SEND, never once per notice
    /// (Wave 4, B4; checklist 157). History, so a later send or settlement does not reset it.
    /// </summary>
    public int RefusalCount { get; private set; }

    /// <summary>
    /// When a refused refund may be sent again; null while nothing is scheduled — never refused, or sent since.
    /// </summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    private Refund()
    {
    }

    private Refund(Id id) : base(id)
    {
    }

    /// <param name="feePart">
    /// The processing fee inside <paramref name="amount"/>, by the payment's frozen rule
    /// (<c>Payment.FeeFor</c>); the rest is booking money.
    /// </param>
    internal static Refund Request(
        Id paymentId,
        Money amount,
        RefundReason reason,
        Id? disputeTicketId,
        Money feePart,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentNullException.ThrowIfNull(feePart);
        if (paymentId.IsEmpty)
            throw new DomainException("A refund requires a payment.");
        if (!string.Equals(feePart.CurrencyCode, amount.CurrencyCode, StringComparison.Ordinal)
            || feePart.Amount < 0m
            || feePart.Amount > amount.Amount)
            throw new DomainException("A refund's fee part is in its own currency and never more than the refund.");

        return new Refund(Id.New())
        {
            PaymentId = paymentId,
            Amount = amount,
            Reason = reason,
            DisputeTicketId = disputeTicketId,
            _feePart = feePart.Amount,
            _bookingPart = amount.Amount - feePart.Amount,
            Status = RefundStatus.Requested,
            RequestedAt = now
        };
    }

    /// <summary>The provider accepted the instruction. Idempotent: a re-send is not a second refund.</summary>
    public void MarkSent(string providerReference, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerReference);
        if (Status == RefundStatus.Settled)
            return;

        ProviderReference = providerReference;
        Status = RefundStatus.Sent;
        SentAt ??= now;
        FailureCode = null;
        FailedAt = null;
        NextAttemptAt = null;
    }

    /// <summary>The provider says the money is back with the customer.</summary>
    public void MarkSettled(DateTimeOffset now)
    {
        if (Status == RefundStatus.Settled)
            return;

        Status = RefundStatus.Settled;
        SettledAt = now;
        FailureCode = null;
        FailedAt = null;
        NextAttemptAt = null;
    }

    /// <summary>
    /// The provider refused a send the sweep made just now (Wave 4, B4). Every refused send is counted, including a
    /// refund that was already refused before it was sent again, and the next send waits by the policy.
    /// </summary>
    /// <remarks>
    /// NOT terminal in the way a settled refund is: money is still owed. From the policy's alert on, the row is put
    /// in front of an administrator.
    /// </remarks>
    public void RecordRefusedSend(string failureCode, DateTimeOffset now, RefundRetryPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        ArgumentNullException.ThrowIfNull(policy);
        if (Status == RefundStatus.Settled)
            return;

        Refuse(failureCode, now, policy);
    }

    /// <summary>
    /// A provider's notice that a refund it had taken was refused (Wave 4, B4). Counted once per send: a notice
    /// about a refund that already reads Failed is the same refusal said again — providers repeat their notices
    /// under new event ids — and changes nothing.
    /// </summary>
    public void MarkFailed(string failureCode, DateTimeOffset now, RefundRetryPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        ArgumentNullException.ThrowIfNull(policy);
        if (Status == RefundStatus.Settled || Status == RefundStatus.Failed)
            return;

        Refuse(failureCode, now, policy);
    }

    /// <summary>
    /// Whether the sweep may send this now: owed and not yet with the provider, and either never refused or past
    /// the wait its last refusal set.
    /// </summary>
    public bool IsDueToSend(DateTimeOffset now) =>
        (Status == RefundStatus.Requested || Status == RefundStatus.Failed)
        && (NextAttemptAt is null || NextAttemptAt <= now);

    private void Refuse(string failureCode, DateTimeOffset now, RefundRetryPolicy policy)
    {
        RefusalCount++;
        Status = RefundStatus.Failed;
        FailureCode = failureCode;
        FailedAt = now;
        NextAttemptAt = now + policy.DelayAfter(RefusalCount);
    }
}
