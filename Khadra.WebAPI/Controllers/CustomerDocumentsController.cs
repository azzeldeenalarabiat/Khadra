using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.Documents;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// A customer's own identity paperwork (spec 5.1).
///
/// Every route is scoped to the ACTOR, never to an id in the path, so there is no route through which
/// one customer can read or replace another's documents. Nothing here ever returns a URL or a storage
/// key: a client that wants to display a file asks for a short-lived signed link (spec 7).
/// </summary>
[Authorize(Policy = SecurityPolicies.Customer)]
[Route("api/v1/customers/me/documents")]
public sealed class CustomerDocumentsController(ICurrentActor actor) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<CustomerDocumentsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> List(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ListCustomerDocumentsQuery(actor.UserId!.Value), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// Uploads one document. Re-sending a type replaces the previous file, which is what spec 5.1
    /// expects of a customer told their photo was unreadable.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(16 * 1024 * 1024)]
    [ProducesResponseType<CustomerDocumentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Upload(
        [FromForm] string type,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return Failure(Domain.IdentityAccess.IdentityErrors.InvalidDocumentContent);

        await using var content = file.OpenReadStream();
        var result = await Mediator.Send(
            new UploadCustomerDocumentCommand(
                actor.UserId!.Value,
                type,
                file.FileName,
                file.ContentType,
                file.Length,
                content),
            cancellationToken);

        return FromResult(result, created => Created($"/api/v1/customers/me/documents/{created.DocumentId}", created));
    }

    /// <summary>Mints a short-lived link to one of the caller's own documents (spec 7).</summary>
    [HttpGet("{documentId:guid}/link")]
    [ProducesResponseType<SignedDocumentLink>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Link(Guid documentId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new CreateCustomerDocumentLinkQuery(actor.UserId!.Value, Id.From(documentId)),
            cancellationToken);
        return FromResult(result);
    }
}
