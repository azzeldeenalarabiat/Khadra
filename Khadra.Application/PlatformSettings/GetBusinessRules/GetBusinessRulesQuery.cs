using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.PlatformSettings.GetBusinessRules;

/// <summary>
/// The business numbers in force, as a wire shape.
///
/// A DTO rather than the <c>BusinessRules</c> record itself, and that is the whole point of it. This
/// endpoint used to serialise the domain snapshot directly, so its one money value went out as the
/// domain's <c>Money</c> — `{ amount, currencyCode }` — while every other endpoint on the platform
/// maps money through <see cref="MoneyDto"/> and sends `{ amount, currency }`. The console reads
/// `currency`, so the settings screen displayed "Delivery fee 10 undefined": a money value with no
/// currency on it, which is exactly what the frontend rules forbid.
///
/// Everything else here is a plain number and travels unchanged; only the money needed mapping, and
/// mapping it through the shared DTO is what stops the two shapes drifting apart again.
/// </summary>
public sealed record BusinessRulesDto(
    decimal CommissionPercent,
    decimal DepositPercent,
    int NoShowTimeoutHours,
    decimal DealerNonDeliveryPenaltyMinPercent,
    decimal DealerNonDeliveryPenaltyMaxPercent,
    int FreeCancellationWindowMinutes,
    int AdminSlaHours,
    decimal CustomerCancellationPenaltyPercent,
    int PaymentWindowHours,
    int PostReturnSettlementHours,
    int? MinimumRenterAge,
    int EarliestVehicleModelYear)
{
    public static BusinessRulesDto From(BusinessRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return new BusinessRulesDto(
            rules.CommissionPercent,
            rules.DepositPercent,
            rules.NoShowTimeoutHours,
            rules.DealerNonDeliveryPenaltyMinPercent,
            rules.DealerNonDeliveryPenaltyMaxPercent,
            rules.FreeCancellationWindowMinutes,
            rules.AdminSlaHours,
            rules.CustomerCancellationPenaltyPercent,
            rules.PaymentWindowHours,
            rules.PostReturnSettlementHours,
            rules.MinimumRenterAge,
            rules.EarliestVehicleModelYear);
    }
}

/// <param name="Source">
/// Configuration today. The <c>BusinessRuleSettings</c> aggregate exists for the editable version,
/// but it and <c>BusinessRules</c> do not carry the same fields, and two of the values here — the
/// dealer non-delivery tier and the customer cancellation penalty — are open owner decisions
/// (spec 2.2). An editable screen would let an administrator settle them by typing into a box.
/// </param>
public sealed record BusinessRulesView(BusinessRulesDto Rules, string Source, bool IsEditable);

public sealed record GetBusinessRulesQuery : IQuery<Result<BusinessRulesView, Error>>;

public sealed class GetBusinessRulesHandler(IBusinessRulesProvider rules)
    : IRequestHandler<GetBusinessRulesQuery, Result<BusinessRulesView, Error>>
{
    public async Task<Result<BusinessRulesView, Error>> Handle(
        GetBusinessRulesQuery request,
        CancellationToken cancellationToken)
    {
        var snapshot = await rules.GetAsync(cancellationToken);
        return new BusinessRulesView(BusinessRulesDto.From(snapshot), "Configuration", IsEditable: false);
    }
}
