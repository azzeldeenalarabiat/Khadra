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
    public DateTimeOffset? FailedAt { get; private set; }

    private Refund()
    {
    }

    private Refund(Id id) : base(id)
    {
    }

    internal static Refund Request(
        Id paymentId,
        Money amount,
        RefundReason reason,
        Id? disputeTicketId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(reason);
        if (paymentId.IsEmpty)
            throw new DomainException("A refund requires a payment.");

        return new Refund(Id.New())
        {
            PaymentId = paymentId,
            Amount = amount,
            Reason = reason,
            DisputeTicketId = disputeTicketId,
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
    }

    /// <summary>
    /// The provider refused. This is NOT terminal in the way a settled refund is: money is still
    /// owed, and the row stays on the admin's list of refunds that need a human.
    /// </summary>
    public void MarkFailed(string failureCode, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        if (Status == RefundStatus.Settled)
            return;

        Status = RefundStatus.Failed;
        FailureCode = failureCode;
        FailedAt = now;
    }
}
