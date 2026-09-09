using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using Khadra.Domain.Payments.Events;

namespace Khadra.Domain.Payments;

/// <summary>
/// One checkout attempt for one booking's deposit, and whatever had to go back.
/// </summary>
/// <remarks>
/// <para>
/// The aggregate is an ATTEMPT, not "the money for a booking". A customer whose card is declined
/// tries again, and that is a new row: reusing one would mean sending the provider an idempotency
/// key it has already closed, and being handed back the same dead session. What stops two live
/// attempts existing at once is a partial unique index in the database, not a field here.
/// </para>
/// <para>
/// <see cref="Refund"/> is a child rather than its own aggregate because the invariant that binds
/// them -- refunds never exceed what was captured -- spans both, and an invariant that spans two
/// aggregates is really one aggregate wearing a disguise.
/// </para>
/// <para>
/// The provider's own event log is deliberately NOT here. An event naming a reference this platform
/// has never seen has no payment to hang off, and it still has to be recorded, so the receipt table
/// lives outside the aggregate and is written beside it.
/// </para>
/// <para>
/// Not soft-deletable, for the same reason a booking is not: it is a financial record.
/// </para>
/// </remarks>
public sealed class Payment : AggregateRoot
{
    private readonly List<Refund> _refunds = [];

    public Id BookingId { get; private set; }
    public Id CustomerId { get; private set; }

    /// <summary>What this attempt asked the customer for -- the booking's frozen deposit.</summary>
    public Money Amount { get; private set; } = null!;

    public PaymentStatus Status { get; private set; } = null!;

    /// <summary>
    /// Which provider this attempt went to, stored on the row rather than read from configuration.
    /// </summary>
    /// <remarks>
    /// A platform that changes provider still has to refund captures the OLD one took. Reading the
    /// current setting at refund time would send the instruction to a provider that never saw the
    /// money.
    /// </remarks>
    public string Provider { get; private set; } = null!;

    /// <summary>The provider's own id for this session. Null until they answer.</summary>
    public string? ProviderReference { get; private set; }

    /// <summary>Where to send the customer. Null until the provider answers.</summary>
    public string? CheckoutUrl { get; private set; }

    /// <summary>
    /// When this attempt stops being usable -- capped below the booking's own payment deadline, so
    /// the provider itself refuses a payment that would land too late to be applied.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>What the provider actually took. Null until a capture lands.</summary>
    public Money? AmountCaptured { get; private set; }

    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? AppliedAt { get; private set; }
    public DateTimeOffset? OrphanedAt { get; private set; }
    public DateTimeOffset? FailedAt { get; private set; }

    /// <summary>
    /// The provider's own word for why this failed, or ours. A code, never a sentence: an admin
    /// screen groups on it and a customer reads it in their own language.
    /// </summary>
    public string? FailureCode { get; private set; }

    /// <summary>Why the capture could not be applied. Set only on an orphan.</summary>
    public string? OrphanReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    /// <summary>What has been sent back or is on its way. A failed refund does not count.</summary>
    /// <remarks>
    /// Totalled in the CAPTURED currency, not the requested one. They are the same on every applied
    /// payment -- <see cref="CanAcceptCapture"/> insists on it -- but an ORPHAN may hold a capture in
    /// another currency, which is one of the reasons it was orphaned. Starting from the asked-for
    /// currency there would make this property throw on exactly the rows somebody is investigating.
    /// </remarks>
    public Money RefundedTotal =>
        _refunds
            .Where(refund => refund.Status.IsOutstanding || refund.Status == RefundStatus.Settled)
            .Aggregate(
                Money.ZeroIn((AmountCaptured ?? Amount).CurrencyCode),
                (total, refund) => total.Add(refund.Amount));

    private Payment()
    {
    }

    private Payment(Id id) : base(id)
    {
    }

    /// <summary>
    /// Opens an attempt. The row exists before the provider is called, and that order matters: the
    /// row's own id is the idempotency key sent to the provider, so a crash between the two leaves
    /// something to resume from rather than a charge nobody can trace.
    /// </summary>
    public static Payment Open(
        Id bookingId,
        Id customerId,
        Money amount,
        string provider,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        if (bookingId.IsEmpty || customerId.IsEmpty)
            throw new DomainException("A payment requires a booking and a customer.");
        if (amount.IsZero)
            throw new DomainException("A payment for nothing cannot be opened.");
        if (expiresAt <= now)
            throw new DomainException("A payment attempt cannot expire before it opens.");

        return new Payment(Id.New())
        {
            BookingId = bookingId,
            CustomerId = customerId,
            Amount = amount,
            Provider = provider,
            Status = PaymentStatus.Initiated,
            ExpiresAt = expiresAt,
            CreatedAt = now
        };
    }

    /// <summary>The provider answered: this attempt now has somewhere for the customer to go.</summary>
    public UnitResult<Error> AttachProviderSession(string providerReference, string checkoutUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkoutUrl);
        if (Status != PaymentStatus.Initiated)
            return UnitResult.Failure(PaymentErrors.NotLive);

