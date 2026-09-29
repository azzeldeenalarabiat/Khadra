using Khadra.Domain.Common;

namespace Khadra.Domain.Payables;

/// <summary>
/// How a paid booking ended, as the office's payable reads it (payments Phase 8). Each outcome names where the
/// booking's money went and so what the office is owed for it; the figures themselves are the one calculator's.
/// </summary>
public sealed class PayableOutcome : Enumeration
{
    /// <summary>The rental happened and completed with no dispute decision: everything paid online is the office's.</summary>
    public static readonly PayableOutcome Rental = new(1, "Rental");

    /// <summary>
    /// The rental happened and a dispute decided its deposit: the office has what was paid above the deposit and
    /// its share of the deposit.
    /// </summary>
    public static readonly PayableOutcome RentalAfterDispute = new(2, "RentalAfterDispute");

    /// <summary>The booking ended before pickup and a dispute decided its deposit: the office has its share.</summary>
    public static readonly PayableOutcome DisputeDecided = new(3, "DisputeDecided");

    /// <summary>
    /// The booking ended before pickup with a penalty on the customer, and its dispute window closed with no
    /// dispute: the penalty is kept from the deposit, for the office (owner, 2026-09-29; pre-launch item 164).
    /// </summary>
    public static readonly PayableOutcome PenaltyKept = new(4, "PenaltyKept");

    /// <summary>The booking ended before pickup with no penalty on the customer: the deposit went back.</summary>
    public static readonly PayableOutcome DepositReleased = new(5, "DepositReleased");

    /// <summary>The whole payment went back: a free cancellation, or an administrator's.</summary>
    public static readonly PayableOutcome PaymentReturned = new(6, "PaymentReturned");

    private PayableOutcome(int id, string name) : base(id, name)
    {
    }

    /// <summary>Whether the booking's money reached the office at all. The two that did not can still carry a charge.</summary>
    public bool CarriesOfficeMoney => this != DepositReleased && this != PaymentReturned;
}

/// <summary>
/// One line of a payable. Every amount is positive; the kind says which way it goes — towards the office (its
/// money) or away from it (Khadra's commission, a charge a dispute assessed).
/// </summary>
public sealed class PayableLineKind : Enumeration
{
    /// <summary>What the customer paid online towards a rental that happened.</summary>
    public static readonly PayableLineKind RentalRevenue = new(1, "RentalRevenue", towardsOffice: true);

    /// <summary>The share of the deposit a resolved dispute transferred to the office. Its source is the ticket.</summary>
    public static readonly PayableLineKind DisputeShare = new(2, "DisputeShare", towardsOffice: true);

    /// <summary>A customer's penalty kept from the deposit when the dispute window closed with no dispute.</summary>
    public static readonly PayableLineKind PenaltyKept = new(3, "PenaltyKept", towardsOffice: true);

    /// <summary>Khadra's commission: the booking's frozen figure, never more than the office's money on it.</summary>
    public static readonly PayableLineKind Commission = new(4, "Commission", towardsOffice: false);

    /// <summary>What a resolved dispute charged the office. Its source is the ticket.</summary>
    public static readonly PayableLineKind DisputeCharge = new(5, "DisputeCharge", towardsOffice: false);

    private PayableLineKind(int id, string name, bool towardsOffice) : base(id, name)
    {
        TowardsOffice = towardsOffice;
    }

    /// <summary>Whether the line is the office's money (its sign is +), rather than taken from it (−).</summary>
    public bool TowardsOffice { get; }
}

/// <summary>Which way the money of a settlement went: its sign, as a word.</summary>
public sealed class SettlementDirection : Enumeration
{
    /// <summary>Khadra paid the office what it was owed.</summary>
    public static readonly SettlementDirection Payout = new(1, "Payout");

    /// <summary>The office paid Khadra what it owed, its charges being more than its money.</summary>
    public static readonly SettlementDirection Received = new(2, "Received");

    /// <summary>What the office was owed and what it owed cancelled out: no money moved, and the payables are closed.</summary>
    public static readonly SettlementDirection Netted = new(3, "Netted");

    private SettlementDirection(int id, string name) : base(id, name)
    {
    }

    /// <summary>The direction of a signed amount, from Khadra's side: positive is paid out.</summary>
    public static SettlementDirection Of(decimal amount) =>
        amount > 0m ? Payout : amount < 0m ? Received : Netted;
}

/// <summary>
/// Why a booking's payable is held back: not recorded yet, or recorded and left out of settlements. Administrators
/// see every open hold with its reason; nothing about an office's money is ever silently missing.
/// </summary>
public sealed class PayableHoldReason : Enumeration
{
    /// <summary>The booking's financial records contradict one another: no payable is recorded from them.</summary>
    public static readonly PayableHoldReason NeedsReview = new(1, "NeedsReview", bySystem: true, beforeRecording: true);

    /// <summary>
    /// The customer's penalty is not the whole deposit held, and nothing can return the rest yet: no payable is
    /// recorded (pre-launch item 205).
    /// </summary>
    public static readonly PayableHoldReason PenaltyNotWholeDeposit = new(2, "PenaltyNotWholeDeposit", bySystem: true, beforeRecording: true);

    /// <summary>The recorded payable no longer matches the booking's records: it is left out of every settlement.</summary>
    public static readonly PayableHoldReason Contradicted = new(3, "Contradicted", bySystem: true, beforeRecording: false);

    /// <summary>An administrator left the payable out of settlements, and said why.</summary>
    public static readonly PayableHoldReason Manual = new(4, "Manual", bySystem: false, beforeRecording: false);

    private PayableHoldReason(int id, string name, bool bySystem, bool beforeRecording) : base(id, name)
    {
        BySystem = bySystem;
        BeforeRecording = beforeRecording;
    }

    /// <summary>Opened and released by the payables pass, never by a person.</summary>
    public bool BySystem { get; }

    /// <summary>Held on the BOOKING, because nothing can be recorded; the others hold a recorded payable.</summary>
    public bool BeforeRecording { get; }
}
