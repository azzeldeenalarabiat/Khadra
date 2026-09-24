using Khadra.Application.Common;
using Khadra.Application.Fleet.BrowseCatalogue;
using Khadra.Application.Fleet.ReadModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The directory of rental offices a customer may browse, for the customer website.
/// </summary>
/// <remarks>
/// Its own controller rather than an action on <see cref="CatalogueController"/>, whose remarks keep
/// that class closed. Held to the same terms: anonymous, read-only, rate limited as public, and
/// <c>no-store</c> because the car count on each card moves the moment a car is listed or taken down.
/// A card carries none of an office's own writing, so the answer does not vary by language.
/// </remarks>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/v1/galleries")]
public sealed class GalleryDirectoryController : ApiControllerBase
{
    /// <summary>Rental offices a customer may be shown, most cars listed first.</summary>
    /// <remarks>
    /// Every filter is optional and named as `/vehicles` names its own. `text` matches part of the
    /// office's name, without case, literally (`%` and `_` are not wildcards); blank is no filter.
    /// `deliveryOnly` keeps the offices that have delivery switched on — the flag the card's delivery
    /// badge shows. `totalCount` counts the offices that match all of them.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<PagedResult<PublicGalleryCard>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> List(
        [FromQuery] Guid? cityId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? text,
        [FromQuery] bool deliveryOnly,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await Mediator.Send(
            new ListPublicGalleriesQuery(cityId, PageRequest.From(page, pageSize), text, deliveryOnly),
            cancellationToken);
        return FromResult(result);
    }
}
