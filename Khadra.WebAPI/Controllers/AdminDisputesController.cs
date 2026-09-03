using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.Disputes.Dtos;
using Khadra.Application.Disputes.ReadModels;
using Khadra.Application.Disputes.ResolveDispute;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The Admin's dispute queue and workspace (spec 3.3), and the one place on the platform where a
/// decision about money is made.
///
/// Admin-only at the class level for the same reason AdminDealersController is. Every resolution is
/// written to the append-only audit trail in the same transaction as the ticket and the booking it
/// closes. Until Payments ships, a resolution is a recorded decision and nothing more: the console
/// labels each one "Decision recorded — no funds moved".
/// </summary>
[Authorize(Policy = SecurityPolicies.Admin)]
[Route("api/v1/admin/disputes")]
public sealed class AdminDisputesController : ApiControllerBase
{
    public sealed record ResolveRequest(
        [Range(0, double.MaxValue)] decimal RefundToCustomer,
        [Range(0, double.MaxValue)] decimal RetainedByPlatform,
        [Range(0, double.MaxValue)] decimal TransferredToDealer,
        [Range(0, double.MaxValue)] decimal? DealerCharge,
        [Required, MaxLength(2000)] string Note);

    /// <summary>The queue. Live tickets by default, soonest deadline first.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<DisputeListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> List(
        [FromQuery] string? status,
        [FromQuery] bool overdueOnly,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ListDisputesQuery(status, overdueOnly, page, pageSize), cancellationToken);
        return FromResult(result);
    }

    /// <summary>The workspace: the ticket, both parties' statements with fresh evidence links, and the whole booking.</summary>
    [HttpGet("{ticketId:guid}")]
    [ProducesResponseType<DisputeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Review(Guid ticketId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetDisputeForReviewQuery(Id.From(ticketId)), cancellationToken);
        return FromResult(result);
    }

    /// <summary>Takes the ticket on. Recorded, but not a gate: any admin may still resolve it.</summary>
    [HttpPost("{ticketId:guid}/assign")]
    [ProducesResponseType<DisputeDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Assign(Guid ticketId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new AssignDisputeCommand(Id.From(ticketId)), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// The decision (spec 3.3: apply, waive, partial, refund -- all of them a split of the held
    /// deposit). The three legs must add up to exactly what the booking holds; a dealer charge must
    /// fall inside the range the booking assessed.
    /// </summary>
    [HttpPost("{ticketId:guid}/resolve")]
    [ProducesResponseType<DisputeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Resolve(
        Guid ticketId,
        [FromBody] ResolveRequest request,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new ResolveDisputeCommand(
                Id.From(ticketId),
                request.RefundToCustomer,
                request.RetainedByPlatform,
                request.TransferredToDealer,
                request.DealerCharge,
                request.Note),
            cancellationToken);
        return FromResult(result);
    }
}
