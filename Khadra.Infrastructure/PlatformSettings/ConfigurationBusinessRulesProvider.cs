using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.PlatformSettings;

// Configuration-backed source of the business numbers. Swapped for a database-backed provider when
// the Admin console gets its settings screen; consumers keep using IBusinessRulesProvider.
internal sealed class ConfigurationBusinessRulesProvider(IOptionsMonitor<BusinessRulesOptions> options)
    : IBusinessRulesProvider
{
    public Task<BusinessRules> GetAsync(CancellationToken cancellationToken = default)
    {
        var current = options.CurrentValue;
        return Task.FromResult(new BusinessRules(
            current.CommissionPercent,
            current.DepositPercent,
            current.NoShowTimeoutHours,
            current.DealerNonDeliveryPenaltyMinPercent,
            current.DealerNonDeliveryPenaltyMaxPercent,
            current.FreeCancellationWindowMinutes,
            current.AdminSlaHours,
            current.CustomerCancellationPenaltyPercent,
            current.PaymentWindowHours,
            current.BookingAnswerWindowHours,
            current.PostReturnSettlementHours,
            current.MinimumRenterAge,
            current.TurnaroundMinutes!.Value,
            current.MaxAdvanceBookingDays!.Value,
            current.MinimumBookingLeadTimeMinutes!.Value,
            current.MaxRentalDays!.Value,
            current.EarliestVehicleModelYear));
    }
}
