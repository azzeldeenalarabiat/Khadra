using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Dealers.Console;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The dealer's dashboard, reports and activity (design: Dealer Console). Any active member of staff
/// may read the dashboard and the activity log; reports answer 403 unless the owner granted them
/// (spec 4.2), which the handler decides.
/// </summary>
[Authorize(Policy = SecurityPolicies.DealerStaff)]
[Route("api/v1/dealers/me")]
public sealed class DealerConsoleController(ICurrentActor actor) : ApiControllerBase
{
    [HttpGet("dashboard")]
    [ProducesResponseType<DealerDashboardDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Dashboard(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetDealerDashboardQuery(actor.UserId!.Value), cancellationToken);
        return FromResult(result);
    }

    [HttpGet("reports")]
    [ProducesResponseType<DealerReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> Reports([FromQuery] string period = "monthly", CancellationToken cancellationToken = default)
    {
        var result = await Mediator.Send(new GetDealerReportQuery(actor.UserId!.Value, period.ToLowerInvariant()), cancellationToken);
        return FromResult(result);
    }

    [HttpGet("activity")]
    [ProducesResponseType<PagedResult<DealerActivityEntry>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Activity([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ListDealerActivityQuery(actor.UserId!.Value, page, pageSize), cancellationToken);
        return FromResult(result);
    }
}
