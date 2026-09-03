using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.Disputes.Dtos;
using Khadra.Application.Disputes.RaiseDispute;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The parties' side of a dispute (spec 3.3): open one from a booking, add to it, withdraw it.
///
/// Any signed-in user may call these; the handlers decide whether the caller is a party to the
/// booking in question and answer 404 to anyone who is not. Nothing here moves money.
/// </summary>
[ApiController]
[Route("api/v1/disputes")]
[Authorize]
public sealed class DisputesController(ICurrentActor actor) : ApiControllerBase
{
    public sealed record EvidenceUploadRequest(
        [Required] Guid BookingId,
        [Required, MaxLength(255)] string FileName,
        [Required, MaxLength(100)] string ContentType);

    public sealed record OpenDisputeRequest(
        [Required] Guid BookingId,
        [Required, MaxLength(2000)] string Reason,
        IReadOnlyList<string>? EvidenceKeys);

    public sealed record StatementRequest(
        [Required, MaxLength(4000)] string Body,
        IReadOnlyList<string>? EvidenceKeys);

    /// <summary>Step one of attaching evidence: where to PUT the file. Step two is PUT /api/v1/uploads/{token}.</summary>
    [HttpPost("evidence/upload-url")]
    [ProducesResponseType<EvidenceUploadDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> RequestEvidenceUpload(
        [FromBody] EvidenceUploadRequest request,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new RequestDisputeEvidenceUploadCommand(
                actor.UserId!.Value, Id.From(request.BookingId), request.FileName, request.ContentType),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>Opens a ticket on a booking the caller is a party to.</summary>
    [HttpPost]
    [ProducesResponseType<DisputeDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Open([FromBody] OpenDisputeRequest request, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new OpenDisputeCommand(actor.UserId!.Value, Id.From(request.BookingId), request.Reason, request.EvidenceKeys ?? []),
            cancellationToken);
        return FromResult(result, dispute => CreatedAtAction(nameof(Get), new { ticketId = dispute.TicketId }, dispute));
    }

    [HttpGet("{ticketId:guid}")]
    [ProducesResponseType<DisputeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Get(Guid ticketId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetMyDisputeQuery(actor.UserId!.Value, Id.From(ticketId)), cancellationToken);
        return FromResult(result);
    }

    /// <summary>Either party may add to a live ticket, including the one who did not open it.</summary>
    [HttpPost("{ticketId:guid}/statements")]
    [ProducesResponseType<DisputeDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> AddStatement(
        Guid ticketId,
        [FromBody] StatementRequest request,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new AddDisputeStatementCommand(actor.UserId!.Value, Id.From(ticketId), request.Body, request.EvidenceKeys ?? []),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>The amicable path: the opener takes the ticket back, and nothing is charged to anyone.</summary>
    [HttpPost("{ticketId:guid}/withdraw")]
    [ProducesResponseType<DisputeDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Withdraw(Guid ticketId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new WithdrawDisputeCommand(actor.UserId!.Value, Id.From(ticketId)), cancellationToken);
        return FromResult(result);
    }
}
