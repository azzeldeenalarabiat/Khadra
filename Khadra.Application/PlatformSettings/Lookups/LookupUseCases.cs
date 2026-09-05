using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.PlatformSettings;
using Khadra.Domain.PlatformSettings.Repositories;
using MediatR;

namespace Khadra.Application.PlatformSettings.Lookups;

// Cities and car types (spec 3.2): the two lists an administrator curates and the customer app
// searches by. Both are LookupEntry, so both take the same four actions, and neither is ever
// deleted — deactivating hides an entry from new searches while the records referencing it stay
// intact. A car type a hundred bookings point at cannot be made to disappear from those bookings.

public sealed record LookupEntryDto(
    Guid Id,
    string NameEn,
    string NameAr,
    bool IsActive,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    double? CentreLatitude,
    double? CentreLongitude)
{
    public static LookupEntryDto From(CarType carType)
    {
        ArgumentNullException.ThrowIfNull(carType);
        return new LookupEntryDto(
            carType.Id.Value, carType.NameEn, carType.NameAr, carType.IsActive,
            carType.DisplayOrder, carType.CreatedAt, null, null);
    }

    public static LookupEntryDto From(City city)
    {
        ArgumentNullException.ThrowIfNull(city);
        return new LookupEntryDto(
            city.Id.Value, city.NameEn, city.NameAr, city.IsActive, city.DisplayOrder,
            city.CreatedAt, city.Centre?.Latitude, city.Centre?.Longitude);
    }
}

public sealed record ListCarTypesQuery(bool ActiveOnly) : IQuery<Result<IReadOnlyList<LookupEntryDto>, Error>>;

public sealed record ListCitiesQuery(bool ActiveOnly) : IQuery<Result<IReadOnlyList<LookupEntryDto>, Error>>;

public sealed record CreateCarTypeCommand(string NameEn, string NameAr, int DisplayOrder)
    : ICommand<Result<LookupEntryDto, Error>>;

public sealed record CreateCityCommand(string NameEn, string NameAr, int DisplayOrder, double? Latitude, double? Longitude)
    : ICommand<Result<LookupEntryDto, Error>>;

public sealed record RenameLookupCommand(Id Id, string Kind, string NameEn, string NameAr)
    : ICommand<Result<LookupEntryDto, Error>>;

public sealed record SetLookupActiveCommand(Id Id, string Kind, bool IsActive)
    : ICommand<Result<LookupEntryDto, Error>>;

