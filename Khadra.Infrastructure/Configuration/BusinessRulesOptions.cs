using System.ComponentModel.DataAnnotations;

namespace Khadra.Infrastructure.Configuration;

// Spec section 2 "Confirmed Business Rules". Values live in configuration (later: an admin-editable
// PlatformSettings aggregate). No defaults here on purpose: a missing value fails validation at startup.
public sealed class BusinessRulesOptions
{
    public const string SectionName = "BusinessRules";

    [Range(0.01, 100)]
    public decimal CommissionPercent { get; init; }

    [Range(0.01, 100)]
    public decimal DepositPercent { get; init; }

    [Range(1, 720)]
    public int NoShowTimeoutHours { get; init; }

    [Range(0, 100)]
    public decimal DealerNonDeliveryPenaltyMinPercent { get; init; }

    [Range(0, 100)]
    public decimal DealerNonDeliveryPenaltyMaxPercent { get; init; }

    [Range(0, 10080)]
    public int FreeCancellationWindowMinutes { get; init; }

    [Range(1, 720)]
    public int AdminSlaHours { get; init; }

    // Awaiting the owner's confirmation; see the note on BusinessRules.
    [Range(0, 100)]
    public decimal CustomerCancellationPenaltyPercent { get; init; }

    [Range(1, 168)]
    public int PaymentWindowHours { get; init; }

    [Range(1, 168)]
    public int BookingAnswerWindowHours { get; init; }

    [Range(0, 720)]
    public int PostReturnSettlementHours { get; init; }

    // Spec 5.1 "minimum age enforced at registration". Confirmed by the owner as 21. Nullable so the
    // rule can be switched off deliberately rather than by deleting a line.
    [Range(18, 30)]
    public int? MinimumRenterAge { get; init; }

    // The gap a gallery needs between one rental coming back and the next going out: cleaning,
    // refuelling, a look over the car. Settled by the owner at 120 minutes on 2026-09-07.
    //
    // Nullable on purpose. An `int` with `[Range(0, ...)]` binds a MISSING key to 0 and passes
    // validation, so a deleted line would read as "no buffer required" and quietly let a car go out
    // the minute it came back. Zero stays a legitimate value; absence has to be an error, and the
    // .Validate in DependencyInjection is what makes it one.
    [Range(0, 1440)]
    public int? TurnaroundMinutes { get; init; }

    // How far ahead a rental may be booked. Nullable for the same reason as the turnaround gap: a
    // missing key must be an error, not a silent zero that would refuse every date.
    [Range(1, 3650)]
    public int? MaxAdvanceBookingDays { get; init; }

    // The soonest a rental may start, from the moment of the request. Nullable for the same reason
    // as the two above: a missing key must be an error rather than a silent zero, which here would
    // read as "a car may be booked for one minute from now" and quietly collapse every window on the
    // booking. Zero is not a legitimate value for this one, but the range starts at 0 so that a
    // deliberate 0 fails the .Validate rather than the binder, with a message that explains itself.
    [Range(0, 10080)]
    public int? MinimumBookingLeadTimeMinutes { get; init; }

    // The longest a single rental may run, in calendar days. Nullable for the same reason as the
    // others: a missing key must be an error rather than a silent zero, which here would refuse
    // every booking on the platform.
    [Range(0, 3650)]
    public int? MaxRentalDays { get; init; }

    // How long after the rental start the customer must wait before reporting non-delivery. Zero is
    // a legitimate and the shipped value, so this is nullable for the same reason as the three above:
    // a missing key must be an error rather than a silent default nobody chose.
    [Range(0, 168)]
    public int? NonDeliveryGraceHours { get; init; }

    // The oldest model year a dealer may list. This is a guard against a typo — "1200", "19" — not a
    // judgement about what is rentable: an older car in sound condition is an ordinary listing on
    // this market, and the owner can lower it without a deploy. It was a `const` in the domain,
    // which this project's own rule forbids for a number a screen shows and an owner may want to
    // move.
    [Range(1900, 2100)]
    public int EarliestVehicleModelYear { get; init; }
}
