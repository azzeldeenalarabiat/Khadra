using Khadra.Application.Common;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Dealers.Dtos;
using Khadra.Application.Fleet.Dtos;
using Khadra.Application.Fleet.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Fleet;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

/// <summary>
/// The customer-facing catalogue: one LINQ statement across Fleet, Dealers, Bookings and the car-type
/// lookup, paged.
/// </summary>
/// <remarks>
/// Two things here are easy to get wrong and quiet when you do.
///
/// The gallery correlation is INNER. `BookingReader` correlates dealers with a LEFT join and a
/// fallback string, because a booking is a financial record that must render even after the gallery
/// leaves. Copying that here would put cars from suspended, unapproved and deleted galleries in
/// front of customers. Every gallery predicate is therefore a `context.Dealers.Any(...)` the vehicle
/// must satisfy.
///
/// Smart enums and computed properties are spelled out. `Vehicle.IsBookable` and `Dealer.CanTrade`
/// are C# properties EF cannot translate, and a static enumeration member cannot be read inside an
/// expression tree — hence the captured locals. The domain remains the definition; this is its
/// SQL spelling, and `CatalogueReaderTests` is what keeps the two saying the same thing.
/// </remarks>
internal sealed class CatalogueReader(KhadraDbContext context) : ICatalogueReader
{
    public async Task<PagedResult<CatalogueListing>> SearchAsync(
        CatalogueFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        var query = Bookable();

        if (filter.DealerId is { } dealerId)
            query = query.Where(vehicle => vehicle.DealerId == dealerId);

        if (filter.CityId is { } cityId)
            query = query.Where(vehicle => context.Dealers.Any(dealer => dealer.Id == vehicle.DealerId && dealer.CityId == cityId));

        if (filter.CarTypeId is { } carTypeId)
            query = query.Where(vehicle => vehicle.CarTypeId == carTypeId);

        if (filter.MinDailyRate is { } minRate)
            query = query.Where(vehicle => vehicle.DailyRate.Amount >= minRate);

        if (filter.MaxDailyRate is { } maxRate)
            query = query.Where(vehicle => vehicle.DailyRate.Amount <= maxRate);

        if (filter.MinSeats is { } minSeats)
            query = query.Where(vehicle => vehicle.Details.Seats >= minSeats);

        if (!string.IsNullOrWhiteSpace(filter.Transmission))
        {
            // An unknown name is not an error and not "everything": it matches nothing, the same way
            // a car type nobody listed matches nothing. Enumeration.FromName throws on a bad name,
            // and a query string is not a place to throw.
            var transmission = Enumeration.GetAll<TransmissionType>()
                .FirstOrDefault(type => string.Equals(type.Name, filter.Transmission, StringComparison.OrdinalIgnoreCase));
            if (transmission is null)
                return PagedResult.Empty<CatalogueListing>(page.Page, page.PageSize);

            query = query.Where(vehicle => vehicle.Details.Transmission == transmission);
        }

        if (filter.DeliveryOnly)
        {
            // Both halves: the gallery offers delivery, and this car may be delivered.
            query = query.Where(vehicle =>
                vehicle.IsDeliveryEligible &&
                context.Dealers.Any(dealer => dealer.Id == vehicle.DealerId && dealer.Delivery.IsEnabled));
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            // Escaped before it becomes a pattern, and Like + ToLower rather than ILike, for the same
            // two reasons AuditLogReader gives: `%` and `_` are LIKE wildcards a customer may
            // legitimately type, and ILike is Npgsql-only, so the search branch could not be covered
            // by the SQLite persistence tests.
            var term = filter.Text.Trim()
                .Replace(@"\", @"\\", StringComparison.Ordinal)
                .Replace("%", @"\%", StringComparison.Ordinal)
                .Replace("_", @"\_", StringComparison.Ordinal)
                .ToLowerInvariant();
            var pattern = $"%{term}%";

            // CA1304/CA1311 want a culture on ToLower. There is none to give: this is an expression
            // tree that .NET never executes — EF turns it into SQL LOWER(). ToLowerInvariant, which
            // the analyzer would accept, is precisely the call that does not translate.
#pragma warning disable CA1304, CA1311
            query = query.Where(vehicle =>
                EF.Functions.Like(vehicle.Details.Make.ToLower(), pattern, @"\") ||
                EF.Functions.Like(vehicle.Details.Model.ToLower(), pattern, @"\"));
#pragma warning restore CA1304, CA1311
        }

        if (filter.Window is not null)
            query = FreeDuring(query, filter.Window);

        // Counted from the SAME query the page is taken from, so every filter — the availability
        // anti-join included — applies to both. A count taken before the anti-join would page over
        // a different set than it reported.
        var totalCount = await query.CountAsync(cancellationToken);
        if (totalCount == 0)
            return PagedResult.Empty<CatalogueListing>(page.Page, page.PageSize);

        var items = await query
            // Newest listing first. Id breaks the tie because CreatedAt is not unique, and a
            // non-total order lets a page boundary drop a car or show it twice.
            .OrderByDescending(vehicle => vehicle.CreatedAt)
            .ThenByDescending(vehicle => vehicle.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(vehicle => new CatalogueListing(
                vehicle.Id.Value,
                vehicle.Details.Make,
                vehicle.Details.Model,
                vehicle.Details.Year,
                context.CarTypes
                    .Where(carType => carType.Id == vehicle.CarTypeId)
                    .Select(carType => new CatalogueCarType(carType.Id.Value, carType.NameEn, carType.NameAr))
                    .FirstOrDefault(),
                vehicle.Details.Transmission.Name,
                vehicle.Details.FuelType.Name,
                vehicle.Details.Seats,
                vehicle.Images
                    .Where(image => image.IsPrimary)
                    .Select(image => VehicleImageDto.PublicPath + "/" + image.StorageKey)
                    .FirstOrDefault(),
                new MoneyDto(vehicle.DailyRate.Amount, vehicle.DailyRate.CurrencyCode),
                vehicle.IsDeliveryEligible &&
                    context.Dealers.Any(dealer => dealer.Id == vehicle.DealerId && dealer.Delivery.IsEnabled),
                context.Dealers
                    .Where(dealer => dealer.Id == vehicle.DealerId)
                    .Select(dealer => new CatalogueGalleryLabel(
                        dealer.Id.Value,
                        dealer.BusinessName.Value,
                        dealer.CityId == null ? null : dealer.CityId.Value.Value,
                        dealer.LogoStorageKey == null
                            ? null
                            : DealerProfileDto.PublicImagePath + "/" + dealer.LogoStorageKey))
                    .First()))
            .ToListAsync(cancellationToken);

        return new PagedResult<CatalogueListing>(items, page.Page, page.PageSize, totalCount);
    }

    public async Task<CatalogueVehicle?> GetAsync(
        Id vehicleId,
        AvailabilityWindow? window,
        CancellationToken cancellationToken = default)
    {
        // The SAME predicate as the list. A detail endpoint that were any more permissive would let
        // anyone walk a competitor's draft and hidden inventory by guessing ids, through an endpoint
        // that needs no sign-in.
        var vehicle = await Bookable()
            .Where(candidate => candidate.Id == vehicleId)
            .Include(candidate => candidate.Images)
            .FirstOrDefaultAsync(cancellationToken);

        if (vehicle is null)
            return null;

        var gallery = await LoadGalleryAsync(vehicle.DealerId, cancellationToken);
        if (gallery is null)
            return null;

        var carType = await context.CarTypes
            .Where(type => type.Id == vehicle.CarTypeId)
            .Select(type => new CatalogueCarType(type.Id.Value, type.NameEn, type.NameAr))
            .FirstOrDefaultAsync(cancellationToken);

        bool? isAvailable = null;
        if (window is not null)
        {
            var free = await FreeDuring(Bookable().Where(candidate => candidate.Id == vehicleId), window)
                .AnyAsync(cancellationToken);
            isAvailable = free;
        }

        return new CatalogueVehicle(
            vehicle.Id.Value,
            vehicle.Details.Make,
            vehicle.Details.Model,
            vehicle.Details.Year,
            vehicle.Details.Color,
            vehicle.Details.Description,
            carType,
            vehicle.Details.Transmission.Name,
            vehicle.Details.FuelType.Name,
            vehicle.Details.Seats,
            MoneyDto.From(vehicle.DailyRate),
            MoneyDto.From(vehicle.SecurityDeposit),
            new MileagePolicyView(
                vehicle.Mileage.IsUnlimited,
                vehicle.Mileage.DailyLimitKm,
                MoneyDto.FromOptional(vehicle.Mileage.ExcessFeePerKm)),
            vehicle.FuelPolicy.Name,
            vehicle.IsDeliveryEligible,
            // Images already come back primary-first, then by position: VehicleImage.Position is what
            // SetPrimaryImage reorders.
            [.. vehicle.Images.Select(image => VehicleImageDto.PublicPath + "/" + image.StorageKey)],
            gallery,
            isAvailable);
    }

    public Task<PublicGallery?> GetGalleryAsync(Id dealerId, CancellationToken cancellationToken = default) =>
        LoadGalleryAsync(dealerId, cancellationToken);

    /// <summary>
    /// Every car a customer may be shown: listed, not deleted, and belonging to a gallery that may
    /// trade today.
    /// </summary>
    /// <remarks>
    /// This is `Vehicle.IsBookable(dealer.CanTrade)` written as SQL. Both are computed properties
    /// over smart enums, so neither translates and both are spelled out here. Soft deletion is not
    /// mentioned because the query filters on `vehicles` and `dealers` already exclude it — the one
    /// place where saying it again would be worse, since a second spelling could drift.
    /// </remarks>
    private IQueryable<Vehicle> Bookable()
    {
        var active = VehicleStatus.Active;
        var approved = DealerVerificationStatus.Approved;

        return context.Vehicles
            .Where(vehicle => vehicle.Status == active)
            .Where(vehicle => context.Dealers.Any(dealer =>
                dealer.Id == vehicle.DealerId &&
                dealer.VerificationStatus == approved &&
                !dealer.IsSuspended));
    }

    /// <summary>
    /// Narrows to the cars nothing is holding across the requested window.
    /// </summary>
    /// <remarks>
    /// The colliding-holds query is built OUTSIDE the lambda deliberately. `BookingHolds.Colliding`
    /// is an ordinary static method; called inside an expression tree EF would try to translate the
    /// call itself and fail. Hoisted, it is a captured IQueryable that composes as a subquery, and
    /// the guard and the catalogue end up asking the database the same question.
    /// </remarks>
    private IQueryable<Vehicle> FreeDuring(IQueryable<Vehicle> vehicles, AvailabilityWindow window)
    {
        var holding = BookingHolds.Colliding(
            context.Bookings, window.Now, window.HoldStart, window.Period.End);

        return vehicles.Where(vehicle => !holding.Any(booking => booking.VehicleId == vehicle.Id));
    }

    private async Task<PublicGallery?> LoadGalleryAsync(Id dealerId, CancellationToken cancellationToken)
    {
        var approved = DealerVerificationStatus.Approved;

        // Loaded as an aggregate rather than projected: OperatingHours is a value object behind a
        // converter and its Days collection is computed, so it cannot be shaped in SQL. One gallery
        // is one row, and mapping it in memory costs nothing.
        var dealer = await context.Dealers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == dealerId &&
                    candidate.VerificationStatus == approved &&
                    !candidate.IsSuspended,
                cancellationToken);

        if (dealer is null)
            return null;

        return new PublicGallery(
            dealer.Id.Value,
            dealer.BusinessName.Value,
            dealer.Description,
            dealer.CityId?.Value,
            dealer.Location.Latitude,
            dealer.Location.Longitude,
            Branding(dealer.Id, dealer.LogoStorageKey),
            Branding(dealer.Id, dealer.CoverStorageKey),
            [.. dealer.OperatingHours.Days.Select(day => new GalleryDaySchedule(
                day.Day.ToString(),
                day.IsClosed,
                day.IsClosed ? null : day.OpensAt,
                day.IsClosed ? null : day.ClosesAt))],
            new GalleryDelivery(
                dealer.Delivery.IsEnabled,
                dealer.Delivery.RadiusKm,
                MoneyDto.FromOptional(dealer.Delivery.Fee)),
            // Reviews has no persistence yet (pre-launch checklist item 3). Null is the truth.
            AverageRating: null,
            ReviewCount: 0);
    }

    /// <summary>
    /// The public URL for a gallery logo or cover.
    /// </summary>
    /// <remarks>
    /// The storage key ALREADY carries its scope — `dealer-branding/{dealerId}/logo-….png` — so the
    /// path is the serving prefix and the key, nothing between them. Building it from the dealer id
    /// a second time produces a URL with the scope in it twice, which 404s. Sharing
    /// DealerProfileDto's constant rather than declaring a second one is what stops the two drifting.
    /// </remarks>
    private static string? Branding(Id dealerId, string? storageKey) =>
        storageKey is null ? null : $"{DealerProfileDto.PublicImagePath}/{storageKey}";
}
