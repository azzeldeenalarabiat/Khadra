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

    /// <summary>
    /// Has this dealer's queue changed? Polled; deliberately cheap; carries no data.
    /// </summary>
    /// <remarks>
    /// The console asks this every thirty seconds and re-reads the real endpoints only when the
    /// token differs. Two indexed round trips, against the eight `GET /bookings/tab-counts` costs and
    /// the composite `dashboard` costs — which is what makes asking often affordable.
    ///
    /// It is an invalidation signal and not a source of truth: bookings, counts, notifications and
    /// the dashboard all keep coming from the endpoints they already came from. When the pulse
    /// service arrives it pushes the same token and the console takes the same path, so a push
    /// channel is a faster way to learn the same fact rather than a second way to learn a different
    /// one.
    /// </remarks>
    [HttpGet("pulse")]
    [ProducesResponseType<DealerPulseDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Pulse(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetDealerPulseQuery(actor.UserId!.Value), cancellationToken);
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
    public async Task<ActionResult> Activity(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery(Name = "actor")] string? actorFilter,
        CancellationToken cancellationToken)
    {
        // `?actor=me` and nothing else. Accepting a user id here would turn the dealership's trail
        // into a way to page a colleague's record one id at a time.
        var result = await Mediator.Send(
            new ListDealerActivityQuery(actor.UserId!.Value, page, pageSize, MineOnly: actorFilter == "me"),
            cancellationToken);
        return FromResult(result);
    }
}
