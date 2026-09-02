using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using Khadra.Domain.Fleet.Events;

namespace Khadra.Domain.Fleet;

// A rentable car belonging to one dealer.
//
// Availability is deliberately NOT a field here. Whether a car is free on given dates is a question
// about bookings, and storing it on the vehicle would create two sources of truth that drift apart
// the first time a booking is cancelled.
public sealed class Vehicle : AggregateRoot, ISoftDeletable
{
    public const int MaxImages = 12;

    private readonly List<VehicleImage> _images = [];

    public Id DealerId { get; private set; }
    public Id CarTypeId { get; private set; }
    public VehicleDetails Details { get; private set; } = null!;
    public PlateNumber PlateNumber { get; private set; } = null!;
    public Money DailyRate { get; private set; } = null!;
    // Spec 4.3: the damage deposit can differ per vehicle class. Separate from the booking deposit.
    public Money SecurityDeposit { get; private set; } = null!;
    public bool IsDeliveryEligible { get; private set; }
    public MileagePolicy Mileage { get; private set; } = null!;
    public FuelPolicy FuelPolicy { get; private set; } = null!;
    public VehicleStatus Status { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public IReadOnlyCollection<VehicleImage> Images => _images.OrderBy(image => image.Position).ToList();

    private Vehicle()
    {
    }

    private Vehicle(Id id) : base(id)
    {
    }

    public static Result<Vehicle, Error> Add(
        Id dealerId,
        Id carTypeId,
        VehicleDetails details,
        PlateNumber plateNumber,
        Money dailyRate,
        Money securityDeposit,
        MileagePolicy mileage,
        FuelPolicy fuelPolicy,
        bool isDeliveryEligible,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(plateNumber);
        ArgumentNullException.ThrowIfNull(dailyRate);
        ArgumentNullException.ThrowIfNull(securityDeposit);
        ArgumentNullException.ThrowIfNull(mileage);
        ArgumentNullException.ThrowIfNull(fuelPolicy);
        if (dealerId.IsEmpty)
            throw new DomainException("A vehicle requires a dealer.");

        if (dailyRate.IsZero)
            return FleetErrors.RateMustBePositive;
        // Mixing currencies inside one listing would make the booking total meaningless.
        if (!string.Equals(dailyRate.CurrencyCode, securityDeposit.CurrencyCode, StringComparison.Ordinal))
            return FleetErrors.CurrencyMismatch;

        var vehicle = new Vehicle(Id.New())
        {
            DealerId = dealerId,
            CarTypeId = carTypeId,
            Details = details,
            PlateNumber = plateNumber,
            DailyRate = dailyRate,
            SecurityDeposit = securityDeposit,
            Mileage = mileage,
            FuelPolicy = fuelPolicy,
            IsDeliveryEligible = isDeliveryEligible,
            Status = VehicleStatus.Draft,
            CreatedAt = now
        };
        vehicle.AddDomainEvent(new VehicleAdded(vehicle.Id, dealerId, now));
        return vehicle;
    }

    // Whether this listing may appear in customer search and accept bookings. The dealer's approval
    // state is passed in because it belongs to another context and is never navigated to from here.
    public bool IsBookable(bool dealerCanTrade) => Status.IsBookable && dealerCanTrade && !IsDeleted;

    public UnitResult<Error> Publish(bool dealerCanTrade, DateTimeOffset now)
    {
        if (!dealerCanTrade)
            return UnitResult.Failure(FleetErrors.DealerNotApproved);
        if (Status == VehicleStatus.Active)
            return UnitResult.Failure(FleetErrors.AlreadyPublished);
        // A listing with no photo converts badly and looks fraudulent to customers.
        if (_images.Count == 0)
            return UnitResult.Failure(FleetErrors.ImageRequiredToPublish);

        Status = VehicleStatus.Active;
        AddDomainEvent(new VehiclePublished(Id, DealerId, now));
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Hide(DateTimeOffset now)
    {
        if (Status != VehicleStatus.Active)
            return UnitResult.Failure(FleetErrors.NotPublished);

        Status = VehicleStatus.Hidden;
        AddDomainEvent(new VehicleHidden(Id, DealerId, now));
        return UnitResult.Success<Error>();
    }

    // Changing the rate never affects bookings already taken: each booking keeps its own priced
    // snapshot, so an in-flight rental cannot become more expensive after the fact.
    public UnitResult<Error> ChangeDailyRate(Money newRate, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(newRate);
        if (newRate.IsZero)
            return UnitResult.Failure(FleetErrors.RateMustBePositive);
        if (!string.Equals(newRate.CurrencyCode, SecurityDeposit.CurrencyCode, StringComparison.Ordinal))
            return UnitResult.Failure(FleetErrors.CurrencyMismatch);

        var previous = DailyRate;
        DailyRate = newRate;
        AddDomainEvent(new VehicleRateChanged(Id, previous.Amount, newRate.Amount, newRate.CurrencyCode, now));
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> ChangeSecurityDeposit(Money deposit)
    {
        ArgumentNullException.ThrowIfNull(deposit);
        if (!string.Equals(deposit.CurrencyCode, DailyRate.CurrencyCode, StringComparison.Ordinal))
            return UnitResult.Failure(FleetErrors.CurrencyMismatch);

        SecurityDeposit = deposit;
        return UnitResult.Success<Error>();
    }

    public void SetDeliveryEligible(bool isEligible) => IsDeliveryEligible = isEligible;

    public void SetMileagePolicy(MileagePolicy mileage)
    {
        ArgumentNullException.ThrowIfNull(mileage);
        Mileage = mileage;
    }

    public void SetFuelPolicy(FuelPolicy fuelPolicy)
    {
        ArgumentNullException.ThrowIfNull(fuelPolicy);
        FuelPolicy = fuelPolicy;
    }

    public UnitResult<Error> UpdateDetails(VehicleDetails details, PlateNumber plateNumber)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(plateNumber);

        Details = details;
        PlateNumber = plateNumber;
        return UnitResult.Success<Error>();
    }

    public Result<VehicleImage, Error> AddImage(string storageKey, DateTimeOffset now)
    {
        if (_images.Count >= MaxImages)
            return FleetErrors.TooManyImages;

        var isFirst = _images.Count == 0;
        var image = VehicleImage.Create(Id, storageKey, _images.Count, isPrimary: isFirst, now);
        _images.Add(image);
        return image;
    }

    public UnitResult<Error> RemoveImage(Id imageId)
    {
        var image = _images.SingleOrDefault(candidate => candidate.Id == imageId);
        if (image is null)
            return UnitResult.Failure(FleetErrors.ImageNotFound);

        _images.Remove(image);
        Reindex();
        // Removing the cover photo promotes the next one so the listing never renders without an image.
        if (image.IsPrimary && _images.Count > 0)
            _images[0].SetPrimary(true);

        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> SetPrimaryImage(Id imageId)
    {
        var image = _images.SingleOrDefault(candidate => candidate.Id == imageId);
        if (image is null)
            return UnitResult.Failure(FleetErrors.ImageNotFound);

        foreach (var candidate in _images)
            candidate.SetPrimary(candidate.Id == imageId);

        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Delete(DateTimeOffset now)
    {
        if (IsDeleted)
            return UnitResult.Failure(FleetErrors.AlreadyDeleted);

        IsDeleted = true;
        DeletedAt = now;
        Status = VehicleStatus.Hidden;
        AddDomainEvent(new VehicleDeleted(Id, DealerId, now));
        return UnitResult.Success<Error>();
    }

    private void Reindex()
    {
        var position = 0;
        foreach (var image in _images.OrderBy(candidate => candidate.Position).ToList())
            image.SetPosition(position++);
    }
}
