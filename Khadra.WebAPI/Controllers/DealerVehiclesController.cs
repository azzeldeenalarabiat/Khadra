using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Fleet.Dtos;
using Khadra.Application.Fleet.ManageVehicles;
using Khadra.Application.Fleet.VehicleImages;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// A dealer's own fleet (spec 4.3).
///
/// Every mutating route carries RequireApprovedDealer, so a PENDING_REVIEW, rejected or suspended
/// dealer gets 403 dealer.not_approved and cannot list a single car. Reading is only DealerStaff:
/// an applicant still waiting should be able to open the (empty) screen they will eventually fill,
/// and refusing them the read would just look broken.
///
/// The dealer is always resolved from the token. A vehicle id in the URL is never sufficient on its
/// own — the handler checks the car belongs to the caller and reports anything else as not found.
/// </summary>
[Route("api/v1/dealers/me/vehicles")]
public sealed class DealerVehiclesController(ICurrentActor actor) : ApiControllerBase
{
    [Authorize(Policy = SecurityPolicies.DealerStaff)]
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<VehicleDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> List(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ListMyVehiclesQuery(actor.UserId!.Value), cancellationToken);
        return FromResult(result);
    }

    [Authorize(Policy = SecurityPolicies.DealerStaff)]
    [HttpGet("{vehicleId:guid}")]
    [ProducesResponseType<VehicleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Get(Guid vehicleId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new GetMyVehicleQuery(actor.UserId!.Value, Id.From(vehicleId)), cancellationToken);
        return FromResult(result);
    }

    /// <summary>Adds a car as a Draft. It reaches customer search only once published, which needs a photo.</summary>
    [Authorize(Policy = SecurityPolicies.ApprovedDealer)]
    [HttpPost]
    [ProducesResponseType<VehicleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Add(VehicleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new AddVehicleCommand(actor.UserId!.Value, request.ToInput()), cancellationToken);
        return FromResult(result, created => Created($"/api/v1/dealers/me/vehicles/{created.VehicleId}", created));
    }

    [Authorize(Policy = SecurityPolicies.ApprovedDealer)]
    [HttpPut("{vehicleId:guid}")]
    [ProducesResponseType<VehicleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Update(
        Guid vehicleId,
        VehicleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new UpdateVehicleCommand(actor.UserId!.Value, Id.From(vehicleId), request.ToInput()), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// The transitions a dealer controls: publish, hide, send to maintenance, bring it back.
    ///
    /// "Booked" is not among them, and never will be: whether a car is free on given dates is a
    /// question about bookings, and a stored flag would drift the first time one was cancelled.
    /// </summary>
    [Authorize(Policy = SecurityPolicies.ApprovedDealer)]
    [HttpPost("{vehicleId:guid}/status")]
    [ProducesResponseType<VehicleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> ChangeStatus(
        Guid vehicleId,
        StatusChangeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.TryParse<VehicleStatusChange>(request.Action, ignoreCase: true, out var change))
            return Failure(Domain.Fleet.FleetErrors.NotInMaintenance);

        var result = await Mediator.Send(
            new ChangeVehicleStatusCommand(actor.UserId!.Value, Id.From(vehicleId), change), cancellationToken);
        return FromResult(result);
    }

    /// <summary>Soft delete: bookings reference this car by id, so the row stays.</summary>
    [Authorize(Policy = SecurityPolicies.ApprovedDealer)]
    [HttpDelete("{vehicleId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid vehicleId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new DeleteVehicleCommand(actor.UserId!.Value, Id.From(vehicleId)), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// Step one of the spec 4.3 upload flow: ask where to put the photo. The client then PUTs the
    /// bytes straight to the returned URL, and confirms with POST .../images.
    /// </summary>
    [Authorize(Policy = SecurityPolicies.ApprovedDealer)]
    [HttpPost("{vehicleId:guid}/images/upload-url")]
    [ProducesResponseType<UploadTicket>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> RequestUpload(
        Guid vehicleId,
        UploadUrlRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new RequestVehicleImageUploadCommand(actor.UserId!.Value, Id.From(vehicleId), request.ContentType),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>Step three: the bytes are in place, record the photo against the car.</summary>
    [Authorize(Policy = SecurityPolicies.ApprovedDealer)]
    [HttpPost("{vehicleId:guid}/images")]
    [ProducesResponseType<VehicleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> AttachImage(
        Guid vehicleId,
        AttachImageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new AttachVehicleImageCommand(actor.UserId!.Value, Id.From(vehicleId), request.StorageKey),
            cancellationToken);
        return FromResult(result);
    }

    [Authorize(Policy = SecurityPolicies.ApprovedDealer)]
    [HttpDelete("{vehicleId:guid}/images/{imageId:guid}")]
    [ProducesResponseType<VehicleDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> RemoveImage(
        Guid vehicleId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new RemoveVehicleImageCommand(actor.UserId!.Value, Id.From(vehicleId), Id.From(imageId)),
            cancellationToken);
        return FromResult(result);
    }

    [Authorize(Policy = SecurityPolicies.ApprovedDealer)]
    [HttpPost("{vehicleId:guid}/images/{imageId:guid}/primary")]
    [ProducesResponseType<VehicleDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> SetPrimaryImage(
        Guid vehicleId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new SetPrimaryVehicleImageCommand(actor.UserId!.Value, Id.From(vehicleId), Id.From(imageId)),
            cancellationToken);
        return FromResult(result);
    }
}

public sealed record VehicleRequest(
    Guid CarTypeId,
    [param: Required, StringLength(60)] string Make,
    [param: Required, StringLength(60)] string Model,
    int Year,
    [param: StringLength(40)] string? Color,
    int Seats,
    [param: Required] string Transmission,
    [param: Required] string FuelType,
    [param: StringLength(2000)] string? Description,
    [param: Required, StringLength(20)] string PlateNumber,
    decimal DailyRate,
    decimal SecurityDeposit,
    bool IsDeliveryEligible,
    bool MileageUnlimited,
    int? MileageDailyLimitKm,
    decimal? MileageExcessFeePerKm,
    [param: Required] string FuelPolicy)
{
    public VehicleDetailsInput ToInput() => new(
        CarTypeId, Make, Model, Year, Color, Seats, Transmission, FuelType, Description, PlateNumber,
        DailyRate, SecurityDeposit, IsDeliveryEligible,
        new MileagePolicyInput(MileageUnlimited, MileageDailyLimitKm, MileageExcessFeePerKm),
        FuelPolicy);
}

public sealed record StatusChangeRequest([param: Required] string Action);

public sealed record UploadUrlRequest([param: Required, StringLength(100)] string ContentType);

public sealed record AttachImageRequest([param: Required, StringLength(500)] string StorageKey);
