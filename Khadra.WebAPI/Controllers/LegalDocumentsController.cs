using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.Legal;
using Khadra.Application.Legal.Dtos;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The legal texts in force: the Terms of Service and the Privacy notice (Wave 2 G1; pre-launch item 224).
/// </summary>
/// <remarks>
/// <para>
/// Anonymous, because a person reads the terms before they have an account, and rate limited like every anonymous
/// read. One document per call: the website renders one page at a time, and its HTTP transfer cache writes whatever
/// was fetched into the page it serves.
/// </para>
/// <para>
/// Cached for five minutes, publicly, and validated by an ETag that names the version and the renderer. A version
/// published a moment ago therefore reaches a browser within five minutes, and the website's renderer within its
/// own minute. A page with nothing published is not cached, so the first publish shows at once.
/// </para>
/// </remarks>
[Authorize]
[Route("api/v1/legal-documents")]
public sealed class LegalDocumentsController : ApiControllerBase
{
    [HttpGet("{kind}/current")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Public)]
    [ProducesResponseType<PublicLegalDocumentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Current(string kind, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new GetCurrentLegalDocumentQuery(kind, Request.Headers.IfNoneMatch.ToString()),
            cancellationToken);
        if (result.IsFailure)
        {
            Response.Headers.CacheControl = "no-cache";
            return Failure(result.Error);
        }

        Response.Headers.CacheControl = "public, max-age=300";
        Response.Headers.ETag = result.Value.ETag;
        return result.Value.Document is { } document ? Ok(document) : StatusCode(StatusCodes.Status304NotModified);
    }
}

/// <summary>
/// Publishing the legal texts (Wave 2 G1). Every version is permanent: there is no edit and no delete, only a newer
/// version. Each publish is written to the audit trail in the same transaction.
/// </summary>
[Authorize(Policy = SecurityPolicies.Admin)]
[Route("api/v1/admin/legal-documents")]
public sealed class AdminLegalDocumentsController : ApiControllerBase
{
    /// <summary>Room for two 200,000-character texts in UTF-8, escaped as JSON, with CRLF line ends.</summary>
    private const long TextsRequestLimit = 4 * 1024 * 1024;

    /// <param name="Kind"><c>Terms</c> or <c>Privacy</c>.</param>
    public sealed record LegalTextsRequest(
        [Required, MaxLength(40)] string Kind,
        [MaxLength(80)] string? VersionLabel,
        string? BodyEn,
        string? BodyAr);

    /// <summary>Every published version, newest first.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<LegalDocumentVersionSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> List(
        [FromQuery] string? kind,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ListLegalDocumentVersionsQuery(kind, page, pageSize), cancellationToken);
        return FromResult(result);
    }

    [HttpGet("{versionId:guid}")]
    [ProducesResponseType<LegalDocumentVersionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Version(Guid versionId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetLegalDocumentVersionQuery(Id.From(versionId)), cancellationToken);
        return FromResult(result);
    }

    /// <summary>What publishing would publish, rendered as the public page will show it. Writes nothing.</summary>
    [HttpPost("preview")]
    [RequestSizeLimit(TextsRequestLimit)]
    [ProducesResponseType<LegalDocumentPreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Preview([FromBody] LegalTextsRequest request, CancellationToken cancellationToken)
    {
        KeepOutOfCaches();
        var result = await Mediator.Send(
            new PreviewLegalDocumentQuery(request.Kind, request.VersionLabel, request.BodyEn, request.BodyAr),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>Publishes a version, in force at once and for good, until a newer one replaces it.</summary>
    [HttpPost]
    [RequestSizeLimit(TextsRequestLimit)]
    [ProducesResponseType<LegalDocumentVersionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Publish([FromBody] LegalTextsRequest request, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new PublishLegalDocumentCommand(request.Kind, request.VersionLabel, request.BodyEn, request.BodyAr),
            cancellationToken);
        return FromResult(result, published => Created($"/api/v1/admin/legal-documents/{published.Summary.VersionId}", published));
    }
}
