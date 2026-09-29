using Khadra.Application.Common;
using Khadra.Application.Payables.Dtos;
using Khadra.Application.Payables.Queries;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The office's own payouts (payments Phase 8): what Khadra owes it — or it owes Khadra — per currency, the payables
/// behind that, and the settlements recorded. For the owner, and an employee granted the financial reports; anyone
/// else on the staff is answered 403, as the reports answer them. Kept out of caches: it is the office's money.
/// </summary>
[Authorize(Policy = SecurityPolicies.DealerStaff)]
[Route("api/v1/dealers/me/payouts")]
public sealed class DealerPayoutsController(ICurrentActor actor) : ApiControllerBase
{
    private const int DefaultPageSize = 25;

    [HttpGet]
    [ProducesResponseType<OfficePayoutsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> Balances(CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(new GetMyPayoutsQuery(actor.UserId!.Value), cancellationToken));
    }

    /// <summary>The office's payables, newest outcome first: <c>scope</c> <c>Open</c> (the default), <c>Settled</c> or <c>All</c>.</summary>
    [HttpGet("payables")]
    [ProducesResponseType<PagedResult<OfficePayableDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> Payables(
        [FromQuery] string? scope,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(
            new ListMyPayablesQuery(actor.UserId!.Value, scope ?? string.Empty, page ?? 1, pageSize ?? DefaultPageSize),
            cancellationToken));
    }

    /// <summary>The office's settlements, newest first.</summary>
    [HttpGet("settlements")]
    [ProducesResponseType<PagedResult<OfficeSettlementDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> Settlements([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(
            new ListMySettlementsQuery(actor.UserId!.Value, page ?? 1, pageSize ?? DefaultPageSize),
            cancellationToken));
    }

    /// <summary>One of the office's settlements with the payables it closed; another office's is answered 404.</summary>
    [HttpGet("settlements/{settlementId:guid}")]
    [ProducesResponseType<OfficeSettlementDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Settlement(Guid settlementId, CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(new GetMySettlementQuery(actor.UserId!.Value, Id.From(settlementId)), cancellationToken));
    }
}