        ProviderReference = providerReference;
        CheckoutUrl = checkoutUrl;
        Status = PaymentStatus.Pending;
        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// This attempt ended with no money moving: the provider declined or let its session lapse, or
    /// the platform superseded it when the customer opened a fresh one.
    /// </summary>
    /// <remarks>
    /// Idempotent, because a provider may send its expiry notice more than once. It refuses only on a
    /// CAPTURED payment, where calling it would be a lie about money.
    /// </remarks>
    public UnitResult<Error> Fail(string failureCode, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        if (Status.IsCaptured)
            return UnitResult.Failure(PaymentErrors.AlreadyCaptured);
        if (Status == PaymentStatus.Failed)
            return UnitResult.Success<Error>();

        Status = PaymentStatus.Failed;
        FailureCode = failureCode;
        FailedAt = now;
        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// The provider took the money and the booking took the payment. Terminal and good.
    /// </summary>
    /// <remarks>
    /// The caller must have confirmed the booking in the SAME transaction. This method does not know
    /// how to, and deliberately cannot: crossing into Bookings from here would put two aggregates'
    /// invariants in one class.
    /// </remarks>
    public UnitResult<Error> Apply(Money captured, DateTimeOffset capturedAt, DateTimeOffset now)
    {
        var guard = CanAcceptCapture(captured);
        if (guard.IsFailure)
            return guard;

        AmountCaptured = captured;
        CapturedAt = capturedAt;
        AppliedAt = now;
        Status = PaymentStatus.Applied;
        AddDomainEvent(new PaymentApplied(Id, BookingId, CustomerId, captured.Amount, captured.CurrencyCode, now));
        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// The provider took the money and nothing could be done with it, so it goes back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <para>
    /// The refund is created HERE rather than left to the caller. An orphaned capture with no refund
    /// row is a customer's money sitting on the platform with nothing that says it is owed, and the
    /// only way to guarantee the two are never separated is to make one impossible without the other.
    /// </para>
    /// <para>
    /// It deliberately does NOT run <see cref="CanAcceptCapture"/>. A capture for the WRONG AMOUNT is
    /// one of the reasons a payment is orphaned in the first place, so refusing to orphan it on that
    /// ground would strand exactly the money most in need of going back. The only thing that can stop
    /// an orphan is a payment that was already captured, where a second capture would be a second
    /// refund of money that only arrived once. What goes back is what the provider actually TOOK,
    /// never what this row asked for.
    /// </para>
    /// </remarks>
    public UnitResult<Error> Orphan(Money captured, DateTimeOffset capturedAt, string reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(captured);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status.IsCaptured)
            return UnitResult.Failure(PaymentErrors.AlreadyCaptured);

        AmountCaptured = captured;
        CapturedAt = capturedAt;
        OrphanedAt = now;
        OrphanReason = reason;
        Status = PaymentStatus.Orphaned;
        _refunds.Add(Refund.Request(Id, captured, RefundReason.OrphanedCapture, disputeTicketId: null, now));
        AddDomainEvent(new PaymentOrphaned(
            Id, BookingId, CustomerId, captured.Amount, captured.CurrencyCode, reason, now));
        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Records that an admin's resolution returns money to the customer.
    /// </summary>
    /// <remarks>
    /// Only against a payment that was actually applied: a refund on an attempt that never captured
    /// would be an instruction to send money the platform never received.
    /// </remarks>
    public Result<Refund, Error> RequestRefund(Money amount, Id? disputeTicketId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(amount);
        if (Status != PaymentStatus.Applied)
            return PaymentErrors.NotLive;
        if (amount.IsZero)
            throw new DomainException("A refund of nothing cannot be requested.");

        var wouldBe = RefundedTotal.Add(amount);
        if (wouldBe.IsGreaterThan(AmountCaptured!))
            return PaymentErrors.RefundExceedsCapture;

        var refund = Refund.Request(Id, amount, RefundReason.DisputeResolution, disputeTicketId, now);
        _refunds.Add(refund);
        return refund;
    }

    /// <summary>Whether this attempt is still one a customer could pay through, at this instant.</summary>
    public bool IsUsable(DateTimeOffset now) => Status.IsLive && now < ExpiresAt;

    /// <summary>
    /// The two things that must hold before a capture may be recorded, whatever the provider says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Public so a caller can ask BEFORE it starts changing anything else. The webhook handler has to
    /// confirm a booking and record a capture in one transaction, and discovering the capture is
    /// unusable after the booking was already confirmed would leave a mutated aggregate with nothing
    /// to undo it -- <c>Booking</c> has no <c>Unconfirm</c>, and giving it one to serve this would be
    /// a worse cure than the disease.
    /// </para>
    /// <para>
    /// A capture on an already-captured payment is refused rather than made idempotent: the caller
    /// guards replays at the receipt table, where a duplicate is recognised as a duplicate BEFORE
    /// anything is touched. Reaching here twice means two different provider events claimed the same
    /// payment, and that is a discrepancy to surface, not to swallow.
    /// </para>
    /// </remarks>
    public UnitResult<Error> CanAcceptCapture(Money captured)
    {
        ArgumentNullException.ThrowIfNull(captured);
        if (Status.IsCaptured)
            return UnitResult.Failure(PaymentErrors.AlreadyCaptured);
        // Amount AND currency. A provider misconfigured onto another currency would otherwise capture
        // "18" of something else and confirm a rental against it.
        if (captured != Amount)
            return UnitResult.Failure(PaymentErrors.AmountMismatch);
        return UnitResult.Success<Error>();
    }
}
