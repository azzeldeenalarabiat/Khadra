using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.AdminUsers;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// Who may administer the platform.
///
/// There is one administrator role: <c>UserRole</c> has a single Admin member and no finer
/// permission model exists, so the endpoint takes no role to assign. Deactivation is a suspension,
/// not a delete — the unique index on email spans soft-deleted rows, so deleting an administrator
/// would burn their address permanently.
///
/// The handler refuses to deactivate the caller, or the last account that could undo it.
/// </summary>
[Authorize(Policy = SecurityPolicies.Admin)]
[Route("api/v1/admin/admin-users")]
public sealed class AdminUsersController : ApiControllerBase
{
    public sealed record InviteRequest(
        [Required, EmailAddress, MaxLength(320)] string Email,
        [Required, MaxLength(32)] string Phone,
        [Required, MaxLength(200)] string FullName);

    /// <param name="Reason">Bounded at 500 to match the column that stores it.</param>
    public sealed record DeactivateRequest([Required, MaxLength(500)] string Reason);

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AdminUserListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> List(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ListAdminUsersQuery(), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// Creates the account and emails a one-time link. Nothing about it works until they accept:
    /// the password is unusable bytes and the address is unverified until the link proves it.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<InviteAdminResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Invite([FromBody] InviteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new InviteAdminCommand(request.Email, request.Phone, request.FullName),
            cancellationToken);
        return FromResult(result, invited => Created($"/api/v1/admin/admin-users/{invited.UserId}", invited));
    }

    /// <summary>Ends their access immediately. Refused for your own account and for the last one.</summary>
    [HttpPost("{userId:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Deactivate(
        Guid userId,
        [FromBody] DeactivateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new DeactivateAdminCommand(Id.From(userId), request.Reason),
            cancellationToken);
        return FromResult(result);
    }

    [HttpPost("{userId:guid}/reactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Reactivate(Guid userId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ReactivateAdminCommand(Id.From(userId)), cancellationToken);
        return FromResult(result);
    }
}
