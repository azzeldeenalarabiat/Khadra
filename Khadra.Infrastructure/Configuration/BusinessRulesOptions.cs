using System.ComponentModel.DataAnnotations;

namespace Khadra.Infrastructure.Configuration;

// Spec §2 "Confirmed Business Rules". Values live in configuration (later: an admin-editable
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
}
