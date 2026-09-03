using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.Dealers.Dtos;
using Khadra.Application.Dealers.GetDealerForReview;
using Khadra.Application.Dealers.ListDealers;
using Khadra.Application.Dealers.ReadModels;
using Khadra.Application.Dealers.ReviewDealer;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The Admin's dealer queue: spec 3.1's licence check and 3.2's ongoing suspend/reactivate.
///
/// Admin-only at the class level. Program.cs sets a fallback policy that requires only
/// authentication, so an action here without an explicit policy would let any signed-in user approve
/// dealers — which is the single most consequential thing this platform can do.
///
/// Every decision is recorded in the append-only audit trail inside the same transaction as the
/// change itself, so an approval that happened and an approval that was logged cannot diverge.
/// </summary>
[Authorize(Policy = SecurityPolicies.Admin)]
[Route("api/v1/admin/dealers")]
public sealed class AdminDealersController : ApiControllerBase
{
    /// <summary>
    /// The dealer queue, ordered by whichever application is closest to breaching its review promise.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<DealerListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> List(
        [FromQuery] string? status,
        [FromQuery] bool? suspendedOnly,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new ListDealersQuery(status, suspendedOnly, search, page, pageSize), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// Everything needed to make the decision, including a freshly signed link per document. Spec 7
    /// keeps dealer commercial documents private and Admin-only; the links expire.
    /// </summary>
    [HttpGet("{dealerId:guid}")]
    [ProducesResponseType<DealerReviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Review(Guid dealerId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetDealerForReviewQuery(Id.From(dealerId)), cancellationToken);
        return FromResult(result);
    }

    /// <summary>Outcome one of three (spec 3.1). Only an approved dealer can trade.</summary>
    [HttpPost("{dealerId:guid}/approve")]
    [ProducesResponseType<DealerProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Approve(Guid dealerId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ApproveDealerCommand(Id.From(dealerId)), cancellationToken);
        return FromResult(result);
    }

    /// <summary>Outcome two. The reason is mandatory and is recorded against the application.</summary>
    [HttpPost("{dealerId:guid}/reject")]
    [ProducesResponseType<DealerProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Reject(
        Guid dealerId,
        ReasonRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new RejectDealerCommand(Id.From(dealerId), request.Reason), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// Outcome three, and the reason spec 3.1 insists on three rather than two: send the application
    /// back with a specific note so the dealer fixes one thing instead of re-applying from scratch.
    /// </summary>
    [HttpPost("{dealerId:guid}/request-clarification")]
    [ProducesResponseType<DealerProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> RequestClarification(
        Guid dealerId,
        ReasonRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new RequestDealerClarificationCommand(Id.From(dealerId), request.Reason), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// Spec 3.2. Suspension is a policy sanction, not a re-run of the licence check: a suspended
    /// dealer stays Approved so reactivating does not send them back through review.
    /// </summary>
    [HttpPost("{dealerId:guid}/suspend")]
    [ProducesResponseType<DealerProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Suspend(
        Guid dealerId,
        ReasonRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new SuspendDealerCommand(Id.From(dealerId), request.Reason), cancellationToken);
        return FromResult(result);
    }

    [HttpPost("{dealerId:guid}/reactivate")]
    [ProducesResponseType<DealerProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Reactivate(Guid dealerId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ReactivateDealerCommand(Id.From(dealerId)), cancellationToken);
        return FromResult(result);
    }
}

/// <summary>A written reason. Rejection, clarification and suspension all require one.</summary>
public sealed record ReasonRequest([param: Required, StringLength(1000, MinimumLength = 1)] string Reason);
