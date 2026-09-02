using CSharpFunctionalExtensions;
using Khadra.Domain.Common;

namespace Khadra.Domain.PlatformSettings;

// Admin-managed global lookup data used by customer search filters (spec 3.2). Bilingual from day one
// because the customer app ships in Arabic and English.
public abstract class LookupEntry : AggregateRoot
{
    public string NameEn { get; private set; } = null!;
    public string NameAr { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public int DisplayOrder { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    protected LookupEntry()
    {
    }

    protected LookupEntry(Id id) : base(id)
    {
    }

    protected static UnitResult<Error> ValidateNames(string? nameEn, string? nameAr)
    {
        if (string.IsNullOrWhiteSpace(nameEn) || nameEn.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(nameAr) || nameAr.Trim().Length > 100)
        {
            return UnitResult.Failure(PlatformSettingsErrors.InvalidLookupName);
        }

        return UnitResult.Success<Error>();
    }

    protected void Initialise(string nameEn, string nameAr, int displayOrder, DateTimeOffset now)
    {
        NameEn = nameEn.Trim();
        NameAr = nameAr.Trim();
        DisplayOrder = displayOrder;
        IsActive = true;
        CreatedAt = now;
    }

    public UnitResult<Error> Rename(string? nameEn, string? nameAr)
    {
        var validated = ValidateNames(nameEn, nameAr);
        if (validated.IsFailure)
            return validated;

        NameEn = nameEn!.Trim();
        NameAr = nameAr!.Trim();
        return UnitResult.Success<Error>();
    }

    public void SetDisplayOrder(int displayOrder) => DisplayOrder = displayOrder;

    // Deactivating hides the entry from new searches but leaves historical bookings that reference it
    // intact, which is why lookups are never deleted.
    public UnitResult<Error> Deactivate()
    {
        if (!IsActive)
            return UnitResult.Failure(PlatformSettingsErrors.LookupAlreadyInactive);

        IsActive = false;
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Activate()
    {
        if (IsActive)
            return UnitResult.Failure(PlatformSettingsErrors.LookupAlreadyActive);

        IsActive = true;
        return UnitResult.Success<Error>();
    }
}

public sealed class CarType : LookupEntry
{
    private CarType()
    {
    }

    private CarType(Id id) : base(id)
    {
    }

    public static Result<CarType, Error> Create(string? nameEn, string? nameAr, int displayOrder, DateTimeOffset now)
    {
        var validated = ValidateNames(nameEn, nameAr);
        if (validated.IsFailure)
            return validated.Error;

        var carType = new CarType(Id.New());
        carType.Initialise(nameEn!, nameAr!, displayOrder, now);
        return carType;
    }
}

public sealed class City : LookupEntry
{
    // Optional centre point, used to bias search results and to sanity-check a dealer's map pin.
    public GeoPoint? Centre { get; private set; }

    private City()
    {
    }

    private City(Id id) : base(id)
    {
    }

    public static Result<City, Error> Create(
        string? nameEn,
        string? nameAr,
        int displayOrder,
        DateTimeOffset now,
        GeoPoint? centre = null)
    {
        var validated = ValidateNames(nameEn, nameAr);
        if (validated.IsFailure)
            return validated.Error;

        var city = new City(Id.New()) { Centre = centre };
        city.Initialise(nameEn!, nameAr!, displayOrder, now);
        return city;
    }

    public void SetCentre(GeoPoint? centre) => Centre = centre;
}
