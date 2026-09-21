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
    /// <remarks>
    /// No <c>[EmailAddress]</c>, and that is deliberate. It was the only one on the platform — the
    /// employee invitation, registration and every other endpoint that takes an address leave the
    /// judgement to <c>EmailAddress.Create</c>, which is both STRICTER (it insists on a dot in the
    /// domain part, which the attribute does not) and forgiving of the one thing the attribute is
    /// not: it trims. The attribute refuses a value with a trailing space or newline before the
    /// domain sees it, answering a bare "not a valid e-mail address" with no <c>code</c> for a
    /// client to translate — and the console's invite dialog was a textarea, where Return types a
    /// newline. One definition of a valid address, in the value object, as with the phone and the
    /// name beside it.
    ///
    /// The lengths are kept as a cheap guard on the body, and they now say what the domain will
    /// actually accept: 256 is <c>EmailAddress.MaxLength</c> and 150 is <c>PersonName.MaxLength</c>,
    /// both of which the FluentValidation validator beside the command already uses. The record is
    /// the OpenAPI contract, and it was promising 320 and 200 — room the platform does not have.
    /// The phone stays at 32, which bounds the RAW form before <c>PhoneNumber.Create</c> strips the
    /// spaces and dashes out of it.
    /// </remarks>
    public sealed record InviteRequest(
        [Required, MaxLength(256)] string Email,
        [Required, MaxLength(32)] string Phone,
        [Required, MaxLength(150)] string FullName);

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

    /// <summary>
    /// Sends the invitation again, with a fresh link. Every earlier link for that account stops
    /// working.
    /// </summary>
    /// <remarks>
    /// The way out of the trap the first invitation can leave: the account is committed before the
    /// email is attempted, so a relay that refuses leaves a real administrator nobody can reach and
    /// an address that can never be invited again. Unlike <see cref="Invite"/>, a refused message
    /// FAILS this (503) — it exists to deliver one, and the reissued link stands either way.
    /// </remarks>
    [HttpPost("{userId:guid}/resend-invitation")]
    [ProducesResponseType<InviteAdminResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> ResendInvitation(Guid userId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new ResendAdminInvitationCommand(Id.From(userId)),
            cancellationToken);
        return FromResult(result);
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
