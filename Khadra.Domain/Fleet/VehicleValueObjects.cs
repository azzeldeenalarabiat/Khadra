using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.Fleet;

// Jordanian plate. Only the digits are kept, because the platform's real guarantee that this is a
// licensed green-plate tourist vehicle is the Admin's review of the dealer's vehicle registration
// document (spec 3.1), not a string pattern.
public sealed class PlateNumber : ValueObject
{
    public const int MinDigits = 4;
    public const int MaxDigits = 10;

    public string Value { get; }

    private PlateNumber(string value)
    {
        Value = value;
    }

    public static Result<PlateNumber, Error> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return FleetErrors.InvalidPlateNumber;

        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length is < MinDigits or > MaxDigits)
            return FleetErrors.InvalidPlateNumber;

        return new PlateNumber(digits);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}

public sealed class VehicleDetails : ValueObject
{
    public const int EarliestModelYear = 1990;

    public const int MaxDescriptionLength = 2000;

    public string Make { get; }
    public string Model { get; }
    public int Year { get; }
    public string? Color { get; }
    public int Seats { get; }
    public TransmissionType Transmission { get; }
    public FuelType FuelType { get; }
    // Spec 4.3: the dealer's own words about this car, shown on the listing.
    public string? Description { get; }

    private VehicleDetails(
        string make,
        string model,
        int year,
        string? color,
        int seats,
        TransmissionType transmission,
        FuelType fuelType,
        string? description)
    {
        Make = make;
        Model = model;
        Year = year;
        Color = color;
        Seats = seats;
        Transmission = transmission;
        FuelType = fuelType;
        Description = description;
    }

    // `currentYear` is passed in rather than read from the clock so the rule stays testable and the
    // domain stays free of ambient time.
    public static Result<VehicleDetails, Error> Create(
        string? make,
        string? model,
        int year,
        int seats,
        TransmissionType transmission,
        FuelType fuelType,
        int currentYear,
        string? color = null,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(transmission);
        ArgumentNullException.ThrowIfNull(fuelType);

        if (string.IsNullOrWhiteSpace(make) || make.Trim().Length > 60 ||
            string.IsNullOrWhiteSpace(model) || model.Trim().Length > 60)
        {
            return FleetErrors.InvalidMakeOrModel;
        }

        // Next year's models go on sale during the current year, so allow one year ahead.
        if (year < EarliestModelYear || year > currentYear + 1)
            return FleetErrors.InvalidYear;

        if (seats is < 1 or > 20)
            return FleetErrors.InvalidSeats;

        if (description is not null && description.Trim().Length > MaxDescriptionLength)
            return FleetErrors.DescriptionTooLong;

        return new VehicleDetails(
            make.Trim(),
            model.Trim(),
            year,
            string.IsNullOrWhiteSpace(color) ? null : color.Trim(),
            seats,
            transmission,
            fuelType,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Make;
        yield return Model;
        yield return Year;
        yield return Color;
        yield return Seats;
        yield return Transmission;
        yield return FuelType;
        yield return Description;
    }

    public override string ToString() => $"{Year} {Make} {Model}";
}

// Spec 4.3: per-car mileage policy. Either unlimited, or a daily cap with a per-kilometre excess fee.
public sealed class MileagePolicy : ValueObject
{
    public bool IsUnlimited { get; }
    public int? DailyLimitKm { get; }
    public Money? ExcessFeePerKm { get; }

#pragma warning disable CS8618 // EF materialises this value object by writing its backing fields;
    // the public factories remain the only way application code can create one.
    private MileagePolicy()
    {
    }
#pragma warning restore CS8618

    private MileagePolicy(bool isUnlimited, int? dailyLimitKm, Money? excessFeePerKm)
    {
        IsUnlimited = isUnlimited;
        DailyLimitKm = dailyLimitKm;
        ExcessFeePerKm = excessFeePerKm;
    }

    public static MileagePolicy Unlimited() => new(true, null, null);

    public static Result<MileagePolicy, Error> Limited(int dailyLimitKm, Money excessFeePerKm)
    {
        ArgumentNullException.ThrowIfNull(excessFeePerKm);
        if (dailyLimitKm <= 0)
            return FleetErrors.InvalidMileagePolicy;

        return new MileagePolicy(false, dailyLimitKm, excessFeePerKm);
    }

    // Excess is computed against the whole-rental allowance, not per day, so a customer who drives far
    // on one day and barely at all on another is not penalised.
    public Money ExcessChargeFor(int kilometresDriven, int rentalDays)
    {
        if (kilometresDriven < 0)
            throw new DomainException("Kilometres driven cannot be negative.");
        if (rentalDays <= 0)
            throw new DomainException("A rental must cover at least one day.");
        if (IsUnlimited)
            return Money.Jod(0m);

        var excessFee = ExcessFeePerKm ?? throw new DomainException("A limited mileage policy must carry an excess fee.");
        var allowance = DailyLimitKm!.Value * rentalDays;
        var excess = kilometresDriven - allowance;
        return excess <= 0 ? Money.ZeroIn(excessFee.CurrencyCode) : excessFee.MultiplyBy(excess);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return IsUnlimited;
        yield return DailyLimitKm;
        yield return ExcessFeePerKm;
    }
}
