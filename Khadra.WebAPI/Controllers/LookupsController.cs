using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.PlatformSettings.AppConfig;
using Khadra.Application.PlatformSettings.Lookups;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The lookups every client reads: car types and cities (spec 3.2).
///
/// Only the ACTIVE entries: an entry an administrator has retired should not be offered on a new car
/// or a new search, while the records that already reference it keep working.
///
/// Car types and cities are ANONYMOUS, because the customer catalogue is. They are the filter chips
/// above a search anyone may run, and a browse screen that could list cars but not name their
/// categories would be a strange kind of half-open. Neither list contains anything private — they
/// are two columns of bilingual labels an administrator curates.
///
/// The model-year range stays behind a sign-in: it exists so the DEALER form does not have to guess
/// its own bounds, and no customer screen asks for it.
/// </summary>
[Authorize]
[Route("api/v1")]
public sealed class LookupsController : ApiControllerBase
{
    [HttpGet("car-types")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Public)]
    [ProducesResponseType<IReadOnlyList<LookupEntryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> CarTypes(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ListCarTypesQuery(ActiveOnly: true), cancellationToken);
        return FromResult(result);
    }

    [HttpGet("cities")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Public)]
    [ProducesResponseType<IReadOnlyList<LookupEntryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Cities(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ListCitiesQuery(ActiveOnly: true), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// What a client needs to know about the platform before it can ask anything sensible.
    /// </summary>
    /// <remarks>
    /// Anonymous and cheap, called once at startup. Everything in it is a value the server already
    /// owns and a phone would otherwise hard-code -- which means a new release in every shop each
    /// time the owner changes a number.
    /// </remarks>
    [HttpGet("app-config")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Public)]
    [ProducesResponseType<AppConfigDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> AppConfig(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetAppConfigQuery(), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// The model years a car may be listed under, so a form does not have to guess the range.
    /// </summary>
    [HttpGet("vehicle-model-years")]
    [ProducesResponseType<VehicleModelYearRange>(StatusCodes.Status200OK)]
    public async Task<ActionResult> VehicleModelYears(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetVehicleModelYearsQuery(), cancellationToken);
        return FromResult(result);
    }
}

/// <summary>
/// Curating those lists.
///
/// Nothing here deletes. Deactivating retires an entry from new cars and new searches while every
/// vehicle and booking already pointing at it goes on reading correctly — which a delete could not
/// promise, because these ids are referenced from other bounded contexts by id and nothing joins
/// back to warn you.
/// </summary>
[Authorize(Policy = SecurityPolicies.Admin)]
[Route("api/v1/admin/lookups")]
public sealed class AdminLookupsController : ApiControllerBase
{
    public sealed record CreateCarTypeRequest(
        [Required, MaxLength(100)] string NameEn,
        [Required, MaxLength(100)] string NameAr,
        [Range(0, 9999)] int DisplayOrder);

    public sealed record CreateCityRequest(
        [Required, MaxLength(100)] string NameEn,
        [Required, MaxLength(100)] string NameAr,
        [Range(0, 9999)] int DisplayOrder,
        double? Latitude,
        double? Longitude);

    public sealed record RenameRequest(
        [Required, MaxLength(100)] string NameEn,
        [Required, MaxLength(100)] string NameAr);

    /// <summary>Every entry, retired ones included — this is the screen that curates them.</summary>
    [HttpGet("car-types")]
    [ProducesResponseType<IReadOnlyList<LookupEntryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> CarTypes(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ListCarTypesQuery(ActiveOnly: false), cancellationToken);
        return FromResult(result);
    }

    [HttpGet("cities")]
    [ProducesResponseType<IReadOnlyList<LookupEntryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Cities(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ListCitiesQuery(ActiveOnly: false), cancellationToken);
        return FromResult(result);
    }

    [HttpPost("car-types")]
    [ProducesResponseType<LookupEntryDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult> CreateCarType([FromBody] CreateCarTypeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new CreateCarTypeCommand(request.NameEn, request.NameAr, request.DisplayOrder),
            cancellationToken);
        return FromResult(result, entry => Created($"/api/v1/admin/lookups/car-types/{entry.Id}", entry));
    }

    [HttpPost("cities")]
    [ProducesResponseType<LookupEntryDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult> CreateCity([FromBody] CreateCityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new CreateCityCommand(request.NameEn, request.NameAr, request.DisplayOrder, request.Latitude, request.Longitude),
            cancellationToken);
        return FromResult(result, entry => Created($"/api/v1/admin/lookups/cities/{entry.Id}", entry));
    }

    [HttpPost("{kind}/{id:guid}/rename")]
    [ProducesResponseType<LookupEntryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Rename(
        string kind,
        Guid id,
        [FromBody] RenameRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new RenameLookupCommand(Id.From(id), kind, request.NameEn, request.NameAr),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>Retires an entry from new cars and new searches. Never a delete.</summary>
    [HttpPost("{kind}/{id:guid}/deactivate")]
    [ProducesResponseType<LookupEntryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Deactivate(string kind, Guid id, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new SetLookupActiveCommand(Id.From(id), kind, IsActive: false), cancellationToken);
        return FromResult(result);
    }

    [HttpPost("{kind}/{id:guid}/activate")]
    [ProducesResponseType<LookupEntryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Activate(string kind, Guid id, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new SetLookupActiveCommand(Id.From(id), kind, IsActive: true), cancellationToken);
        return FromResult(result);
    }
}
