using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.MySecurity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The signed-in person's own account security: where they are signed in, and ending one of those.
///
/// Deliberately NOT admin-only and deliberately not addressable by user id. Reading another person's
/// devices, addresses and sign-in times is surveillance rather than administration, and nothing in
/// the spec asks an administrator to do it. Every route here answers for whoever is calling.
/// </summary>
[Authorize]
[Route("api/v1/auth/sessions")]
public sealed class MySecurityController : ApiControllerBase
{
    /// <summary>Every sign-in, one row per device rather than one per token refresh.</summary>
    [HttpGet]
    [ProducesResponseType<MySessionsView>(StatusCodes.Status200OK)]
    public async Task<ActionResult> List(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetMySessionsQuery(), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// Ends one session. Answers 404 for a family that is not yours, so a session cannot be ended by
    /// guessing an id.
    /// </summary>
    [HttpPost("{familyId:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Revoke(Guid familyId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new RevokeMySessionCommand(familyId), cancellationToken);
        return FromResult(result);
    }
}
