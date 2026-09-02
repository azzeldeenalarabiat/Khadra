using Khadra.Domain.Common;

namespace Khadra.Application.Common.Ports;

// The confirmed business numbers from the spec (§2). They are configuration today and will become an
// admin-editable PlatformSettings aggregate later; consumers only ever see this snapshot.
public sealed record BusinessRules(
    decimal CommissionPercent,
    decimal DepositPercent,
    int NoShowTimeoutHours,
    Money DeliveryFee,
    decimal DealerNonDeliveryPenaltyMinPercent,
    decimal DealerNonDeliveryPenaltyMaxPercent,
    int FreeCancellationWindowMinutes,
    int AdminSlaHours);

public interface IBusinessRulesProvider
{
    Task<BusinessRules> GetAsync(CancellationToken cancellationToken = default);
}
