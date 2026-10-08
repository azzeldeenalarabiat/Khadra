using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.Reviews;
using Khadra.Application.Reviews.ReadModels;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// Review moderation (spec 3.2; pre-launch item 81): both directions — customers rating galleries and galleries rating
/// customers — listed for an administrator, who may hide one under a policy reason and restore it.
/// </summary>
/// <remarks>
/// Admin-only at the class level. Hiding never edits a rating: a customer's review of a gallery loses its text and keeps
/// its score; a gallery's rating of a customer stops counting (<c>ReviewDirection.HiddenScoreStillCounts</c>).
/// </remarks>
[Authorize(Policy = SecurityPolicies.Admin)]
[Route("api/v1/admin/reviews")]
public sealed class AdminReviewsController : ApiControllerBase
{
    /// <param name="Reason">A policy reason: PersonalContactDetails, AbusiveLanguage, NotAboutThisRental or SpamOrPromotion.</param>
    public sealed record HideRequest([Required, MaxLength(40)] string Reason);

    /// <param name="visibility">"hidden" or "visible"; omitted for both.</param>
    /// <param name="direction">CustomerRatesDealer or DealerRatesCustomer; omitted for both.</param>
    /// <param name="search">The comment, or a booking's exact reference.</param>
    [HttpGet]
    [ProducesResponseType<PagedResult<ModerationReview>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> List(
        [FromQuery] string? visibility,
        [FromQuery] string? direction,
        [FromQuery] int? rating,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new ListReviewsForModerationQuery(visibility, direction, rating, search, PageRequest.From(page, pageSize)),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>Hides a review under a policy reason. Audited; refused with 409 if it is already hidden.</summary>
    [HttpPost("{reviewId:guid}/hide")]
    [ProducesResponseType<ModerationReview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Hide(Guid reviewId, HideRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(new HideReviewCommand(Id.From(reviewId), request.Reason), cancellationToken);
        return FromResult(result);
    }

    /// <summary>Puts a hidden review back. Audited; refused with 409 if it is not hidden.</summary>
    [HttpPost("{reviewId:guid}/restore")]
    [ProducesResponseType<ModerationReview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Restore(Guid reviewId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new RestoreReviewCommand(Id.From(reviewId)), cancellationToken);
        return FromResult(result);
    }
}
