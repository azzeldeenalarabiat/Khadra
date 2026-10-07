using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.AdminCustomers;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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

    /// <param name="Reason">Why the file cannot be accepted, as the customer will read it: at most 500 characters.</param>
    /// <param name="UploadedAt">The document's <c>uploadedAt</c> exactly as the profile sent it: which upload was judged.</param>
    public sealed record RejectDocumentRequest([Required, MaxLength(600)] string Reason, [Required] DateTimeOffset? UploadedAt);

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
    /// Documents are DESCRIBED, not linked: no signed URL for a passport is ever minted here. An
    /// administrator opens one through the streaming route below (Wave 4, W4-9), which records the view
    /// first.
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

    /// <summary>
    /// Opens one of the customer's documents (Wave 4, W4-9; checklist 27). Streamed through the API, never a
    /// signed link, with the view recorded before a byte is sent; no-store and nosniff, as every private file.
    /// </summary>
    [HttpGet("{userId:guid}/documents/{documentId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.PrivateDocuments)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> OpenDocument(Guid userId, Guid documentId, CancellationToken cancellationToken)
    {
        // Before the handler, so a refusal carries it too, as the office's listing does (the advisor's review).
        KeepOutOfCaches();
        var result = await Mediator.Send(new OpenCustomerDocumentCommand(Id.From(userId), Id.From(documentId)), cancellationToken);
        return FromResult(result, opened => PrivateDocument(opened.Content, opened.ContentType));
    }

    /// <summary>
    /// Rejects one of the customer's documents, with the reason the customer will read (Wave 4, W4-9). The customer
    /// is told and must upload a new file before their next request. 409 <c>documents.changed_since_viewed</c> when the
    /// file was replaced after it was opened.
    /// </summary>
    [HttpPost("{userId:guid}/documents/{documentId:guid}/reject")]
    [ProducesResponseType<CustomerProfile>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> RejectDocument(
        Guid userId,
        Guid documentId,
        [FromBody] RejectDocumentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new RejectCustomerDocumentCommand(Id.From(userId), Id.From(documentId), request.Reason, request.UploadedAt!.Value),
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
