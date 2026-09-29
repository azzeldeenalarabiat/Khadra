using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.FinancialDocuments.Queries;
using Khadra.Application.FinancialDocuments.VoidFinancialDocument;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The administrator's issued financial documents (payments Phase 5): every document, one document's page
/// with the provider, the proof of what was issued and any void, the holds on documents owed and not
/// issued, a booking's documents — and the one action, voiding a wrong document and issuing its correction.
/// Every answer is kept out of caches, refusals included (owner, 2026-09-28): a document names a customer and
/// their money, a void carries the administrator's reason, and a hold its free-text error.
/// </summary>
[Authorize(Policy = SecurityPolicies.Admin)]
[Route("api/v1/admin")]
public sealed class AdminFinancialDocumentsController(ICurrentActor actor) : ApiControllerBase
{
    /// <summary>Every document, newest issued first, filtered by type, status, number, booking reference and Amman issue days.</summary>
    [HttpGet("financial-documents")]
    [ProducesResponseType<PagedResult<AdminFinancialDocumentListItem>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> List(
        [FromQuery] string? type,
        [FromQuery] string? status,
        [FromQuery] string? number,
        [FromQuery] string? reference,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(
            new ListAdminFinancialDocumentsQuery(type, status, number, reference, from, to, page, pageSize),
            cancellationToken));
    }

    /// <summary>The words the documents screens filter on, from the domain's own enumerations.</summary>
    [HttpGet("financial-documents/vocabulary")]
    [ProducesResponseType<FinancialDocumentVocabularyDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Vocabulary(CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(new GetFinancialDocumentVocabularyQuery(), cancellationToken));
    }

    /// <summary>Documents owed and not issued, with their reasons, oldest failure first.</summary>
    [HttpGet("financial-documents/holds")]
    [ProducesResponseType<PagedResult<FinancialDocumentHoldDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Holds([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(new ListFinancialDocumentHoldsQuery(page, pageSize), cancellationToken));
    }

    /// <summary>One document: the customer's page, plus its provider, its hash, what it covered and its void.</summary>
    [HttpGet("financial-documents/{documentId:guid}")]
    [ProducesResponseType<AdminFinancialDocumentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Document(Guid documentId, CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(new GetAdminFinancialDocumentQuery(Id.From(documentId)), cancellationToken));
    }

    /// <summary>
    /// A link, good for a few minutes, to the document's PDF in <c>en</c> or <c>ar</c> (payments Phase 6), drawn with
    /// the newest template: the document as issued — a voided one's included, unstamped — or, with
    /// <c>kind=Voided</c>, the voided copy its customer is given. 409 <c>financial_documents.pdf_not_ready</c> while it
    /// is being drawn, and <c>financial_documents.not_voided</c> for the voided copy of a document that is not voided.
    /// </summary>
    [HttpGet("financial-documents/{documentId:guid}/pdf-link")]
    [ProducesResponseType<SignedDocumentLink>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> PdfLink(
        Guid documentId,
        [FromQuery] string? language,
        [FromQuery] string? kind,
        CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(new GetAdminFinancialDocumentPdfLinkQuery(Id.From(documentId), language, kind), cancellationToken));
    }

    /// <summary>
    /// Voids a CURRENT document and issues its correction under a new number, in one audited transaction.
    /// 201 with the correction's address; 409 when the document is no longer current, is already voided, or
    /// its correction cannot be issued (the records need review, no issuer); 422 when the correction could
    /// not be composed — a defect, logged. Nothing is voided in any of those.
    /// </summary>
    [HttpPost("financial-documents/{documentId:guid}/void")]
    [ProducesResponseType<VoidedFinancialDocumentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult> Void(
        Guid documentId,
        [FromBody] VoidRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        KeepOutOfCaches();
        var result = await Mediator.Send(
            new VoidFinancialDocumentCommand(Id.From(documentId), actor.UserId!.Value, request.Reason),
            cancellationToken);
        return FromResult(result, voided => Created($"/api/v1/admin/financial-documents/{voided.ReplacementDocumentId}", voided));
    }

    /// <summary>A booking's documents, what is being prepared, and what is on hold — for its Money section.</summary>
    [HttpGet("bookings/{bookingId:guid}/financial-documents")]
    [ProducesResponseType<AdminBookingFinancialDocumentsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> ForBooking(Guid bookingId, CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        return FromResult(await Mediator.Send(new GetAdminBookingFinancialDocumentsQuery(Id.From(bookingId)), cancellationToken));
    }

    /// <summary>Why the document is being voided. The administrator's words; customers never see them.</summary>
    public sealed record VoidRequest([Required, MaxLength(FinancialDocumentVoid.MaxReasonLength)] string? Reason);
}
