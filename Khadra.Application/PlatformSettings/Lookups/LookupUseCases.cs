using Khadra.Domain.Auditing;
using Khadra.Application.Auditing;
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

    /// <summary>Either kind, when the caller has the base type in hand.</summary>
    public static LookupEntryDto From(LookupEntry entry) => entry switch
    {
        CarType carType => From(carType),
        City city => From(city),
        _ => throw new ArgumentOutOfRangeException(nameof(entry), entry?.GetType().Name, "Unknown lookup kind."),
    };

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
    AdminActionRecorder audit,
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

        if (await NameIsTakenAsync(CarTypesKind, request.NameEn, request.NameAr, exceptId: null, cancellationToken))
            return PlatformSettingsErrors.LookupNameTaken;

        var created = CarType.Create(request.NameEn, request.NameAr, request.DisplayOrder, clock.UtcNow);
        if (created.IsFailure)
            return created.Error;

        await carTypes.AddAsync(created.Value, cancellationToken);
        RecordLookup(AuditAction.LookupCreated, CarTypesKind, created.Value, previous: null, updated: Describe(created.Value));
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

        if (await NameIsTakenAsync(CitiesKind, request.NameEn, request.NameAr, exceptId: null, cancellationToken))
            return PlatformSettingsErrors.LookupNameTaken;

        var created = City.Create(request.NameEn, request.NameAr, request.DisplayOrder, clock.UtcNow, centre);
        if (created.IsFailure)
            return created.Error;

        await cities.AddAsync(created.Value, cancellationToken);
        RecordLookup(AuditAction.LookupCreated, CitiesKind, created.Value, previous: null, updated: Describe(created.Value));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return LookupEntryDto.From(created.Value);
    }

    public async Task<Result<LookupEntryDto, Error>> Handle(RenameLookupCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await NameIsTakenAsync(request.Kind, request.NameEn, request.NameAr, request.Id, cancellationToken))
            return PlatformSettingsErrors.LookupNameTaken;

        return await ActAsync(
            request.Id, request.Kind, AuditAction.LookupRenamed,
            entry => entry.Rename(request.NameEn, request.NameAr), cancellationToken);
    }

    public async Task<Result<LookupEntryDto, Error>> Handle(SetLookupActiveCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Bringing an entry back is the third way a name can enter the offered set, alongside
        // creating and renaming. Retiring one cannot collide with anything, so it is not checked.
        if (request.IsActive)
        {
            var entry = await FindAsync(request.Kind, request.Id, cancellationToken);
            if (entry is not null &&
                await NameIsTakenAsync(request.Kind, entry.NameEn, entry.NameAr, request.Id, cancellationToken))
            {
                return PlatformSettingsErrors.LookupNameTaken;
            }
        }

        return await ActAsync(
            request.Id,
            request.Kind,
            request.IsActive ? AuditAction.LookupRestored : AuditAction.LookupRetired,
            entry => request.IsActive ? entry.Activate() : entry.Deactivate(),
            cancellationToken);
    }

    private async Task<LookupEntry?> FindAsync(string kind, Id id, CancellationToken cancellationToken) =>
        string.Equals(kind, CarTypesKind, StringComparison.OrdinalIgnoreCase)
            ? await carTypes.GetByIdAsync(id, cancellationToken)
            : await cities.GetByIdAsync(id, cancellationToken);

    /// <summary>
    /// Whether another OFFERED entry on the same list already reads the same, in either language.
    /// </summary>
    /// <remarks>
    /// Offered only. Retiring is the nearest thing to a delete this list has, so reserving every
    /// name a retired entry ever held would make one typo unusable for ever, and a retired entry is
    /// in no dropdown to be confused with.
    /// </remarks>
    private async Task<bool> NameIsTakenAsync(
        string kind,
        string nameEn,
        string nameAr,
        Id? exceptId,
        CancellationToken cancellationToken)
    {
        var offered = string.Equals(kind, CarTypesKind, StringComparison.OrdinalIgnoreCase)
            ? (IReadOnlyList<LookupEntry>)await carTypes.ListAsync(activeOnly: true, cancellationToken)
            : await cities.ListAsync(activeOnly: true, cancellationToken);

        var wantedEn = LookupEntry.ComparisonKey(nameEn);
        var wantedAr = LookupEntry.ComparisonKey(nameAr);
        return offered.Any(entry =>
            entry.Id != exceptId &&
            (LookupEntry.ComparisonKey(entry.NameEn) == wantedEn ||
             LookupEntry.ComparisonKey(entry.NameAr) == wantedAr));
    }

    /// <summary>
    /// Both lookups take the same actions, so the kind only decides which repository answers.
    /// </summary>
    private async Task<Result<LookupEntryDto, Error>> ActAsync(
        Id id,
        string kind,
        AuditAction action,
        Func<LookupEntry, UnitResult<Error>> act,
        CancellationToken cancellationToken)
    {
        var isCarType = string.Equals(kind, CarTypesKind, StringComparison.OrdinalIgnoreCase);
        LookupEntry? entry = isCarType
            ? await carTypes.GetByIdAsync(id, cancellationToken)
            : await cities.GetByIdAsync(id, cancellationToken);
        if (entry is null)
            return PlatformSettingsErrors.LookupNotFound;

        // Read before the change: a line that cannot say what the entry WAS is half a record.
        var previous = Describe(entry);

        var outcome = act(entry);
        if (outcome.IsFailure)
            return outcome.Error;

        RecordLookup(action, kind, entry, previous, Describe(entry));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return LookupEntryDto.From(entry);
    }

    /// <summary>What the entry read as, for the before/after columns on the audit screen.</summary>
    private static string Describe(LookupEntry entry) =>
        $"{entry.NameEn} / {entry.NameAr} · {(entry.IsActive ? "Offered" : "Retired")}";

    private void RecordLookup(
        AuditAction action,
        string kind,
        LookupEntry entry,
        string? previous,
        string? updated) =>
        audit.Record(
            action,
            string.Equals(kind, CarTypesKind, StringComparison.OrdinalIgnoreCase)
                ? AuditEntityType.CarType
                : AuditEntityType.City,
            entry.Id,
            entry.NameEn,
            previous,
            updated);

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
