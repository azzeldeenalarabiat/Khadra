using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.PlatformSettings.GetBusinessRules;

/// <summary>
/// The business numbers in force, and where they come from.
/// </summary>
/// <param name="Source">
/// Configuration today. The <c>BusinessRuleSettings</c> aggregate exists for the editable version,
/// but it and <c>BusinessRules</c> do not carry the same fields, and two of the values here — the
/// dealer non-delivery tier and the customer cancellation penalty — are open owner decisions
/// (spec 2.2). An editable screen would let an administrator settle them by typing into a box.
/// </param>
public sealed record BusinessRulesView(BusinessRules Rules, string Source, bool IsEditable);

public sealed record GetBusinessRulesQuery : IQuery<Result<BusinessRulesView, Error>>;

public sealed class GetBusinessRulesHandler(IBusinessRulesProvider rules)
    : IRequestHandler<GetBusinessRulesQuery, Result<BusinessRulesView, Error>>
{
    public async Task<Result<BusinessRulesView, Error>> Handle(
        GetBusinessRulesQuery request,
        CancellationToken cancellationToken)
    {
        var snapshot = await rules.GetAsync(cancellationToken);
        return new BusinessRulesView(snapshot, "Configuration", IsEditable: false);
    }
}
