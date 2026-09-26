using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using Khadra.Domain.Payments.Events;

namespace Khadra.Domain.Payments;

/// <summary>
/// One checkout attempt for one booking — its deposit or its full amount — and whatever had to go back.
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

    /// <summary>
    /// EXACTLY what the provider is asked to capture: the booking amount this payment covers, plus any
    /// processing fee. The capture check compares against this and nothing else.
    /// </summary>
    public Money Amount { get; private set; } = null!;

    /// <summary>What this attempt is for. Deposit on every row made before 2026-09-24.</summary>
    public PaymentPurpose Purpose { get; private set; } = PaymentPurpose.Deposit;

    // Stored as a bare figure in the payment's own currency: a second currency column for one row can
    // only ever agree with the first, or be wrong.
    private decimal _processingFee;

    /// <summary>
    /// The part of <see cref="Amount"/> that is an online-payment processing fee (zero unless the fee
    /// is enabled and applies to this purpose). Never commission, never rental, never tax.
    /// </summary>
    public Money ProcessingFee => Money.Create(_processingFee, Amount.CurrencyCode);

    // Whether the fee goes back with the payment, frozen when the attempt opened: a later change to the
    // platform's rule never re-judges a capture already taken.
    private bool _feeRefundable = true;

    /// <summary>Whether <see cref="ProcessingFee"/> is returned when this payment is refunded in full.</summary>
    public bool FeeRefundable => _feeRefundable;

    /// <summary>What this payment puts towards the booking itself: <see cref="Amount"/> less the fee.</summary>
    public Money AppliedToBooking => Amount.Subtract(ProcessingFee);

    /// <summary>
    /// The processing fee that goes back when this payment's booking money does: the whole fee when
    /// the payment froze it as refundable, nothing otherwise.
    /// </summary>
    public Money RefundableFee => FeeRefundable ? ProcessingFee : Money.ZeroIn(Amount.CurrencyCode);

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

    /// <summary>
    /// Whether no money moved for this attempt, and never could have.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="Provider"/> rather than stored beside it, deliberately. A second
    /// persisted flag can disagree with the first, and the one that would be trusted is whichever the
    /// screen happened to read. This is the same fact asked a different way, so it cannot drift.
    /// </remarks>
    public bool IsSandbox => PaymentProviders.IsSandbox(Provider);

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
    /// <remarks>
    /// <para>
    /// <b>It belongs to <see cref="PaymentStatus.Failed"/> and to nothing else.</b> A payment that
    /// took money explains itself with <see cref="AmountCaptured"/> and, if the money could not be
    /// used, with <see cref="OrphanReason"/>; a failure code on such a row is describing an attempt
    /// that is over. So every transition into a CAPTURED state clears it, and the only one that sets
    /// it is <see cref="Fail"/>.
    /// </para>
    /// <para>
    /// This is not hypothetical tidiness. A payment can legitimately fail and then be captured: the
    /// sweep closes an attempt whose session the provider has forgotten, and a real capture for that
    /// same session lands afterwards, inside the booking's own payment window. Before 2026-09-21 the
    /// row then read <c>Applied</c> while still carrying <c>sandbox_session_forgotten</c> — a paid
    /// booking whose payment record gave a reason it had not worked. The manual lifecycle run
    /// produced exactly that row.
    /// </para>
    /// </remarks>
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
    /// <para>
    /// EVERY refund counts, whatever its status. A FAILED refund is still owed — the sweep re-sends it
    /// under its own id — so leaving it out (as this total once did) let a second refund be promised
    /// on top of it, and the two could together exceed the capture. No refund row is ever withdrawn:
    /// each one is money the platform has promised back.
    /// </para>
    public Money RefundedOrOwed =>
        _refunds.Aggregate(
            Money.ZeroIn((AmountCaptured ?? Amount).CurrencyCode),
            (total, refund) => total.Add(refund.Amount));

    /// <summary>What has actually reached the customer: the SETTLED refunds, and nothing else.</summary>
    public Money RefundSettled =>
        _refunds
            .Where(refund => refund.Status == RefundStatus.Settled)
            .Aggregate(
                Money.ZeroIn((AmountCaptured ?? Amount).CurrencyCode),
                (total, refund) => total.Add(refund.Amount));

    /// <summary>
    /// Whether everything this payment will ever give back has now reached the customer: the whole
    /// capture, less a processing fee it froze as non-refundable (Phase 3). What decides "your payment
    /// has been refunded" against "part of your payment has been refunded".
    /// </summary>
    /// <remarks>
    /// At least, not exactly: an orphaned capture goes back whole, fee included, which is more than a
    /// booking's ending would ever return.
    /// </remarks>
    public bool IsRefundedInFull =>
        WholePaymentRefundAmount is { } whole &&
        !whole.IsZero &&
        !whole.IsGreaterThan(RefundSettled);

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
    /// <param name="amount">The whole charge: booking amount plus <paramref name="processingFee"/>.</param>
    /// <param name="purpose">What it is for. Deposit when omitted, as every attempt was before 2026-09-24.</param>
    /// <param name="processingFee">The fee inside <paramref name="amount"/>. Zero when omitted.</param>
    public static Payment Open(
        Id bookingId,
        Id customerId,
        Money amount,
        string provider,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        PaymentPurpose? purpose = null,
        Money? processingFee = null,
        bool feeRefundable = true)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        if (bookingId.IsEmpty || customerId.IsEmpty)
            throw new DomainException("A payment requires a booking and a customer.");
        if (amount.IsZero)
            throw new DomainException("A payment for nothing cannot be opened.");
        if (expiresAt <= now)
            throw new DomainException("A payment attempt cannot expire before it opens.");
        var fee = processingFee ?? Money.ZeroIn(amount.CurrencyCode);
        if (!string.Equals(fee.CurrencyCode, amount.CurrencyCode, StringComparison.Ordinal))
            throw new DomainException("A processing fee must be in the payment's own currency.");
        if (fee.Amount >= amount.Amount)
            throw new DomainException("A processing fee cannot be the whole payment.");

        return new Payment(Id.New())
        {
            BookingId = bookingId,
            CustomerId = customerId,
            Amount = amount,
            Purpose = purpose ?? PaymentPurpose.Deposit,
            _processingFee = fee.Amount,
            _feeRefundable = feeRefundable,
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
        // The attempt succeeded, so whatever an earlier pass thought had gone wrong is no longer
        // true of this payment. Leaving it would put a failure reason on a row that took money.
        FailureCode = null;
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
        // Same rule as Apply, and reachable by the same route: a swept-Failed attempt whose late
        // capture cannot be used is orphaned, and must not also carry the reason the sweep gave.
        // Money moved; OrphanReason is what explains this row now.
        FailureCode = null;
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

        return AddRefund(amount, RefundReason.DisputeResolution, disputeTicketId, now);
    }

    /// <summary>
    /// Returns the whole deposit because the customer cancelled inside the free-cancellation window
    /// (owner, 2026-09-24).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Idempotent on the refund, not on the call.</b> A second call returns the refund the first one
    /// recorded, whatever has become of it since — Requested, Sent, Settled or Failed. A Failed one is
    /// still owed and the sweep re-sends it under its own id; minting a second row would be a second
    /// instruction for one deposit.
    /// </para>
    /// <para>
    /// <b>The full capture, never "what is left".</b> No refund can exist on a payment before its
    /// booking is cancelled (a dispute needs a finished booking), so anything already refunded here
    /// would be a bug, and quietly refunding the remainder would hide it. The shared guard refuses it.
    /// </para>
    /// </remarks>
    public Result<Refund, Error> RefundForFreeCancellation(DateTimeOffset now) =>
        RefundWholePayment(RefundReason.FreeCancellation, now);

    /// <summary>
    /// Returns the WHOLE payment because the booking ended with nothing held against the customer: a
    /// customer's free cancellation, or an administrator's cancellation before pickup (owner,
    /// 2026-09-26). Idempotent the same way as a free cancellation always was: a second call returns
    /// the refund the first one recorded, whatever has become of it.
    /// </summary>
    public Result<Refund, Error> RefundWholePayment(RefundReason reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (!reason.ReturnsWholePayment)
            throw new DomainException($"{reason.Name} does not return the whole payment.");
        if (Status != PaymentStatus.Applied)
            return PaymentErrors.NotLive;

        if (WholePaymentRefund is { } existing)
            return existing;

        return AddRefund(WholePaymentRefundAmount!, reason, disputeTicketId: null, now);
    }

    /// <summary>
    /// Returns everything above the deposit because a PAID booking ended before the car was
    /// collected (owner, 2026-09-24): the booking money above <paramref name="deposit"/>, plus the
    /// processing fee when this payment's fee was refundable. Null when nothing is above the deposit
    /// — every deposit-only payment — so no empty refund row is ever minted.
    /// </summary>
    /// <remarks>
    /// Idempotent: a second call returns the refund the first one recorded. The deposit is never part
    /// of it; penalties and disputes stay deposit-based.
    /// </remarks>
    public Result<Refund?, Error> RefundAboveDeposit(Money bookingPart, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(bookingPart);
        if (Status != PaymentStatus.Applied)
            return PaymentErrors.NotLive;

        if (RefundFor(RefundReason.EndedBeforePickup) is { } existing)
            return existing;

        // Nothing above the deposit, nothing to return: the fee only ever goes back WITH that money,
        // and a deposit-only payment never carries one (Khadra absorbs the deposit's processing cost).
        if (bookingPart.IsZero)
            return (Refund?)null;

        var amount = AmountReturnedFor(bookingPart);
        if (amount is null || amount.IsZero)
            return (Refund?)null;

        return AddRefund(amount, RefundReason.EndedBeforePickup, disputeTicketId: null, now).Map(refund => (Refund?)refund);
    }

    /// <summary>
    /// Returns the deposit a booking held, because its dispute window closed cleanly (owner,
    /// 2026-09-26). The booking part only: a refundable processing fee already went back with the
    /// money above the deposit, and a deposit-only payment carries none. Idempotent.
    /// </summary>
    public Result<Refund?, Error> RefundHeldDeposit(Money deposit, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(deposit);
        if (Status != PaymentStatus.Applied)
            return PaymentErrors.NotLive;

        if (RefundFor(RefundReason.DisputeWindowClosed) is { } existing)
            return existing;

        var amount = HeldDepositRefundAmount(deposit);
        if (amount is null || amount.IsZero)
            return (Refund?)null;

        return AddRefund(amount, RefundReason.DisputeWindowClosed, disputeTicketId: null, now).Map(refund => (Refund?)refund);
    }

    /// <summary>
    /// What a free cancellation returns from this payment: everything captured, except a processing
    /// fee the payment was opened as non-refundable (owner, 2026-09-24: configurable until the
    /// provider's contract says). Null until something was captured.
    /// </summary>
    /// <remarks>
    /// One definition, read by <see cref="RefundForFreeCancellation"/> and by the booking screens that
    /// state the figure BEFORE the customer cancels, so the promise and the refund cannot disagree.
    /// A deposit-only payment returns the deposit; a full payment returns the whole booking.
    /// </remarks>
    public Money? WholePaymentRefundAmount =>
        AmountCaptured is null
            ? null
            : FeeRefundable || ProcessingFee.IsZero
                ? AmountCaptured
                : AmountCaptured.Subtract(ProcessingFee);

    /// <summary>
    /// What returning <paramref name="bookingPart"/> of the booking's money takes back from this
    /// payment: that part, plus the processing fee when this payment froze its fee as refundable.
    /// Null until something was captured.
    /// </summary>
    /// <remarks>
    /// The BOOKING decides the part (<c>Booking.RefundableAboveDeposit</c>); the payment only adds the
    /// fee rule it froze. The fee goes back once, with the money above the deposit — a deposit-only
    /// payment never carries one. With the deposit it keeps and a fee it keeps, it is exactly the
    /// capture: <c>returned + deposit + keptFee == AmountCaptured</c>.
    /// </remarks>
    public Money? AmountReturnedFor(Money bookingPart)
    {
        ArgumentNullException.ThrowIfNull(bookingPart);
        if (AmountCaptured is null)
            return null;

        return Money.Create(bookingPart.Amount, bookingPart.CurrencyCode).Add(RefundableFee);
    }

    /// <summary>The deposit a clean dispute-window close returns: the booking part, never more than was applied.</summary>
    public Money? HeldDepositRefundAmount(Money deposit)
    {
        ArgumentNullException.ThrowIfNull(deposit);
        if (AmountCaptured is null)
            return null;

        var applied = AppliedToBooking;
        return applied.IsGreaterThan(deposit) ? Money.Create(deposit.Amount, deposit.CurrencyCode) : applied;
    }

    /// <summary>
    /// The part of <paramref name="refund"/> that returns this payment's processing fee, by the rule
    /// the refund was recorded under — never a figure a screen works out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fee goes back ONCE and only with the money it rode on: whole with a whole-payment refund
    /// (free or administrator's cancellation) and with the refund of everything above the deposit, when
    /// the payment froze it as refundable; never with the deposit's own release or a dispute's share,
    /// which are booking money alone (<see cref="RefundAboveDeposit"/>,
    /// <see cref="WholePaymentRefundAmount"/>, <see cref="HeldDepositRefundAmount"/>). An orphaned
    /// capture goes back whole, so it carries the fee the attempt asked for, up to what it returns.
    /// </para>
    /// <para>
    /// Every input is frozen on the payment (the fee and whether it is refundable), so the split can
    /// never change after the refund is recorded. Phase 5 stores it on the refund (owner, 2026-09-26);
    /// until then this is where it is read.
    /// </para>
    /// </remarks>
    public Money FeeInside(Refund refund)
    {
        ArgumentNullException.ThrowIfNull(refund);
        if (refund.PaymentId != Id)
            throw new DomainException($"Refund {refund.Id} does not belong to payment {Id}.");

        var currency = refund.Amount.CurrencyCode;
        // A refund in another currency can only be an orphaned capture the provider took in the wrong
        // one; the fee was asked for in this payment's currency, so none of it is inside.
        if (_processingFee == 0m || !string.Equals(currency, Amount.CurrencyCode, StringComparison.Ordinal))
            return Money.ZeroIn(currency);

        var fee = refund.Reason == RefundReason.OrphanedCapture
            ? _processingFee
            : refund.Reason.ReturnsWholePayment || refund.Reason == RefundReason.EndedBeforePickup
                ? RefundableFee.Amount
                : 0m;
        return Money.Create(Math.Min(fee, refund.Amount.Amount), currency);
    }

    /// <summary>What <paramref name="refund"/> returns of the booking's own money: its amount less the fee inside it.</summary>
    public Money BookingMoneyIn(Refund refund)
    {
        ArgumentNullException.ThrowIfNull(refund);
        return refund.Amount.Subtract(FeeInside(refund));
    }

    /// <summary>The one way money is promised back on an applied payment: never beyond what was taken.</summary>
    private Result<Refund, Error> AddRefund(Money amount, RefundReason reason, Id? disputeTicketId, DateTimeOffset now)
    {
        var wouldBe = RefundedOrOwed.Add(amount);
        if (wouldBe.IsGreaterThan(AmountCaptured!))
            return PaymentErrors.RefundExceedsCapture;

        // A fresh Money, never the payment's own tracked instance: EF tracks owned values by
        // reference, and one instance owned by two rows is the pattern the architecture rules forbid.
        var refund = Refund.Request(Id, Money.Create(amount.Amount, amount.CurrencyCode), reason, disputeTicketId, now);
        _refunds.Add(refund);
        return refund;
    }

    /// <summary>The refund a free cancellation recorded, if there is one.</summary>
    public Refund? FreeCancellationRefund => RefundFor(RefundReason.FreeCancellation);

    /// <summary>The refund that returned the whole payment (a free or an administrator's cancellation), if any.</summary>
    public Refund? WholePaymentRefund => _refunds.FirstOrDefault(refund => refund.Reason.ReturnsWholePayment);

    /// <summary>The refund recorded for <paramref name="reason"/>, if there is one.</summary>
    public Refund? RefundFor(RefundReason reason) => _refunds.FirstOrDefault(refund => refund.Reason == reason);

    /// <summary>
    /// The refund a provider's notice names by the reference the provider gave it when it was sent —
    /// what a real provider's refund webhook carries (pre-launch item 159).
    /// </summary>
    public Refund? RefundWithProviderReference(string providerReference) =>
        string.IsNullOrWhiteSpace(providerReference)
            ? null
            : _refunds.FirstOrDefault(refund => string.Equals(refund.ProviderReference, providerReference, StringComparison.Ordinal));

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
