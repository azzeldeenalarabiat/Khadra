using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.Payables.Dtos;
using Khadra.Application.Payables.Holds;
using Khadra.Application.Payables.Queries;
using Khadra.Application.Payables.Settlements;
using Khadra.Domain.Common;
using Khadra.Domain.Payables;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The office payables ledger, as the administrator works it (payments Phase 8): every office's balance, the payables
/// behind it, the holds, the settlements recorded by hand, and Khadra's own finance figures. There is no payout rail:
/// recording a settlement records money an administrator moved outside the platform. Every answer is kept out of
/// caches, refusals included — each names offices and their money.
/// </summary>
[Authorize(Policy = SecurityPolicies.Admin)]
[Route("api/v1/admin")]
public sealed class AdminPayablesController(ICurrentActor actor) : ApiControllerBase
{
    private const int DefaultPageSize = 25;

    /// <summary>Every office's balance per currency and kind of money — due now, and recorded but not due yet — or one office's.</summary>
    [HttpGet("office-balances")]
    [ProducesResponseType<IReadOnlyList<OfficeBalanceDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Balances([FromQuery] Guid? dealerId, CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return Ok(await Mediator.Send(new GetOfficeBalancesQuery(dealerId is { } id ? Id.From(id) : null), cancellationToken));
    }

    /// <summary>
    /// Recorded payables, newest outcome first: <c>scope</c> <c>Open</c> (the default), <c>Settled</c> or <c>All</c>, for
    /// one office or every office, over the Amman days their outcomes became final.
    /// </summary>
    [HttpGet("office-payables")]
    [ProducesResponseType<PagedResult<OfficePayableDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Payables(
        [FromQuery] Guid? dealerId,
        [FromQuery] string? scope,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return Ok(await Mediator.Send(
            new ListOfficePayablesQuery(
                dealerId is { } id ? Id.From(id) : null,
                scope ?? string.Empty,
                from,
                to,
                page ?? 1,
                pageSize ?? DefaultPageSize),
            cancellationToken));
    }

    /// <summary>The bookings a hold stops being recorded — their records need a person — oldest first.</summary>
    [HttpGet("office-payables/holds")]
    [ProducesResponseType<IReadOnlyList<PayableHoldDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> BookingHolds([FromQuery] Guid? dealerId, CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return Ok(await Mediator.Send(new ListPayableBookingHoldsQuery(dealerId is { } id ? Id.From(id) : null), cancellationToken));
    }

    /// <summary>
    /// Leaves one payable out of settlements, with the administrator's reason, audited. 409
    /// <c>payables.already_held</c>, or <c>payables.already_settled</c> for one a settlement closed.
    /// </summary>
    [HttpPost("office-payables/{payableId:guid}/hold")]
    [ProducesResponseType<OfficePayableDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Hold(Guid payableId, [FromBody] HoldRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(
            new HoldOfficePayableCommand(Id.From(payableId), actor.UserId!.Value, request.Reason),
            cancellationToken));
    }

    /// <summary>Lets a payable an administrator held back into the next settlement, audited. 409 <c>payables.not_held</c>.</summary>
    [HttpPost("office-payables/{payableId:guid}/release")]
    [ProducesResponseType<OfficePayableDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Release(Guid payableId, [FromBody] ReleaseRequest? request, CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(
            new ReleaseOfficePayableCommand(Id.From(payableId), actor.UserId!.Value, request?.Note),
            cancellationToken));
    }

    /// <summary>One office's settlements, newest first, voided ones included and marked.</summary>
    [HttpGet("offices/{dealerId:guid}/settlements")]
    [ProducesResponseType<PagedResult<OfficeSettlementDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Settlements(Guid dealerId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(
            new ListOfficeSettlementsQuery(Id.From(dealerId), page ?? 1, pageSize ?? DefaultPageSize),
            cancellationToken));
    }

    /// <summary>
    /// Records that everything due to or from the office in one currency and kind of money was settled by hand on
    /// <c>paidOn</c>, netted, audited, under a new number: 201 with the settlement. 409 <c>payables.balance_changed</c>
    /// (with <c>currentAmount</c>) when the balance due is no longer <c>expectedAmount</c>, <c>payables.nothing_due</c>,
    /// <c>payables.records_changed</c> when a booking no longer matches its payable, and
    /// <c>payables.changed_concurrently</c> when another administrator settled at the same moment. Nothing is
    /// recorded in any of those.
    /// </summary>
    [HttpPost("offices/{dealerId:guid}/settlements")]
    [ProducesResponseType<OfficeSettlementDetailDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> RecordSettlement(Guid dealerId, [FromBody] RecordSettlementRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        KeepOutOfCaches();
        var result = await Mediator.Send(
            new RecordOfficeSettlementCommand(
                Id.From(dealerId),
                request.Currency ?? string.Empty,
                request.Provider ?? string.Empty,
                request.ExpectedAmount ?? 0m,
                request.PaidOn ?? default,
                request.Reference,
                request.Note,
                actor.UserId!.Value),
            cancellationToken);
        return FromResult(result, recorded => Created($"/api/v1/admin/office-settlements/{recorded.Settlement.SettlementId}", recorded));
    }

    /// <summary>One settlement with the payables it closed, each at the net it closed it at.</summary>
    [HttpGet("office-settlements/{settlementId:guid}")]
    [ProducesResponseType<OfficeSettlementDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Settlement(Guid settlementId, CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(new GetOfficeSettlementQuery(Id.From(settlementId)), cancellationToken));
    }

    /// <summary>
    /// Voids a settlement recorded wrongly, with the administrator's reason, audited: its payables are due again. 409
    /// <c>payables.settlement_already_voided</c>.
    /// </summary>
    [HttpPost("office-settlements/{settlementId:guid}/void")]
    [ProducesResponseType<OfficeSettlementDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> VoidSettlement(Guid settlementId, [FromBody] VoidSettlementRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(
            new VoidOfficeSettlementCommand(Id.From(settlementId), actor.UserId!.Value, request.Reason),
            cancellationToken));
    }

    /// <summary>
    /// Khadra's own money over a span of Amman days (the current month by default): commission earned, what disputes
    /// left with the platform, what moved to and from offices, and what is owed right now.
    /// </summary>
    [HttpGet("finance/summary")]
    [ProducesResponseType<FinanceSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Finance([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(new GetFinanceSummaryQuery(from, to), cancellationToken));
    }

    /// <summary>Why the payable is being held. The administrator's words; the office never sees them.</summary>
    public sealed record HoldRequest([Required, MaxLength(OfficePayableHold.MaxDetailLength)] string? Reason);

    public sealed record ReleaseRequest([MaxLength(OfficePayableHold.MaxDetailLength)] string? Note);

    /// <param name="Provider">The kind of money, exactly as the office's balance named it.</param>
    /// <param name="ExpectedAmount">The balance due the administrator saw and confirms, signed from Khadra's side.</param>
    /// <param name="PaidOn">The Amman day the money moved.</param>
    public sealed record RecordSettlementRequest(
        [Required, StringLength(3, MinimumLength = 3)] string? Currency,
        [Required, MaxLength(OfficePayable.ProviderMaxLength)] string? Provider,
        [Required] decimal? ExpectedAmount,
        [Required] DateOnly? PaidOn,
        [MaxLength(OfficeSettlement.ReferenceMaxLength)] string? Reference,
        [MaxLength(OfficeSettlement.NoteMaxLength)] string? Note);

    /// <summary>Why the settlement is being voided. The administrator's words; the office never sees them.</summary>
    public sealed record VoidSettlementRequest([Required, MaxLength(OfficeSettlementVoid.MaxReasonLength)] string? Reason);
}
