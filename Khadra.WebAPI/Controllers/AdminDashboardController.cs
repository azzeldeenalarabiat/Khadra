using Khadra.Application.AdminDashboard.Dtos;
using Khadra.Application.AdminDashboard.GetAdminDashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

// The Admin policy is applied at the CLASS level, not per action.
//
// Program.cs sets a fallback policy that only requires an authenticated user, so an action here
// without an explicit policy would be readable by any dealer employee holding a valid bearer token.
// Platform-wide counts, the dispute queue and the audit feed are none of their business. Putting it
// on the class means a future action cannot forget it.
[Authorize(Policy = SecurityPolicies.Admin)]
[Route("api/v1/admin")]
public sealed class AdminDashboardController : ApiControllerBase
{
    /// <summary>
    /// The whole admin landing screen in one snapshot.
    ///
    /// One endpoint rather than five: it is one screen with one refresh, and five parallel requests to
    /// paint one view is worse for both the browser and the database. The list screens each KPI links
    /// to are paged queries with a different read model, so nothing is duplicated by composing here.
    /// </summary>
    [HttpGet("dashboard")]
    [ProducesResponseType<AdminDashboardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> Dashboard(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetAdminDashboardQuery(), cancellationToken);
        return FromResult(result);
    }
}
