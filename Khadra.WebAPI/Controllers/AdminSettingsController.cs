using Khadra.Application.Common;
using Khadra.Application.PlatformSettings.GetBusinessRules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The numbers the whole platform runs on (spec 2).
///
/// Read-only, and it says so. They come from configuration through <c>IBusinessRulesProvider</c>;
/// the <c>BusinessRuleSettings</c> aggregate that would make them editable is not wired to a table,
/// does not carry the same fields, and two of the values are still open owner decisions. Serving
/// them read-only is the honest state — an editable form would invite an administrator to settle a
/// business question by typing into a box.
/// </summary>
[Authorize(Policy = SecurityPolicies.Admin)]
[Route("api/v1/admin/settings")]
public sealed class AdminSettingsController : ApiControllerBase
{
    [HttpGet("business-rules")]
    [ProducesResponseType<BusinessRulesView>(StatusCodes.Status200OK)]
    public async Task<ActionResult> BusinessRules(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetBusinessRulesQuery(), cancellationToken);
        return FromResult(result);
    }
}
