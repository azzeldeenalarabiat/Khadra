using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Dealers;
using Khadra.Application.Common.Ports;
using Khadra.Application.Fleet.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Fleet;
using Khadra.Domain.Fleet.Repositories;
using Khadra.Domain.PlatformSettings.Repositories;
using MediatR;

namespace Khadra.Application.Fleet.ManageVehicles;

// Spec 4.3: a dealer adds, edits, hides and soft-deletes their own cars, sets per-car delivery
// eligibility, mileage and fuel policy, and a per-car security deposit.
//
// Every use case here resolves the dealer FROM THE ACTOR and then checks the car belongs to them. The
// vehicle id in the URL is never trusted on its own: without that check any approved dealer could
// edit any other dealer's fleet by guessing an id.

public sealed record MileagePolicyInput(bool IsUnlimited, int? DailyLimitKm, decimal? ExcessFeePerKm);

public sealed record VehicleDetailsInput(
    Guid CarTypeId,
    string Make,
    string Model,
    int Year,
    string? Color,
    int Seats,
    string Transmission,
    string FuelType,
    string? Description,
    string PlateNumber,
    decimal DailyRate,
    decimal SecurityDeposit,
    bool IsDeliveryEligible,
    MileagePolicyInput Mileage,
    string FuelPolicy);

// Reading the fleet is for any member of staff: an employee answering "is this car free?" needs the
// list as much as the owner does. Changing it stays with the owner, which the endpoints enforce.
public sealed record ListMyVehiclesQuery(Id ActorUserId) : IQuery<Result<IReadOnlyList<VehicleDto>, Error>>;

public sealed record GetMyVehicleQuery(Id ActorUserId, Id VehicleId) : IQuery<Result<VehicleDto, Error>>;

public sealed record AddVehicleCommand(Id OwnerUserId, VehicleDetailsInput Details)
    : ICommand<Result<VehicleDto, Error>>;

public sealed record UpdateVehicleCommand(Id OwnerUserId, Id VehicleId, VehicleDetailsInput Details)
    : ICommand<Result<VehicleDto, Error>>;

/// <summary>
/// The four transitions a dealer controls. Draft is only ever left, and "Booked" is deliberately not
/// among them: availability is a question about bookings, never a stored flag on the car.
/// </summary>
public enum VehicleStatusChange
{
    Publish,
    Hide,
    SendToMaintenance,
    ReturnFromMaintenance
}

public sealed record ChangeVehicleStatusCommand(Id OwnerUserId, Id VehicleId, VehicleStatusChange Change)
    : ICommand<Result<VehicleDto, Error>>;

public sealed record DeleteVehicleCommand(Id OwnerUserId, Id VehicleId) : ICommand<UnitResult<Error>>;

public sealed class VehicleDetailsInputValidator : AbstractValidator<VehicleDetailsInput>
{
    public VehicleDetailsInputValidator()
    {
        RuleFor(input => input.Make).NotEmpty().MaximumLength(60);
        RuleFor(input => input.Model).NotEmpty().MaximumLength(60);
        // Only the bound that can never move. The platform's own floor is BusinessRules
        // .EarliestVehicleModelYear and VehicleDetails enforces it, so there is one authority for it
        // rather than a copy here that would go stale the day the owner changes it.
        RuleFor(input => input.Year).InclusiveBetween(VehicleDetails.EarliestPossibleModelYear, DateTime.UtcNow.Year + 1);
        RuleFor(input => input.Seats).InclusiveBetween(1, 20);
        // Raw input, so it has room for the separators the value object strips; the digit count is
        // PlateNumber's rule, not this one's.
        RuleFor(input => input.PlateNumber).NotEmpty().MaximumLength(30);
        RuleFor(input => input.DailyRate).GreaterThan(0m);
        RuleFor(input => input.SecurityDeposit).GreaterThanOrEqualTo(0m);
        RuleFor(input => input.Description).MaximumLength(VehicleDetails.MaxDescriptionLength);
        RuleFor(input => input.Mileage.DailyLimitKm)
            .GreaterThan(0)
            .When(input => !input.Mileage.IsUnlimited)
            .WithMessage("A limited mileage policy needs a daily allowance.");
    }
}

