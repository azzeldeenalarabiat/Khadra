using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.PushDevices;
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

    /// <summary>
    /// Registers (or re-registers) the phone making this request for push notifications, tied to
    /// this session. Called after sign-in, when the push token rotates, and when the language changes.
    /// </summary>
    /// <remarks>
    /// The token travels in the BODY, never the path: request paths are written to access logs, and
    /// a logged token lets whoever reads the log address that phone.
    /// </remarks>
    [HttpPut("current/push-device")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> RegisterPushDevice(RegisterPushDeviceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new RegisterMyPushDeviceCommand(request.Token, request.Platform, request.Language, request.AppVersion),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>Stops push notifications to the phone making this request. Called before sign-out. Idempotent.</summary>
    [HttpDelete("current/push-device")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> RemovePushDevice(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new RemoveMyPushDeviceCommand(), cancellationToken);
        return FromResult(result);
    }
}

public sealed record RegisterPushDeviceRequest(
    [param: Required, StringLength(1024)] string Token,
    [param: Required, StringLength(10)] string Platform,
    [param: Required, StringLength(2)] string Language,
    [param: StringLength(32)] string? AppVersion = null);
