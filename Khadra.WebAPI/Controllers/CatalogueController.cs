using Khadra.Application.Common;
using Khadra.Application.Fleet.BrowseCatalogue;
using Khadra.Application.Fleet.ReadModels;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The shop window: what anyone can see before they have an account.
/// </summary>
/// <remarks>
/// Anonymous on purpose, settled with the owner on 2026-09-07. Daily rates, delivery fees and the
/// deposit percentage are public prices, and a marketplace that demands a sign-up before it will
/// show a car converts badly — a tourist comparing prices before flying to Jordan has no reason to
/// create an account first. Booking still requires one.
///
/// NOTHING ELSE IS EVER ADDED TO THIS CONTROLLER. Every action on it is anonymous and read-only, and
/// keeping that true of the whole class is what makes the `[AllowAnonymous]` at the top safe to read
/// at a glance. A customer-facing WRITE belongs on a controller that authenticates.
///
/// The rate limit is deliberately generous. Mobile carriers put thousands of subscribers behind one
/// address, so a tight per-IP limit here would lock out a whole network rather than an abuser — see
/// pre-launch checklist item 32, which already notes the partitioning problem behind a proxy.
///
/// Responses are `no-store`. Availability changes the moment somebody books, and a cached search
/// showing a car that has since been taken is worse than a slow one. The image paths these responses
/// point at are a different matter and are cached normally.
/// </remarks>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/v1")]
public sealed class CatalogueController : ApiControllerBase
{
    /// <summary>Cars a customer could book, newest listing first.</summary>
    /// <remarks>
    /// Supply both `pickupAt` and `returnAt` to see only what is free across those instants, or
    /// neither to browse everything listed. Half a period is refused rather than ignored.
    /// </remarks>
    [HttpGet("vehicles")]
    [ProducesResponseType<PagedResult<CatalogueListing>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Search(
        [FromQuery] Guid? cityId,
        [FromQuery] Guid? carTypeId,
        [FromQuery] Guid? dealerId,
        [FromQuery] decimal? minDailyRate,
        [FromQuery] decimal? maxDailyRate,
        [FromQuery] string? transmission,
        [FromQuery] int? minSeats,
        [FromQuery] bool deliveryOnly,
        [FromQuery] string? text,
        [FromQuery] DateTimeOffset? pickupAt,
        [FromQuery] DateTimeOffset? returnAt,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        NoStore();
        var result = await Mediator.Send(
            new SearchCatalogueQuery(
                cityId, carTypeId, dealerId, minDailyRate, maxDailyRate, transmission, minSeats,
                deliveryOnly, text, pickupAt, returnAt, PageRequest.From(page, pageSize)),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>One car in full.</summary>
    /// <remarks>
    /// Answers 404 for a car that is not listed, whose gallery cannot trade, or that does not exist,
    /// without saying which. Anything more specific would let an anonymous caller enumerate a
    /// competitor's unpublished inventory.
    /// </remarks>
    [HttpGet("vehicles/{vehicleId:guid}")]
    [ProducesResponseType<CatalogueVehicle>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Vehicle(
        Guid vehicleId,
        [FromQuery] DateTimeOffset? pickupAt,
        [FromQuery] DateTimeOffset? returnAt,
        CancellationToken cancellationToken)
    {
        NoStore();
        var result = await Mediator.Send(
            new GetCatalogueVehicleQuery(Id.From(vehicleId), pickupAt, returnAt), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// What a named rental would cost, priced by the server.
    /// </summary>
    /// <remarks>
    /// The app displays these figures and never computes its own. The day count in particular is the
    /// server's: rentals are billed in Amman calendar days, and a phone subtracting two instants
    /// would get a different answer for the same rental.
    /// </remarks>
    [HttpGet("vehicles/{vehicleId:guid}/quote")]
    [ProducesResponseType<RentalQuote>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Quote(
        Guid vehicleId,
        [FromQuery] DateTimeOffset pickupAt,
        [FromQuery] DateTimeOffset returnAt,
        [FromQuery] string pickupMethod,
        [FromQuery] double? latitude,
        [FromQuery] double? longitude,
        CancellationToken cancellationToken)
    {
        NoStore();
        var result = await Mediator.Send(
            new QuoteRentalQuery(Id.From(vehicleId), pickupAt, returnAt, pickupMethod, latitude, longitude),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>A gallery's public page.</summary>
    [HttpGet("galleries/{dealerId:guid}")]
    [ProducesResponseType<PublicGallery>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Gallery(Guid dealerId, CancellationToken cancellationToken)
    {
        NoStore();
        var result = await Mediator.Send(new GetPublicGalleryQuery(Id.From(dealerId)), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// Availability is live, so none of this may be cached.
    /// </summary>
    /// <remarks>
    /// A car free when the response was built can be taken a second later. A shared cache in front of
    /// the API would hand the stale answer to everyone behind it, and the customer who acts on it
    /// gets a refusal at the point of booking instead of at the point of looking.
    /// </remarks>
    private void NoStore() => Response.Headers.CacheControl = "no-store";
}