public sealed class CreateCarTypeCommandValidator : AbstractValidator<CreateCarTypeCommand>
{
    public CreateCarTypeCommandValidator()
    {
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(100);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(100);
        RuleFor(command => command.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateCityCommandValidator : AbstractValidator<CreateCityCommand>
{
    public CreateCityCommandValidator()
    {
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(100);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(100);
        RuleFor(command => command.DisplayOrder).GreaterThanOrEqualTo(0);

        // Both or neither: half a coordinate is not a place.
        RuleFor(command => command.Longitude)
            .NotNull()
            .When(command => command.Latitude is not null)
            .WithMessage("A centre point needs both a latitude and a longitude.");
        RuleFor(command => command.Latitude)
            .NotNull()
            .When(command => command.Longitude is not null)
            .WithMessage("A centre point needs both a latitude and a longitude.");
    }
}

public sealed class RenameLookupCommandValidator : AbstractValidator<RenameLookupCommand>
{
    public RenameLookupCommandValidator()
    {
        // The aggregate refuses a blank or over-long name too; this answers 400 with the field named
        // rather than letting a domain error surface as an unexplained conflict.
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(100);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Kind)
            .Must(kind =>
                string.Equals(kind, LookupHandlers.CarTypesKind, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, LookupHandlers.CitiesKind, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Unknown lookup.");
    }
}

public sealed class SetLookupActiveCommandValidator : AbstractValidator<SetLookupActiveCommand>
{
    public SetLookupActiveCommandValidator()
    {
        // The kind arrives from the route, so an unknown one must be refused rather than silently
        // falling through to the other list.
        RuleFor(command => command.Kind)
            .Must(kind =>
                string.Equals(kind, LookupHandlers.CarTypesKind, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, LookupHandlers.CitiesKind, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Unknown lookup.");
    }
}

public sealed class LookupHandlers(
    ICarTypeRepository carTypes,
    ICityRepository cities,
    IUnitOfWork unitOfWork,
    IClock clock) :
    IRequestHandler<ListCarTypesQuery, Result<IReadOnlyList<LookupEntryDto>, Error>>,
    IRequestHandler<ListCitiesQuery, Result<IReadOnlyList<LookupEntryDto>, Error>>,
    IRequestHandler<CreateCarTypeCommand, Result<LookupEntryDto, Error>>,
    IRequestHandler<CreateCityCommand, Result<LookupEntryDto, Error>>,
    IRequestHandler<RenameLookupCommand, Result<LookupEntryDto, Error>>,
    IRequestHandler<SetLookupActiveCommand, Result<LookupEntryDto, Error>>
{
    public async Task<Result<IReadOnlyList<LookupEntryDto>, Error>> Handle(
        ListCarTypesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var list = await carTypes.ListAsync(request.ActiveOnly, cancellationToken);
        return Result.Success<IReadOnlyList<LookupEntryDto>, Error>(list.Select(LookupEntryDto.From).ToList());
    }

    public async Task<Result<IReadOnlyList<LookupEntryDto>, Error>> Handle(
        ListCitiesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var list = await cities.ListAsync(request.ActiveOnly, cancellationToken);
        return Result.Success<IReadOnlyList<LookupEntryDto>, Error>(list.Select(LookupEntryDto.From).ToList());
    }

    public async Task<Result<LookupEntryDto, Error>> Handle(CreateCarTypeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var created = CarType.Create(request.NameEn, request.NameAr, request.DisplayOrder, clock.UtcNow);
        if (created.IsFailure)
            return created.Error;

        await carTypes.AddAsync(created.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return LookupEntryDto.From(created.Value);
    }

    public async Task<Result<LookupEntryDto, Error>> Handle(CreateCityCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        GeoPoint? centre = null;
        if (request.Latitude is { } latitude && request.Longitude is { } longitude)
        {
            var point = GeoPoint.Create(latitude, longitude);
            if (point.IsFailure)
                return point.Error;
            centre = point.Value;
        }

        var created = City.Create(request.NameEn, request.NameAr, request.DisplayOrder, clock.UtcNow, centre);
        if (created.IsFailure)
            return created.Error;

        await cities.AddAsync(created.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return LookupEntryDto.From(created.Value);
    }

    public Task<Result<LookupEntryDto, Error>> Handle(RenameLookupCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ActAsync(request.Id, request.Kind, entry => entry.Rename(request.NameEn, request.NameAr), cancellationToken);
    }

    public Task<Result<LookupEntryDto, Error>> Handle(SetLookupActiveCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ActAsync(
            request.Id,
            request.Kind,
            entry => request.IsActive ? entry.Activate() : entry.Deactivate(),
            cancellationToken);
    }

    /// <summary>
    /// Both lookups take the same actions, so the kind only decides which repository answers.
    /// </summary>
    private async Task<Result<LookupEntryDto, Error>> ActAsync(
        Id id,
        string kind,
        Func<LookupEntry, UnitResult<Error>> act,
        CancellationToken cancellationToken)
    {
        if (string.Equals(kind, CarTypesKind, StringComparison.OrdinalIgnoreCase))
        {
            var carType = await carTypes.GetByIdAsync(id, cancellationToken);
            if (carType is null)
                return PlatformSettingsErrors.LookupNotFound;

            var outcome = act(carType);
            if (outcome.IsFailure)
                return outcome.Error;

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return LookupEntryDto.From(carType);
        }

        var city = await cities.GetByIdAsync(id, cancellationToken);
        if (city is null)
            return PlatformSettingsErrors.LookupNotFound;

        var cityOutcome = act(city);
        if (cityOutcome.IsFailure)
            return cityOutcome.Error;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return LookupEntryDto.From(city);
    }

    /// <summary>The route segment the controller uses, so the kind is never a loose string.</summary>
    public const string CarTypesKind = "car-types";

    public const string CitiesKind = "cities";
}

/// <summary>
/// The model years a car may be listed under.
///
/// The console builds its year field from this rather than from a range written into a component.
/// It used to offer twelve years ending at the current one, which quietly refused every older car
/// on the market — and the domain refused anything before 1990 regardless, from a `const`, so
/// widening the dropdown alone would have moved the rejection from the form to the server.
///
/// `Latest` is next year: a 2027 model goes on sale during 2026.
/// </summary>
public sealed record VehicleModelYearRange(int Earliest, int Latest);

public sealed record GetVehicleModelYearsQuery : IQuery<Result<VehicleModelYearRange, Error>>;

public sealed class GetVehicleModelYearsHandler(IBusinessRulesProvider businessRules, IClock clock)
    : IRequestHandler<GetVehicleModelYearsQuery, Result<VehicleModelYearRange, Error>>
{
    public async Task<Result<VehicleModelYearRange, Error>> Handle(
        GetVehicleModelYearsQuery request,
        CancellationToken cancellationToken)
    {
        var rules = await businessRules.GetAsync(cancellationToken);
        return new VehicleModelYearRange(rules.EarliestVehicleModelYear, clock.UtcNow.Year + 1);
    }
}
