using Khadra.Application.Common;
using Khadra.Application.Payments.AdminPayments;
using Khadra.Application.Payments.ReadModels;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The administrator's view of money across the platform (payments Phase 4b): every checkout attempt,
/// one payment's page with what the provider said about it, and the refunds queue. Read-only: nothing
/// here moves money, retries a card or sends a refund — the payment sweep does that, and a refused
/// refund is re-sent by it without anybody pressing a button.
/// </summary>
[Authorize(Policy = SecurityPolicies.Admin)]
[Route("api/v1/admin")]
public sealed class AdminPaymentsController(ICurrentActor actor) : ApiControllerBase
{
    /// <summary>Every attempt, newest first, filtered by status, purpose, office, customer, booking reference and Amman days.</summary>
    [HttpGet("payments")]
    [ProducesResponseType<PagedResult<AdminPaymentListItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Payments(
        [FromQuery] string? status,
        [FromQuery] string? purpose,
        [FromQuery] Guid? dealerId,
        [FromQuery] Guid? customerId,
        [FromQuery] string? reference,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        FromResult(await Mediator.Send(
            new ListAdminPaymentsQuery(status, purpose, dealerId, customerId, reference, from, to, page, pageSize),
            cancellationToken));

    /// <summary>The words the payments list and the refunds queue filter on, from the domain's own enumerations.</summary>
    [HttpGet("payments/vocabulary")]
    [ProducesResponseType<PaymentVocabularyDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Vocabulary(CancellationToken cancellationToken) =>
        FromResult(await Mediator.Send(new GetPaymentVocabularyQuery(), cancellationToken));

    /// <summary>One payment: the attempt and its refunds, its booking, and every event the provider sent about it.</summary>
    [HttpGet("payments/{paymentId:guid}")]
    [ProducesResponseType<AdminPaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Payment(Guid paymentId, CancellationToken cancellationToken) =>
        FromResult(await Mediator.Send(new GetAdminPaymentQuery(Id.From(paymentId)), cancellationToken));

    /// <summary>
    /// Closes a capture incident with the administrator's account of how its money was dealt with at the provider,
    /// audited (Wave 4, B1). Moves no money. 404 <c>payments.incident_not_found</c>; 409
    /// <c>payments.incident_already_handled</c>.
    /// </summary>
    [HttpPost("payments/{paymentId:guid}/incidents/{incidentId:guid}/handled")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> MarkIncidentHandled(
        Guid paymentId,
        Guid incidentId,
        [FromBody] MarkIncidentHandledRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return FromResult(await Mediator.Send(
            new MarkPaymentIncidentHandledCommand(Id.From(paymentId), Id.From(incidentId), actor.UserId!.Value, request.Note),
            cancellationToken));
    }

    public sealed record MarkIncidentHandledRequest(string? Note);

    /// <summary>
    /// The refunds queue. With no status, the live queue: refused first, then recorded, then sent, each
    /// owed longest first; <c>status=Settled</c> reads what already went back, newest first.
    /// </summary>
    [HttpGet("refunds")]
    [ProducesResponseType<PagedResult<AdminRefundListItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Refunds(
        [FromQuery] string? status,
        [FromQuery] string? reason,
        [FromQuery] string? reference,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken) =>
        FromResult(await Mediator.Send(
            new ListAdminRefundsQuery(status, reason, reference, from, to, page, pageSize),
            cancellationToken));
}
