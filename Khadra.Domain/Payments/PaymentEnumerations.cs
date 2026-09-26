using Khadra.Domain.Common;

namespace Khadra.Domain.Payments;

/// <summary>
/// One checkout attempt's life. Deliberately has no <c>Captured</c> member.
/// </summary>
/// <remarks>
/// A capture is a FACT about money that has already moved; it is not a state this platform may rest
/// in. The handler that receives one must resolve it, in the same transaction, to either
/// <see cref="Applied"/> (the booking took it) or <see cref="Orphaned"/> (it could not, and the money
/// goes back). Persisting a captured-but-unresolved row would be the worst of both: the provider's
/// event id is already recorded, so the provider's retry is refused as a replay, and the row sits
/// there with a customer's money and no booking behind it, forever.
/// </remarks>
/// <summary>What a payment is FOR. Chosen by the customer when they pay, never when they request.</summary>
/// <remarks>
/// <para>
/// <see cref="Deposit"/> is the mandatory minimum (the booking's frozen deposit) and
/// <see cref="FullPayment"/> the whole booking total online (owner, 2026-09-24). Either one confirms
/// an approved booking. <see cref="RemainingBalance"/> — paying what is left after a deposit, online,
/// after confirmation — is part of the model so a later release needs no migration, and NO customer
/// flow opens one yet (owner, 2026-09-24): the checkout refuses it.
/// </para>
/// <para>
/// Refunds are not a purpose. They are outgoing money hanging off the payment they return
/// (<see cref="Refund"/>), with their own lifecycle and their own idempotency.
/// </para>
/// </remarks>
public sealed class PaymentPurpose : Enumeration
{
    public static readonly PaymentPurpose Deposit = new(1, "Deposit");
    public static readonly PaymentPurpose FullPayment = new(2, "FullPayment");
    public static readonly PaymentPurpose RemainingBalance = new(3, "RemainingBalance");

    private PaymentPurpose(int id, string name) : base(id, name)
    {
    }

    /// <summary>Whether a capture for this purpose can confirm an approved booking.</summary>
    public bool Confirms => this == Deposit || this == FullPayment;
}

public sealed class PaymentStatus : Enumeration
{
    /// <summary>Our row exists; the provider has not been asked yet, or did not answer.</summary>
    public static readonly PaymentStatus Initiated = new(1, "Initiated");

    /// <summary>The provider has a session. The customer may still be looking at it.</summary>
    public static readonly PaymentStatus Pending = new(2, "Pending");

    /// <summary>This attempt ended without money moving. Terminal for the ATTEMPT, not the booking.</summary>
    public static readonly PaymentStatus Failed = new(3, "Failed");

    /// <summary>Captured, and the booking took it. Terminal.</summary>
    public static readonly PaymentStatus Applied = new(4, "Applied");

    /// <summary>Captured, and the booking could not take it. Carries a refund. Terminal.</summary>
    public static readonly PaymentStatus Orphaned = new(5, "Orphaned");

    private PaymentStatus(int id, string name) : base(id, name)
    {
    }

    /// <summary>Whether this attempt is still one the customer could be paying through.</summary>
    public bool IsLive => this == Initiated || this == Pending;

    public bool IsTerminal => !IsLive;

    /// <summary>Whether money actually moved. The two terminal states that mean it did.</summary>
    public bool IsCaptured => this == Applied || this == Orphaned;
}

/// <summary>Why money is going back. Never prose: an admin screen groups on this.</summary>
public sealed class RefundReason : Enumeration
{
    /// <summary>
    /// The provider captured, and the booking could not take it -- it had expired, been cancelled, or
    /// already been paid by another attempt. Nobody chose this refund; it is owed the instant the
    /// capture lands, and is created in the same transaction that records the capture.
    /// </summary>
    public static readonly RefundReason OrphanedCapture = new(1, "OrphanedCapture");

    /// <summary>An admin resolved a dispute and the disposition returns money to the customer.</summary>
    public static readonly RefundReason DisputeResolution = new(2, "DisputeResolution");

    /// <summary>
    /// The customer cancelled a PAID booking inside its free-cancellation window, so the whole deposit
    /// goes back to the card it came from (owner, 2026-09-24). Nobody decides it: it is recorded in the
    /// same transaction as the cancellation, and the sweep sends it.
    /// </summary>
    public static readonly RefundReason FreeCancellation = new(3, "FreeCancellation");

