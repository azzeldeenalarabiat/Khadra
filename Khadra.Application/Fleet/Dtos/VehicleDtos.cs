using Khadra.Domain.Fleet;

namespace Khadra.Application.Fleet.Dtos;

/// <summary>
/// A car as its dealer sees it.
///
/// `imageUrls` are public paths, unlike the signed, expiring links used for identity documents:
/// spec 7 draws that line explicitly ("never public URLs like car images"), and a marketing photo
/// behind a five-minute link would be unusable in customer search anyway.
/// </summary>
public sealed record VehicleDto(
    Guid VehicleId,
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
    MoneyDto DailyRate,
    MoneyDto SecurityDeposit,
    bool IsDeliveryEligible,
    MileagePolicyDto Mileage,
    string FuelPolicy,
    string Status,
    bool IsBookable,
    IReadOnlyList<VehicleImageDto> Images,
    DateTimeOffset CreatedAt)
{
    public static VehicleDto From(Vehicle vehicle, bool dealerCanTrade)
    {
        ArgumentNullException.ThrowIfNull(vehicle);

        return new VehicleDto(
            vehicle.Id.Value,
            vehicle.CarTypeId.Value,
            vehicle.Details.Make,
            vehicle.Details.Model,
            vehicle.Details.Year,
            vehicle.Details.Color,
            vehicle.Details.Seats,
            vehicle.Details.Transmission.Name,
            vehicle.Details.FuelType.Name,
            vehicle.Details.Description,
            vehicle.PlateNumber.Value,
            MoneyDto.From(vehicle.DailyRate),
            MoneyDto.From(vehicle.SecurityDeposit),
            vehicle.IsDeliveryEligible,
            MileagePolicyDto.From(vehicle.Mileage),
            vehicle.FuelPolicy.Name,
            vehicle.Status.Name,
            // Depends on the DEALER's approval state, which lives in another context and is passed in.
            vehicle.IsBookable(dealerCanTrade),
            [.. vehicle.Images.Select(VehicleImageDto.From)],
            vehicle.CreatedAt);
    }
}

/// <summary>Every money value carries its currency; the console never assumes JOD.</summary>
public sealed record MoneyDto(decimal Amount, string Currency)
{
    public static MoneyDto From(Domain.Common.Money money)
    {
        ArgumentNullException.ThrowIfNull(money);
        return new MoneyDto(money.Amount, money.CurrencyCode);
    }
}

public sealed record MileagePolicyDto(bool IsUnlimited, int? DailyLimitKm, MoneyDto? ExcessFeePerKm)
{
    public static MileagePolicyDto From(MileagePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new MileagePolicyDto(
            policy.IsUnlimited,
            policy.DailyLimitKm,
            policy.ExcessFeePerKm is null ? null : MoneyDto.From(policy.ExcessFeePerKm));
    }
}

public sealed record VehicleImageDto(Guid ImageId, string Url, int Position, bool IsPrimary)
{
    public const string PublicPath = "/api/v1/vehicle-images";

    public static VehicleImageDto From(VehicleImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new VehicleImageDto(image.Id.Value, $"{PublicPath}/{image.StorageKey}", image.Position, image.IsPrimary);
    }
}
