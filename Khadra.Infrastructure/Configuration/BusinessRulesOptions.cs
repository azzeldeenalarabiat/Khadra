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

    [Range(0, 1000)]
    public decimal DeliveryFeeJod { get; init; }

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

    [Range(5, 1440)]
    public int PaymentWindowMinutes { get; init; }

    [Range(0, 720)]
    public int PostReturnSettlementHours { get; init; }

    // Spec 5.1 "minimum age enforced at registration". Confirmed by the owner as 21. Nullable so the
    // rule can be switched off deliberately rather than by deleting a line.
    [Range(18, 30)]
    public int? MinimumRenterAge { get; init; }

    // The oldest model year a dealer may list. This is a guard against a typo — "1200", "19" — not a
    // judgement about what is rentable: an older car in sound condition is an ordinary listing on
    // this market, and the owner can lower it without a deploy. It was a `const` in the domain,
    // which this project's own rule forbids for a number a screen shows and an owner may want to
    // move.
    [Range(1900, 2100)]
    public int EarliestVehicleModelYear { get; init; }
}