    /// <summary>
    /// A PAID booking ended before the car was collected — a cancellation after the free window, a
    /// no-show, the office never handing the car over — so everything the customer paid ABOVE the
    /// deposit goes back, with the processing fee when it was refundable (owner, 2026-09-24). The
    /// deposit itself keeps the cancellation rules: penalties and disputes stay deposit-based.
    /// </summary>
    public static readonly RefundReason EndedBeforePickup = new(4, "EndedBeforePickup");

    /// <summary>
    /// An administrator cancelled a PAID booking before pickup with no penalty on the customer, so
    /// the whole payment goes back, deposit included (owner, 2026-09-26).
    /// </summary>
    public static readonly RefundReason PlatformCancellation = new(5, "PlatformCancellation");

    /// <summary>
    /// A paid booking ended before pickup and its dispute window closed CLEANLY — no dispute ever
    /// opened, no penalty assessed, nothing else holding it — so the deposit it held goes back
    /// (owner, 2026-09-26; spec 3.3: with no ticket, no penalty applies). Recorded by the sweep.
    /// </summary>
    public static readonly RefundReason DisputeWindowClosed = new(6, "DisputeWindowClosed");

    private RefundReason(int id, string name) : base(id, name)
    {
    }

    /// <summary>Whether this refund returns everything the payment took, deposit included.</summary>
    public bool ReturnsWholePayment => this == FreeCancellation || this == PlatformCancellation;
}

/// <summary>
/// A refund's own life, separate from the payment's because sending it is a second network call that
/// can fail on its own.
/// </summary>
public sealed class RefundStatus : Enumeration
{
    /// <summary>Owed, and recorded. Nothing has been sent yet.</summary>
    public static readonly RefundStatus Requested = new(1, "Requested");

    /// <summary>The provider accepted the instruction.</summary>
    public static readonly RefundStatus Sent = new(2, "Sent");

    /// <summary>The provider says the money is back with the customer.</summary>
    public static readonly RefundStatus Settled = new(3, "Settled");

    /// <summary>The provider refused. Stays visible; a human has to look.</summary>
    public static readonly RefundStatus Failed = new(4, "Failed");

    private RefundStatus(int id, string name) : base(id, name)
    {
    }

    public bool IsOutstanding => this == Requested || this == Sent;
}

/// <summary>
/// Where a payment's refunds stand, read as ONE status (payments Phase 4): nothing refunded, on its way,
/// refused and being sent again, partly back, or everything back. The one definition, read by the
/// booking's financial state and the administrator's payments list alike.
/// </summary>
public sealed class RefundProgress : Enumeration
{
    /// <summary>Nothing has been refunded.</summary>
    public static readonly RefundProgress None = new(1, "None");

    /// <summary>A refund is on its way.</summary>
    public static readonly RefundProgress InProgress = new(2, "InProgress");

    /// <summary>A refund was refused and is being sent again: still owed.</summary>
    public static readonly RefundProgress Delayed = new(3, "Delayed");

    /// <summary>Every refund shown reached the customer, and part of the payment was kept.</summary>
    public static readonly RefundProgress Partial = new(4, "Partial");

    /// <summary>Everything the reader is owed back has reached the customer.</summary>
    public static readonly RefundProgress Complete = new(5, "Complete");

    private RefundProgress(int id, string name) : base(id, name)
    {
    }

    /// <summary>
    /// One reading of the refunds a reader is shown. A refused refund outranks one on its way, because it
    /// needs a human; whether the rest is complete is the caller's to say — for the whole payment,
    /// <see cref="Payment.IsWhollyReturned"/>; for a reader shown only some refunds, what those cover.
    /// </summary>
    public static RefundProgress Of(IEnumerable<Refund> shown, bool complete)
    {
        ArgumentNullException.ThrowIfNull(shown);
        var refunds = shown.ToList();
        if (refunds.Count == 0)
            return None;
        if (refunds.Exists(refund => refund.Status == RefundStatus.Failed))
            return Delayed;
        if (refunds.Exists(refund => refund.Status.IsOutstanding))
            return InProgress;
        return complete ? Complete : Partial;
    }
}
