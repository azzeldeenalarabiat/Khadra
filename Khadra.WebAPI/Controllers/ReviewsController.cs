using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.Reviews;
using Khadra.Application.Reviews.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Reviews;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// A customer rating the gallery they rented from (spec 4.1, 5.6).
/// </summary>
/// <remarks>
/// The customer's review of a gallery is the public half. The dealer's review OF a customer, which
/// spec 5.6 also asks for, has no endpoint here: it is visible to other dealers to inform their
/// approve/reject decisions, and that is a dealer-console feature with its own audience and its own
/// privacy question. The domain supports both directions; only one is exposed, and adding the other
/// is a deliberate act rather than a matter of passing a different string.
/// </remarks>
[ApiController]
[Route("api/v1")]
public sealed class ReviewsController(ICurrentActor actor) : ApiControllerBase
{
    /// <param name="Rating">1 to 5. The scale is the platform's and is not configurable.</param>
    public sealed record LeaveReviewRequest(
        [param: Required, Range(1, 5)] int Rating,
        [param: StringLength(2000)] string? Comment);

    /// <summary>
    /// Rates the gallery behind one of the caller's own completed bookings.
    /// </summary>
    /// <remarks>
    /// The gallery is read from the booking, never named by the caller: a request that could name its
    /// subject could rate a competitor. Refused until the booking is Completed — which is the
    /// settlement window having elapsed with no dispute — so a rating cannot be used as leverage
    /// while money is still in question.
    /// </remarks>
    [Authorize(Policy = SecurityPolicies.Customer)]
    [HttpPost("bookings/{bookingId:guid}/review")]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Leave(
        Guid bookingId,
        [FromBody] LeaveReviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await Mediator.Send(
            new LeaveReviewCommand(actor.UserId!.Value, Id.From(bookingId), request.Rating, request.Comment),
            cancellationToken);

        return FromResult(result, review => Created($"/api/v1/bookings/{bookingId}/review", review));
    }

    /// <summary>
    /// The caller's own review of one booking, or 200 with null if they have not left one.
    /// </summary>
    /// <remarks>
    /// Null rather than 404, because "you have not reviewed this yet" is a legitimate answer about a
    /// booking that does exist. A 404 would make a client unable to tell it apart from a booking that
    /// is not theirs, and it needs to, to decide whether to offer the form.
    /// </remarks>
    [Authorize(Policy = SecurityPolicies.Customer)]
    [HttpGet("bookings/{bookingId:guid}/review")]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Mine(Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new GetMyReviewQuery(actor.UserId!.Value, Id.From(bookingId)),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// A gallery's public reviews, newest first.
    /// </summary>
    /// <remarks>
    /// Anonymous, for the same reason the catalogue is: a rating is the main thing a customer weighs
    /// before booking, and a marketplace that hides it until sign-up is asking to be trusted on
    /// nothing.
    ///
    /// No reviewer is named. Who left a review is not something the platform has asked a customer's
    /// permission to publish, and a name beside the dates of a rental says more than either fact on
    /// its own.
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Public)]
    [HttpGet("galleries/{dealerId:guid}/reviews")]
    [ProducesResponseType<PagedResult<GalleryReviewDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Gallery(
        Guid dealerId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new ListGalleryReviewsQuery(Id.From(dealerId), PageRequest.From(page, pageSize)),
            cancellationToken);
        return FromResult(result);
    }
}