public sealed class AddVehicleCommandValidator : AbstractValidator<AddVehicleCommand>
{
    public AddVehicleCommandValidator() =>
        RuleFor(command => command.Details).NotNull().SetValidator(new VehicleDetailsInputValidator());
}

public sealed class UpdateVehicleCommandValidator : AbstractValidator<UpdateVehicleCommand>
{
    public UpdateVehicleCommandValidator() =>
        RuleFor(command => command.Details).NotNull().SetValidator(new VehicleDetailsInputValidator());
}

public sealed class VehicleHandlers(
    IVehicleRepository vehicles,
    IDealerRepository dealers,
    DealerMembershipResolver membership,
    IClock clock,
    IBusinessRulesProvider businessRules,
    ICarTypeRepository carTypes,
    IUnitOfWork unitOfWork) :
    IRequestHandler<ListMyVehiclesQuery, Result<IReadOnlyList<VehicleDto>, Error>>,
    IRequestHandler<GetMyVehicleQuery, Result<VehicleDto, Error>>,
    IRequestHandler<AddVehicleCommand, Result<VehicleDto, Error>>,
    IRequestHandler<UpdateVehicleCommand, Result<VehicleDto, Error>>,
    IRequestHandler<ChangeVehicleStatusCommand, Result<VehicleDto, Error>>,
    IRequestHandler<DeleteVehicleCommand, UnitResult<Error>>
{
    public async Task<Result<IReadOnlyList<VehicleDto>, Error>> Handle(
        ListMyVehiclesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Through the membership resolver, not the owner lookup: the endpoint admits any dealer
        // staff, and resolving by owner alone handed an employee "you have no dealership" for a
        // fleet they work with every day.
        var member = await membership.ResolveAsync(request.ActorUserId, cancellationToken);
        if (member.IsFailure)
            return member.Error;
        var dealer = member.Value.Dealer;

        var fleet = await vehicles.ListByDealerAsync(dealer.Id, cancellationToken);
        IReadOnlyList<VehicleDto> listed = [.. fleet.Select(vehicle => VehicleDto.From(vehicle, dealer.CanTrade))];
        return Result.Success<IReadOnlyList<VehicleDto>, Error>(listed);
    }

    public async Task<Result<VehicleDto, Error>> Handle(GetMyVehicleQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Same reasoning as the list: staff may read one of their dealership's cars.
        var member = await membership.ResolveAsync(request.ActorUserId, cancellationToken);
        if (member.IsFailure)
            return member.Error;

        var vehicle = await vehicles.GetByIdAsync(request.VehicleId, cancellationToken);
        return vehicle is null || vehicle.DealerId != member.Value.Dealer.Id
            ? FleetErrors.NotYours
            : VehicleDto.From(vehicle, member.Value.Dealer.CanTrade);
    }

    public async Task<Result<VehicleDto, Error>> Handle(AddVehicleCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dealer = await dealers.GetByOwnerUserIdAsync(request.OwnerUserId, cancellationToken);
        if (dealer is null)
            return DealerErrors.NotRegistered;

        var rules = await businessRules.GetAsync(cancellationToken);
        var parsed = ParseDetails(request.Details, rules.EarliestVehicleModelYear);
        if (parsed.IsFailure)
            return parsed.Error;

        // Nothing joins a vehicle to its car type — cross-context references are by id and carry no
        // foreign key — so this check is the only thing standing between a typo and a car that
        // points at a category which does not exist. A new listing must name a type that is offered.
        var carTypeCheck = await RequireCarType(Id.From(request.Details.CarTypeId), mustBeOffered: true, cancellationToken);
        if (carTypeCheck.IsFailure)
            return carTypeCheck.Error;

        // Spec: one car, one listing. A plate already on the platform means either a duplicate or a
        // dealer listing a car that is not theirs.
        if (await vehicles.PlateNumberExistsAsync(parsed.Value.Plate, cancellationToken))
            return FleetErrors.PlateNumberTaken;

        var vehicle = Vehicle.Add(
            dealer.Id,
            Id.From(request.Details.CarTypeId),
            parsed.Value.Details,
            parsed.Value.Plate,
            parsed.Value.DailyRate,
            parsed.Value.SecurityDeposit,
            parsed.Value.Mileage,
            parsed.Value.FuelPolicy,
            request.Details.IsDeliveryEligible,
            clock.UtcNow);
        if (vehicle.IsFailure)
            return vehicle.Error;

        await vehicles.AddAsync(vehicle.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return VehicleDto.From(vehicle.Value, dealer.CanTrade);
    }

    public async Task<Result<VehicleDto, Error>> Handle(
        UpdateVehicleCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owned = await LoadOwnedAsync(request.OwnerUserId, request.VehicleId, cancellationToken);
        if (owned.IsFailure)
            return owned.Error;

        var (dealer, vehicle) = owned.Value;
        var rules = await businessRules.GetAsync(cancellationToken);
        var parsed = ParseDetails(request.Details, rules.EarliestVehicleModelYear);
        if (parsed.IsFailure)
            return parsed.Error;

        // A type the dealer is CHANGING to must still be offered; the one already on the car need
        // only exist. Otherwise retiring a category would freeze every car in it — the owner could
        // not correct a price until they had re-categorised, which is not a decision a price edit
        // should force. Mirrors the plate rule just below.
        var carTypeId = Id.From(request.Details.CarTypeId);
        var carTypeCheck = await RequireCarType(carTypeId, mustBeOffered: carTypeId != vehicle.CarTypeId, cancellationToken);
        if (carTypeCheck.IsFailure)
            return carTypeCheck.Error;

        // Only worth a lookup if the plate actually changed; otherwise the car collides with itself.
        if (parsed.Value.Plate != vehicle.PlateNumber &&
            await vehicles.PlateNumberExistsAsync(parsed.Value.Plate, cancellationToken))
        {
            return FleetErrors.PlateNumberTaken;
        }

        var updated = vehicle.UpdateDetails(
            Id.From(request.Details.CarTypeId),
            parsed.Value.Details,
            parsed.Value.Plate);
        if (updated.IsFailure)
            return updated.Error;

        var rate = vehicle.ChangeDailyRate(parsed.Value.DailyRate, clock.UtcNow);
        if (rate.IsFailure)
            return rate.Error;

        var deposit = vehicle.ChangeSecurityDeposit(parsed.Value.SecurityDeposit);
        if (deposit.IsFailure)
            return deposit.Error;

        vehicle.SetDeliveryEligible(request.Details.IsDeliveryEligible);
        vehicle.SetMileagePolicy(parsed.Value.Mileage);
        vehicle.SetFuelPolicy(parsed.Value.FuelPolicy);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return VehicleDto.From(vehicle, dealer.CanTrade);
    }

    public async Task<Result<VehicleDto, Error>> Handle(
        ChangeVehicleStatusCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owned = await LoadOwnedAsync(request.OwnerUserId, request.VehicleId, cancellationToken);
        if (owned.IsFailure)
            return owned.Error;

        var (dealer, vehicle) = owned.Value;
        var now = clock.UtcNow;
        var change = request.Change switch
        {
            // Publishing asks the aggregate again whether the dealer may trade; the endpoint policy
            // says the same thing, and neither layer is load-bearing on its own.
            VehicleStatusChange.Publish => vehicle.Publish(dealer.CanTrade, now),
            VehicleStatusChange.Hide => vehicle.Hide(now),
            VehicleStatusChange.SendToMaintenance => vehicle.SendToMaintenance(now),
            VehicleStatusChange.ReturnFromMaintenance => vehicle.ReturnFromMaintenance(),
            _ => UnitResult.Failure<Error>(FleetErrors.NotYours)
        };
        if (change.IsFailure)
            return change.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return VehicleDto.From(vehicle, dealer.CanTrade);
    }

    public async Task<UnitResult<Error>> Handle(DeleteVehicleCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owned = await LoadOwnedAsync(request.OwnerUserId, request.VehicleId, cancellationToken);
        if (owned.IsFailure)
            return UnitResult.Failure(owned.Error);

        // Soft delete: the listing disappears but the row stays, because bookings reference this
        // vehicle by id and a hard delete would orphan a rental's history.
        var deleted = owned.Value.Vehicle.Delete(clock.UtcNow);
        if (deleted.IsFailure)
            return deleted;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Resolves the actor's dealer and the car, and refuses anything that is not theirs.
    ///
    /// A car belonging to another dealer is reported as NOT FOUND rather than forbidden: a 403 would
    /// confirm the id exists, which is enough to enumerate a competitor's fleet.
    /// </summary>
    private async Task<Result<(Dealer Dealer, Vehicle Vehicle), Error>> LoadOwnedAsync(
        Id ownerUserId,
        Id vehicleId,
        CancellationToken cancellationToken)
    {
        var dealer = await dealers.GetByOwnerUserIdAsync(ownerUserId, cancellationToken);
        if (dealer is null)
            return DealerErrors.NotRegistered;

        var vehicle = await vehicles.GetByIdAsync(vehicleId, cancellationToken);
        if (vehicle is null || vehicle.DealerId != dealer.Id)
            return FleetErrors.NotYours;

        return (dealer, vehicle);
    }

    private async Task<UnitResult<Error>> RequireCarType(Id carTypeId, bool mustBeOffered, CancellationToken cancellationToken)
    {
        var carType = await carTypes.GetByIdAsync(carTypeId, cancellationToken);
        if (carType is null)
            return UnitResult.Failure(FleetErrors.UnknownCarType);
        if (mustBeOffered && !carType.IsActive)
            return UnitResult.Failure(FleetErrors.CarTypeRetired);

        return UnitResult.Success<Error>();
    }

    private static Result<ParsedVehicle, Error> ParseDetails(VehicleDetailsInput input, int earliestModelYear)
    {
        // Each field answers for itself. All three used to fall through to InvalidMakeOrModel, so a
        // bad fuel type told the caller — including the Flutter app — to fix the make and model,
        // which were fine.
        var transmission = Enumeration.GetAll<TransmissionType>()
            .SingleOrDefault(type => string.Equals(type.Name, input.Transmission, StringComparison.OrdinalIgnoreCase));
        if (transmission is null)
            return FleetErrors.InvalidTransmission;

        var fuelType = Enumeration.GetAll<FuelType>()
            .SingleOrDefault(type => string.Equals(type.Name, input.FuelType, StringComparison.OrdinalIgnoreCase));
        if (fuelType is null)
            return FleetErrors.InvalidFuelType;

        var fuelPolicy = Enumeration.GetAll<FuelPolicy>()
            .SingleOrDefault(policy => string.Equals(policy.Name, input.FuelPolicy, StringComparison.OrdinalIgnoreCase));
        if (fuelPolicy is null)
            return FleetErrors.InvalidFuelPolicy;

        var details = VehicleDetails.Create(
            input.Make, input.Model, input.Year, input.Seats, transmission, fuelType,
            currentYear: DateTime.UtcNow.Year, input.Color, input.Description, earliestModelYear);
        if (details.IsFailure)
            return details.Error;

        var plate = PlateNumber.Create(input.PlateNumber);
        if (plate.IsFailure)
            return plate.Error;

        var mileage = input.Mileage.IsUnlimited
            ? Result.Success<MileagePolicy, Error>(MileagePolicy.Unlimited())
            : MileagePolicy.Limited(
                input.Mileage.DailyLimitKm ?? 0,
                Money.Jod(input.Mileage.ExcessFeePerKm ?? 0m));
        if (mileage.IsFailure)
            return mileage.Error;

        return new ParsedVehicle(
            details.Value,
            plate.Value,
            Money.Jod(input.DailyRate),
            Money.Jod(input.SecurityDeposit),
            mileage.Value,
            fuelPolicy);
    }

    private sealed record ParsedVehicle(
        VehicleDetails Details,
        PlateNumber Plate,
        Money DailyRate,
        Money SecurityDeposit,
        MileagePolicy Mileage,
        FuelPolicy FuelPolicy);
}
