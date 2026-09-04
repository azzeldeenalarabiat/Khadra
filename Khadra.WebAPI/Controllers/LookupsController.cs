using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.PlatformSettings.Lookups;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The lookups every signed-in client reads: car types and cities (spec 3.2).
///
/// Authenticated but not Admin-only. A dealer picking a category for a car and a customer filtering
/// a search both need this list, and it contains nothing private. Only the ACTIVE entries: an entry
/// an administrator has retired should not be offered on a new car or a new search, while the
/// records that already reference it keep working.
/// </summary>
[Authorize]
[Route("api/v1")]
public sealed class LookupsController : ApiControllerBase
{
    [HttpGet("car-types")]
    [ProducesResponseType<IReadOnlyList<LookupEntryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> CarTypes(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ListCarTypesQuery(ActiveOnly: true), cancellationToken);
        return FromResult(result);
    }

    [HttpGet("cities")]
    [ProducesResponseType<IReadOnlyList<LookupEntryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Cities(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ListCitiesQuery(ActiveOnly: true), cancellationToken);
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
