using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.AdminCustomers;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The people who rent (spec 5), as the platform sees them.
///
/// Admin-only at the class level, for the same reason the other admin controllers are: the fallback
/// policy is authenticated-only, so a route added here later is restricted by default rather than by
/// somebody remembering.
///
/// Every route speaks about CUSTOMERS. A user who is not one answers 404, the same as an id that does
/// not exist: suspending a dealer owner through here would lock them out while their dealership went
/// on trading, and under an audit action that says "customer".
/// </summary>
[Authorize(Policy = SecurityPolicies.Admin)]
[Route("api/v1/admin/customers")]
public sealed class AdminCustomersController : ApiControllerBase
{
    /// <param name="Reason">Bounded at 500 to match the column that stores it.</param>
    public sealed record SuspendRequest([Required, MaxLength(500)] string Reason);

    [HttpGet]
    [ProducesResponseType<PagedResult<CustomerListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] bool? unverifiedOnly,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new ListCustomersQuery(status, search, unverifiedOnly, PageRequest.From(page, pageSize)),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>How many customers sit in each state, so the filters can carry live counts.</summary>
    [HttpGet("counts")]
    [ProducesResponseType<CustomerCountsView>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Counts(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetCustomerCountsQuery(), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// One customer: their account, what they have on file, and their history with the platform.
    /// </summary>
    /// <remarks>
    /// Documents are DESCRIBED, not linked. Spec 7 keeps identity papers private and
    /// <c>CustomerDocument</c> scopes viewing to the customer and to a dealer with an active request;
    /// an Admin is not named there, and spec 5.1's review mechanism is undecided. No signed URL for a
    /// passport is minted until the owner says so.
    /// </remarks>
    [HttpGet("{userId:guid}")]
    [ProducesResponseType<CustomerProfile>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Get(Guid userId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetCustomerQuery(Id.From(userId)), cancellationToken);
        return FromResult(result);
    }

    /// <summary>Stops the account. Its sessions end immediately through the rotated security stamp.</summary>
    [HttpPost("{userId:guid}/suspend")]
    [ProducesResponseType<CustomerProfile>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Suspend(Guid userId, [FromBody] SuspendRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new SuspendCustomerCommand(Id.From(userId), request.Reason),
            cancellationToken);
        return FromResult(result);
    }

    [HttpPost("{userId:guid}/reactivate")]
    [ProducesResponseType<CustomerProfile>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Reactivate(Guid userId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ReactivateCustomerCommand(Id.From(userId)), cancellationToken);
        return FromResult(result);
    }
}
