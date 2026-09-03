using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.Dealers.ManageEmployees;
using Khadra.Application.Dealers.ReadModels;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The owner's staff (spec 4.2).
///
/// Owner-only and gated on the business being able to trade: an employee cannot manage employees,
/// and a dealer that is not approved has nobody to delegate to. The handler re-checks ownership;
/// the policy is the pipeline seam, not the rule.
/// </summary>
[Authorize(Policy = SecurityPolicies.ApprovedDealer)]
[Route("api/v1/dealers/me/employees")]
public sealed class DealerEmployeesController(ICurrentActor actor) : ApiControllerBase
{
    public sealed record InviteRequest(
        [Required, MaxLength(150)] string FullName,
        [Required, MaxLength(256)] string Email,
        // Required because a User has a phone. Spec 4.2 lists name and email only; this is a recorded
        // deviation from the design's two-field form rather than a nullable phone across the platform.
        [Required, MaxLength(20)] string Phone,
        bool CanViewReports);

    public sealed record ReportAccessRequest(bool CanViewReports);

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<EmployeeListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> List(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ListMyEmployeesQuery(actor.UserId!.Value), cancellationToken);
        return FromResult(result);
    }

    /// <summary>Creates the account and emails an invitation; the person sets their own password.</summary>
    [HttpPost]
    [ProducesResponseType<EmployeeListItem>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Invite([FromBody] InviteRequest request, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new InviteEmployeeCommand(actor.UserId!.Value, request.FullName, request.Email, request.Phone, request.CanViewReports),
            cancellationToken);
        return FromResult(result, employee => Created($"/api/v1/dealers/me/employees/{employee.EmployeeId}", employee));
    }

    [HttpPost("{employeeId:guid}/resend-invitation")]
    [ProducesResponseType<EmployeeListItem>(StatusCodes.Status200OK)]
    public async Task<ActionResult> ResendInvitation(Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new ResendEmployeeInvitationCommand(actor.UserId!.Value, Id.From(employeeId)), cancellationToken);
        return FromResult(result);
    }

    /// <summary>Spec 4.2: financial reports are off by default and only the owner grants them.</summary>
    [HttpPut("{employeeId:guid}/report-access")]
    [ProducesResponseType<EmployeeListItem>(StatusCodes.Status200OK)]
    public async Task<ActionResult> SetReportAccess(Guid employeeId, [FromBody] ReportAccessRequest request, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new SetEmployeeReportAccessCommand(actor.UserId!.Value, Id.From(employeeId), request.CanViewReports),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>Ends their access immediately; their past actions keep their name.</summary>
    [HttpPost("{employeeId:guid}/deactivate")]
    [ProducesResponseType<EmployeeListItem>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Deactivate(Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new DeactivateEmployeeCommand(actor.UserId!.Value, Id.From(employeeId)), cancellationToken);
        return FromResult(result);
    }

    [HttpPost("{employeeId:guid}/reactivate")]
    [ProducesResponseType<EmployeeListItem>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Reactivate(Guid employeeId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new ReactivateEmployeeCommand(actor.UserId!.Value, Id.From(employeeId)), cancellationToken);
        return FromResult(result);
    }
}
