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
/// Both halves of spec 5.6 live here, and they are shaped very differently on purpose.
///
/// The customer's review of a gallery is PUBLIC: anonymous to read, free text allowed, and its score
/// survives moderation so a gallery cannot erase a bad rating by reporting the comment on it.
///
/// The gallery's rating of a customer is not public and never becomes so. It is a bare score with no
/// text at all; it is readable only as an AGGREGATE, only by a gallery holding a live booking with
/// that customer, and only through the booking that gives them the relationship. There is no endpoint
/// anywhere that takes a customer id, because that would be a lookup oracle over the whole customer
/// base for anybody with a dealer session.
///
/// Both directions are blind until the window closes or both sides are in. Without that, publishing
/// the customer's review the instant it is written would hand the gallery a retaliation button with
/// the platform's own machinery behind it.
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

    /// <param name="Rating">1 to 5, and nothing else. There is deliberately no comment field.</param>
    public sealed record RateCustomerRequest([param: Required, Range(1, 5)] int Rating);

    /// <summary>
    /// The gallery's rating of the customer on one of its own completed bookings (spec 5.6).
    /// </summary>
    /// <remarks>
    /// <b>No comment, and none is stored.</b> The audience is other galleries, so free text would be
    /// unverified prose about a named private individual circulating between competing businesses,
    /// unmoderated at the moment of writing and invisible to the person it describes. The platform has
    /// already replaced free text with closed codes twice, for weaker reasons than this.
    ///
    /// The customer is read from the booking. Completed only, like the customer's own direction and
    /// for the same reason: a rating must not be leverage while money is still in question.
    /// </remarks>
    [Authorize(Policy = SecurityPolicies.ApprovedDealerStaff)]
    [HttpPost("bookings/{bookingId:guid}/customer-rating")]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> RateCustomer(
        Guid bookingId,
        [FromBody] RateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await Mediator.Send(
            new RateCustomerCommand(actor.UserId!.Value, Id.From(bookingId), request.Rating),
            cancellationToken);

        return FromResult(
            result,
            review => Created($"/api/v1/bookings/{bookingId}/customer-rating", review));
    }

    /// <summary>The gallery's own rating of one booking, or 200 with null if they have not left one.</summary>
    [Authorize(Policy = SecurityPolicies.ApprovedDealerStaff)]
    [HttpGet("bookings/{bookingId:guid}/customer-rating")]
    [ProducesResponseType<ReviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> MyCustomerRating(Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new GetMyCustomerRatingQuery(actor.UserId!.Value, Id.From(bookingId)),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// What the platform knows about the customer behind one of this gallery's LIVE bookings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed on the booking, and that is the whole authorization model rather than a convenience. A
    /// gallery may read this for as long as they are deciding about, or holding, a booking with that
    /// person -- and no longer. There is no customer id to substitute and nothing to enumerate.
    /// </para>
    /// <para>
    /// It answers with AGGREGATES only: a rating, some counts, and when the account was made. No
    /// contact details, no documents, no per-review rows, no dates on individual ratings -- a rating
    /// dated last Tuesday would tell this gallery when the customer rented from a competitor.
    /// </para>
    /// <para>
    /// 409 <c>review.reputation_not_available</c> once the booking is no longer live. 404 for a
    /// booking belonging to another dealership, so a stranger cannot tell a real id from an invented
    /// one.
    /// </para>
    /// </remarks>
    [Authorize(Policy = SecurityPolicies.ApprovedDealerStaff)]
    [HttpGet("bookings/{bookingId:guid}/customer-reputation")]
    [ProducesResponseType<CustomerReputation>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> CustomerReputation(Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new GetCustomerReputationQuery(actor.UserId!.Value, Id.From(bookingId)),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// The caller's own reputation, as galleries see it.
    /// </summary>
    /// <remarks>
    /// A semi-private score about a person that the person cannot see is precisely what privacy law
    /// objects to -- and it is the only way a customer learns they should dispute a wrong no-show
    /// while the window is still open. Same shape; the with-this-gallery count is zero, because there
    /// is no gallery asking.
    /// </remarks>
    [Authorize(Policy = SecurityPolicies.Customer)]
    [HttpGet("customers/me/reputation")]
    [ProducesResponseType<CustomerReputation>(StatusCodes.Status200OK)]
    public async Task<ActionResult> MyReputation(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetMyReputationQuery(actor.UserId!.Value), cancellationToken);
        return FromResult(result);
    }
}
