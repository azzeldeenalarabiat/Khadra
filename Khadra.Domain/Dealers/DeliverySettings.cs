using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.Dealers;

// Spec 4.4: delivery is dealer-controlled (on/off plus a radius), but the FEE is platform-wide and
// therefore lives in business-rule settings, never here.
public sealed class DeliverySettings : ValueObject
{
    public const decimal MaxRadiusKm = 200m;

    public static readonly DeliverySettings Disabled = new(false, 0m);

    public bool IsEnabled { get; }
    public decimal RadiusKm { get; }

    private DeliverySettings(bool isEnabled, decimal radiusKm)
    {
        IsEnabled = isEnabled;
        RadiusKm = radiusKm;
    }

    public static Result<DeliverySettings, Error> Enabled(decimal radiusKm)
    {
        if (radiusKm <= 0m || radiusKm > MaxRadiusKm)
            return DealerErrors.InvalidDeliveryRadius;

        return new DeliverySettings(true, decimal.Round(radiusKm, 2, MidpointRounding.ToEven));
    }

    public bool Covers(GeoPoint dealerLocation, GeoPoint destination)
    {
        ArgumentNullException.ThrowIfNull(dealerLocation);
        ArgumentNullException.ThrowIfNull(destination);

        return IsEnabled && (decimal)dealerLocation.DistanceKmTo(destination) <= RadiusKm;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return IsEnabled;
        yield return RadiusKm;
    }
}
