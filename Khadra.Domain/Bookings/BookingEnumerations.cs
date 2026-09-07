using Khadra.Domain.Common;

namespace Khadra.Domain.Bookings;

// The booking lifecycle. Every transition is forward-only, and the terminal set is
// {Rejected, Cancelled, NoShow, Expired, Completed} so no booking can sit in limbo forever.
public sealed class BookingStatus : Enumeration
{
    // NOTE: id 1 and the name "PendingPayment" are RETIRED and must never be reused. The deposit
    // used to be paid before a dealer ever saw the request; the owner reversed that on 2026-09-07
    // (docs/spec-amendments.md). A persisted name is a contract with every stored row and with the
    // exclusion constraint that spells these out in SQL, so a retired one stays retired.

    // The customer has asked. Nothing is owed yet, and the car is held while the dealer decides.
    public static readonly BookingStatus Requested = new(2, "Requested");
    // The dealer said yes. The car is still held, and the customer now has a window to pay the
    // deposit. Approved does NOT mean paid: that is Confirmed.
    public static readonly BookingStatus Approved = new(3, "Approved");
    public static readonly BookingStatus Rejected = new(4, "Rejected");
    public static readonly BookingStatus PickedUp = new(5, "PickedUp");
    public static readonly BookingStatus Returned = new(6, "Returned");
    // Settled: no open dispute after the post-return window. Reviews unlock here.
    public static readonly BookingStatus Completed = new(7, "Completed");
    public static readonly BookingStatus Cancelled = new(8, "Cancelled");
    public static readonly BookingStatus NoShow = new(9, "NoShow");
    // Nobody acted in time. Never a party's fault, so it never carries a penalty.
    public static readonly BookingStatus Expired = new(10, "Expired");
    // The deposit has been paid and the rental is on. A new id: 1 is retired, never recycled.
    public static readonly BookingStatus Confirmed = new(11, "Confirmed");
    private BookingStatus(int id, string name) : base(id, name)
    {
    }

    public bool IsTerminal =>
        this == Rejected || this == Cancelled || this == NoShow || this == Expired || this == Completed;

    // The states in which this booking occupies the vehicle and must block an overlapping booking.
    /// <summary>The states in which this booking occupies the vehicle.</summary>
    /// <remarks>
    /// Requested holds it because a request is exclusive: the customer is told the car is theirs
    /// pending a decision. Approved still holds it while the deposit is outstanding. Whether a hold
    /// is still LIVE also depends on a deadline -- an unanswered request and an unpaid approval both
    /// stop holding the car the moment their window closes, before anything expires the row -- and
    /// that part lives in BookingHolds, because it needs a clock the domain does not have.
    /// </remarks>
    public bool HoldsVehicle =>
        this == Requested || this == Approved || this == Confirmed || this == PickedUp;
}

public sealed class PickupMethod : Enumeration
{
    public static readonly PickupMethod SelfPickup = new(1, "SelfPickup");
    public static readonly PickupMethod Delivery = new(2, "Delivery");

    private PickupMethod(int id, string name) : base(id, name)
    {
    }
}

// Spec 5.3. FullUpfront needs a payout rail to the dealer, which does not exist yet, so the
// application layer must keep it closed until the owner approves that work.
public sealed class PaymentOption : Enumeration
{
    public static readonly PaymentOption DepositOnly = new(1, "DepositOnly");
    public static readonly PaymentOption FullUpfront = new(2, "FullUpfront");

    private PaymentOption(int id, string name) : base(id, name)
    {
    }
}

public sealed class BookingParty : Enumeration
{
    public static readonly BookingParty Customer = new(1, "Customer");
    public static readonly BookingParty Dealer = new(2, "Dealer");
    // The platform acting on a timer, not a person.
    public static readonly BookingParty System = new(3, "System");
    // Nobody can be blamed from the facts available. Spec 3.3: an admin decides, if anyone asks.
    public static readonly BookingParty Unattributed = new(4, "Unattributed");

    /// <summary>
    /// A named administrator acting on the platform's behalf.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="System"/>, which is a timer. An admin intervening in a booking is a
    /// person, and the status history has to say which one.
    ///
    /// It is also the only safe party for an admin cancellation: <c>AssessCancellation</c> attributes
    /// a penalty to <see cref="Customer"/> and <see cref="Dealer"/>, so cancelling "on behalf of" the
    /// customer would have an administrator assess the full deposit against them by hand. Every other
    /// party falls through to no penalty, which is what the platform cancelling actually means.
    /// </remarks>
    public static readonly BookingParty Admin = new(5, "Admin");

    private BookingParty(int id, string name) : base(id, name)
    {
    }
}

public sealed class HandoverType : Enumeration
{
    public static readonly HandoverType Pickup = new(1, "Pickup");
    public static readonly HandoverType Return = new(2, "Return");

    private HandoverType(int id, string name) : base(id, name)
    {
    }
}
