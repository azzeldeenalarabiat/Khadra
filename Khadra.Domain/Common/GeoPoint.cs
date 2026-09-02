using CSharpFunctionalExtensions;

namespace Khadra.Domain.Common;

// WGS84 coordinate. Used for dealer locations and delivery pins; distance drives the delivery-radius rule.
public sealed class GeoPoint : ValueObject
{
    private const double EarthRadiusKm = 6371.0088;

    public double Latitude { get; }
    public double Longitude { get; }

    private GeoPoint(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public static Result<GeoPoint, Error> Create(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || latitude is < -90 or > 90)
            return Error.Validation("geo.invalid_latitude", "Latitude must be between -90 and 90.");
        if (double.IsNaN(longitude) || longitude is < -180 or > 180)
            return Error.Validation("geo.invalid_longitude", "Longitude must be between -180 and 180.");

        return new GeoPoint(latitude, longitude);
    }

    // Haversine great-circle distance.
    public double DistanceKmTo(GeoPoint other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var latitude1 = DegreesToRadians(Latitude);
        var latitude2 = DegreesToRadians(other.Latitude);
        var deltaLatitude = DegreesToRadians(other.Latitude - Latitude);
        var deltaLongitude = DegreesToRadians(other.Longitude - Longitude);

        var a = Math.Sin(deltaLatitude / 2) * Math.Sin(deltaLatitude / 2) +
                Math.Cos(latitude1) * Math.Cos(latitude2) *
                Math.Sin(deltaLongitude / 2) * Math.Sin(deltaLongitude / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Latitude;
        yield return Longitude;
    }

    public override string ToString() => $"{Latitude:0.######},{Longitude:0.######}";
}
