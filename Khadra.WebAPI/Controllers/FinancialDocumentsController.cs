using Khadra.Application.Common;
using Khadra.Application.FinancialDocuments.Queries;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The customer's issued financial documents (payments Phase 5): the Invoices &amp; Receipts area and one
/// document's page. Payment Receipts, Refund Receipts and Booking Statements — none of them a tax invoice.
/// </summary>
/// <remarks>
/// Keyed on the ACTOR: there is no customer id in any path here. A document of anyone else's answers 404,
/// never a hint that it exists. New endpoints only; nothing an installed app reads has changed. Every answer
/// is kept out of caches: a document is the customer's record, with their name on it.
/// </remarks>
[Route("api/v1")]
[Authorize(Policy = SecurityPolicies.Customer)]
public sealed class FinancialDocumentsController(ICurrentActor actor) : ApiControllerBase
{
    /// <summary>Every document of the caller's, newest issued first, optionally of one type.</summary>
    [HttpGet("customers/me/financial-documents")]
    [ProducesResponseType<PagedResult<FinancialDocumentListItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Mine(
        [FromQuery] string? type,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(
            new ListMyFinancialDocumentsQuery(actor.UserId!.Value, type, page, pageSize),
            cancellationToken));
    }

    /// <summary>
    /// One document exactly as issued — its stored snapshot, in English and Arabic — with its standing and
    /// its links to other versions and related receipts.
    /// </summary>
    [HttpGet("financial-documents/{documentId:guid}")]
    [ProducesResponseType<FinancialDocumentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Document(Guid documentId, CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(
            new GetMyFinancialDocumentQuery(actor.UserId!.Value, Id.From(documentId)),
            cancellationToken));
    }
}
