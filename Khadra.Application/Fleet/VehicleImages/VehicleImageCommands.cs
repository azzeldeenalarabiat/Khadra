using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Fleet.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Fleet;
using Khadra.Domain.Fleet.Repositories;
using MediatR;

namespace Khadra.Application.Fleet.VehicleImages;

// Spec 4.3's presigned upload, in three steps:
//
//   1. RequestVehicleImageUpload -> a short-lived ticket and the key the file will live under
//   2. the client PUTs the bytes straight to that URL
//   3. AttachVehicleImage records it against the car
//
// Splitting it this way is what makes the flow portable: step 2 becomes an S3 presigned PUT without
// steps 1 and 3 changing at all, so the console and the Flutter app are written once.

public sealed record RequestVehicleImageUploadCommand(Id OwnerUserId, Id VehicleId, string ContentType)
    : ICommand<Result<UploadTicket, Error>>;

public sealed record AttachVehicleImageCommand(Id OwnerUserId, Id VehicleId, string StorageKey)
    : ICommand<Result<VehicleDto, Error>>;

public sealed record RemoveVehicleImageCommand(Id OwnerUserId, Id VehicleId, Id ImageId)
    : ICommand<Result<VehicleDto, Error>>;

public sealed record SetPrimaryVehicleImageCommand(Id OwnerUserId, Id VehicleId, Id ImageId)
    : ICommand<Result<VehicleDto, Error>>;

public sealed class RequestVehicleImageUploadCommandValidator
    : AbstractValidator<RequestVehicleImageUploadCommand>
{
    public RequestVehicleImageUploadCommandValidator() =>
        RuleFor(command => command.ContentType).NotEmpty().MaximumLength(100);
}

public sealed class AttachVehicleImageCommandValidator : AbstractValidator<AttachVehicleImageCommand>
{
    public AttachVehicleImageCommandValidator() =>
        RuleFor(command => command.StorageKey).NotEmpty().MaximumLength(500);
}

public sealed class VehicleImageHandlers(
    IVehicleRepository vehicles,
    IDealerRepository dealers,
    IUploadTicketService tickets,
    IDocumentStorage storage,
    IDocumentPolicySettings policy,
    IClock clock,
    IUnitOfWork unitOfWork) :
    IRequestHandler<RequestVehicleImageUploadCommand, Result<UploadTicket, Error>>,
    IRequestHandler<AttachVehicleImageCommand, Result<VehicleDto, Error>>,
    IRequestHandler<RemoveVehicleImageCommand, Result<VehicleDto, Error>>,
    IRequestHandler<SetPrimaryVehicleImageCommand, Result<VehicleDto, Error>>
{
    public async Task<Result<UploadTicket, Error>> Handle(
        RequestVehicleImageUploadCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owned = await LoadOwnedAsync(request.OwnerUserId, request.VehicleId, cancellationToken);
        if (owned.IsFailure)
            return owned.Error;

        if (!policy.AllowedContentTypes.Contains(request.ContentType, StringComparer.OrdinalIgnoreCase))
            return FleetErrors.InvalidImageType;

        // The cap is checked here as well as on attach: no point handing out a ticket for a photo
        // the car has no room for.
        if (owned.Value.Images.Count >= Vehicle.MaxImages)
            return FleetErrors.TooManyImages;

        // The key is the SERVER's choice, scoped to this car. A client that could name the key could
        // write over another dealer's photo, or somewhere else entirely.
        var key = $"vehicles/{request.VehicleId.Value}/{Guid.CreateVersion7():N}{Extension(request.ContentType)}";
        return tickets.Issue(key, request.ContentType, clock.UtcNow);
    }

    public async Task<Result<VehicleDto, Error>> Handle(
        AttachVehicleImageCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owned = await LoadOwnedAsync(request.OwnerUserId, request.VehicleId, cancellationToken);
        if (owned.IsFailure)
            return owned.Error;

        // The key must be one WE issued for THIS car. Without this a dealer could attach any key they
        // could guess, including a private identity document from another context.
        if (!request.StorageKey.StartsWith($"vehicles/{request.VehicleId.Value}/", StringComparison.Ordinal))
            return FleetErrors.ImageNotFound;

        // And the bytes have to actually be there: a client that skipped step two would otherwise
        // leave the listing pointing at nothing.
        await using var content = await storage.OpenAsync(request.StorageKey, cancellationToken);
        if (content is null)
            return FleetErrors.ImageNotFound;

        var attached = owned.Value.AddImage(request.StorageKey, clock.UtcNow);
        if (attached.IsFailure)
            return attached.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await DescribeAsync(request.OwnerUserId, owned.Value, cancellationToken);
    }

    public async Task<Result<VehicleDto, Error>> Handle(
        RemoveVehicleImageCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owned = await LoadOwnedAsync(request.OwnerUserId, request.VehicleId, cancellationToken);
        if (owned.IsFailure)
            return owned.Error;

        var image = owned.Value.Images.SingleOrDefault(candidate => candidate.Id == request.ImageId);
        var removed = owned.Value.RemoveImage(request.ImageId);
        if (removed.IsFailure)
            return removed.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Delete the blob only once the row is gone, so a failed save never orphans the listing.
        if (image is not null)
            await storage.DeleteAsync(image.StorageKey, cancellationToken);

        return await DescribeAsync(request.OwnerUserId, owned.Value, cancellationToken);
    }

    public async Task<Result<VehicleDto, Error>> Handle(
        SetPrimaryVehicleImageCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owned = await LoadOwnedAsync(request.OwnerUserId, request.VehicleId, cancellationToken);
        if (owned.IsFailure)
            return owned.Error;

        var primary = owned.Value.SetPrimaryImage(request.ImageId);
        if (primary.IsFailure)
            return primary.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await DescribeAsync(request.OwnerUserId, owned.Value, cancellationToken);
    }

    private async Task<Result<Vehicle, Error>> LoadOwnedAsync(
        Id ownerUserId,
        Id vehicleId,
        CancellationToken cancellationToken)
    {
        var dealer = await dealers.GetByOwnerUserIdAsync(ownerUserId, cancellationToken);
        if (dealer is null)
            return Domain.Dealers.DealerErrors.NotRegistered;

        var vehicle = await vehicles.GetByIdAsync(vehicleId, cancellationToken);
        // Another dealer's car reads as not found: a 403 would confirm the id is real.
        if (vehicle is null || vehicle.DealerId != dealer.Id)
            return FleetErrors.NotYours;

        return vehicle;
    }

    private async Task<Result<VehicleDto, Error>> DescribeAsync(
        Id ownerUserId,
        Vehicle vehicle,
        CancellationToken cancellationToken)
    {
        var dealer = await dealers.GetByOwnerUserIdAsync(ownerUserId, cancellationToken);
        return VehicleDto.From(vehicle, dealer?.CanTrade ?? false);
    }

    private static string Extension(string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
}
