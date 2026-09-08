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

    private RefundReason(int id, string name) : base(id, name)
    {
    }
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
