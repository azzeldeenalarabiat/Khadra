using Khadra.Application.Common;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Dealers.Dtos;
using Khadra.Application.Fleet.Dtos;
using Khadra.Application.Fleet.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Fleet;
using Khadra.Infrastructure.Persistence;
using System.Linq.Expressions;
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
#pragma warning disable CA1304, CA1311, CA1862
            query = query.Where(vehicle =>
                EF.Functions.Like(vehicle.Details.Make.ToLower(), pattern, @"\") ||
                EF.Functions.Like(vehicle.Details.Model.ToLower(), pattern, @"\"));
#pragma warning restore CA1304, CA1311, CA1862
        }

        if (!string.IsNullOrWhiteSpace(filter.FuelType))
        {
            // As with transmission: an unknown name matches nothing, and a query string never throws.
            var fuelType = Enumeration.GetAll<FuelType>()
                .FirstOrDefault(type => string.Equals(type.Name, filter.FuelType, StringComparison.OrdinalIgnoreCase));
            if (fuelType is null)
                return PagedResult.Empty<CatalogueListing>(page.Page, page.PageSize);

            query = query.Where(vehicle => vehicle.Details.FuelType == fuelType);
        }

        if (!string.IsNullOrWhiteSpace(filter.Make))
        {
            var make = filter.Make.Trim().ToLowerInvariant();
            // See the text search above for why this is ToLower inside an expression tree.
#pragma warning disable CA1304, CA1311, CA1862
            query = query.Where(vehicle => vehicle.Details.Make.ToLower() == make);
#pragma warning restore CA1304, CA1311, CA1862
        }

        if (filter.MinYear is { } minYear)
            query = query.Where(vehicle => vehicle.Details.Year >= minYear);

        if (filter.MaxYear is { } maxYear)
            query = query.Where(vehicle => vehicle.Details.Year <= maxYear);

        if (filter.Collection is { } collection)
        {
            // Materialised first, as ListByIdsAsync does: EF translates Contains over a local List.
            var open = collection.OpenForSelfPickup.ToList();
            var delivering = collection.Delivering.ToList();
            query = query.Where(vehicle =>
                open.Contains(vehicle.DealerId) ||
                (vehicle.IsDeliveryEligible && delivering.Contains(vehicle.DealerId)));
        }

        if (filter.Window is not null)
            query = FreeDuring(query, filter.Window);

        // Counted from the SAME query the page is taken from, so every filter — the availability
        // anti-join included — applies to both. A count taken before the anti-join would page over
        // a different set than it reported.
        var totalCount = await query.CountAsync(cancellationToken);
        if (totalCount == 0)
            return PagedResult.Empty<CatalogueListing>(page.Page, page.PageSize);

        var items = await Ordered(query, filter.Sort ?? CatalogueSort.Newest)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(ToListing())
            .ToListAsync(cancellationToken);

        return new PagedResult<CatalogueListing>(items, page.Page, page.PageSize, totalCount);
    }

    public async Task<CatalogueVehicle?> GetAsync(
        Id vehicleId,
        AvailabilityWindow? window,
        Language language,
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

        // The EMBED, not the page: a car carries enough of its office to recognise it, and none of
        // what the office writes — including the sections it has hidden.
        var gallery = await LoadGalleryEmbedAsync(vehicle.DealerId, cancellationToken);
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
            ResolvedTextDto.From(vehicle.Details.Description.Resolve(language)),
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
            // The cover photo first, then the rest in the dealer's order.
            //
            // `Vehicle.Images` orders by position ALONE, and `SetPrimaryImage` sets a flag without
            // moving anything -- so the list arrives here cover-or-not, whatever position 0 happens
            // to hold. The comment that used to sit on this line claimed the opposite, and the
            // consequence was visible: the search card picks the primary (below), while the car's own
            // page opened on position 0, so a dealer who made the third photo their cover saw one
            // photograph on the card and a different one when they tapped it. The customer app now
            // opens this same list FULL SCREEN, which makes the disagreement harder to miss and no
            // more correct. Ordering it here fixes both without touching what a dealer's own console
            // shows them.
            [.. vehicle.Images
                .OrderByDescending(image => image.IsPrimary)
                .ThenBy(image => image.Position)
                .Select(image => VehicleImageDto.PublicPath + "/" + image.StorageKey)],
            gallery,
            isAvailable);
    }


    /// <summary>
    /// One car as a catalogue row, defined once.
    /// </summary>
    /// <remarks>
    /// Two callers build this shape -- the paged search and <c>ListByIdsAsync</c>, which a saved
    /// list reads through -- and a second copy would drift the first time a field was added to one
    /// of them. The rating is deliberately left null and 0 here, and this reader never fills it: the
    /// handler does, from the Reviews context's published summary (see <c>CatalogueRatings</c>). This
    /// reader's own average once counted reviews still inside their blind window.
    ///
    /// A METHOD returning the expression rather than a static field, because it closes over the
    /// DbContext; EF inlines a locally-bound expression the same way it inlines a literal one.
    /// </remarks>
    private Expression<Func<Vehicle, CatalogueListing>> ToListing() =>
        vehicle => new CatalogueListing(
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
                        : DealerProfileDto.PublicImagePath + "/" + dealer.LogoStorageKey,
                    // A placeholder: the handler writes the published rating on (CatalogueRatings).
                    null,
                    0))
                .First(),
            // Written by the search handler from the offices' hours (Wave 3 E7); an expression tree cannot omit it.
            null);

    public async Task<IReadOnlyList<CatalogueListing>> ListByIdsAsync(
        IReadOnlyCollection<Id> vehicleIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vehicleIds);
        if (vehicleIds.Count == 0) return [];

        // Materialised first: EF translates Contains over a local List, not over an
        // IReadOnlyCollection it cannot recognise as a parameter.
        var wanted = vehicleIds.ToList();

        // `Bookable()`, exactly as the search uses it. An id that is a draft, hidden, in
        // maintenance, soft-deleted, or belongs to a gallery that may not trade simply does not
        // come back -- and the caller cannot tell which, which is the point.
        return await Bookable()
            .Where(vehicle => wanted.Contains(vehicle.Id))
            .Select(ToListing())
            .ToListAsync(cancellationToken);
    }

    public async Task<PublicGalleryPage?> GetGalleryAsync(Id dealerId, Language language, CancellationToken cancellationToken = default)
    {
        var dealer = await VisibleDealerAsync(dealerId, cancellationToken);
        if (dealer is null)
            return null;

        // The office's own words, filtered by the aggregate: hidden and never-written are both simply
        // absent, and nothing here decides that a second time.
        var shown = dealer.VisiblePublicProfile(language);

        return new PublicGalleryPage(
            dealer.Id.Value,
            dealer.BusinessName.Value,
            dealer.CityId?.Value,
            dealer.Address is null ? null : new GalleryAddress(dealer.Address.Area, dealer.Address.Street),
            dealer.Location.Latitude,
            dealer.Location.Longitude,
            Branding(dealer.Id, dealer.LogoStorageKey),
            Branding(dealer.Id, dealer.CoverStorageKey),
            Schedule(dealer),
            DeliveryOf(dealer),
            // Placeholders: the handler writes the published rating on (CatalogueRatings).
            null,
            0,
            new GallerySections(
                ResolvedTextDto.From(shown.About),
                ResolvedTextDto.From(shown.RentalConditions),
                ResolvedTextDto.From(shown.Insurance),
                ResolvedTextDto.From(shown.PickupInstructions),
                ResolvedTextDto.From(shown.DeliveryNotes),
                ResolvedTextDto.From(shown.CustomerNotes)));
    }

    public async Task<CatalogueFacets> FacetsAsync(CancellationToken cancellationToken = default)
    {
        // `Bookable()`, exactly as the search uses it: a choice may only be offered for something a
        // customer could actually be shown.
        var bookable = Bookable();

        var seats = await bookable
            .Select(vehicle => vehicle.Details.Seats)
            .Distinct()
            .OrderBy(count => count)
            .ToListAsync(cancellationToken);

        // Distinct over the converted column and unwrapped afterwards: an Id's .Value inside the query
        // is an expression EF cannot translate.
        var carTypeIds = await bookable
            .Select(vehicle => vehicle.CarTypeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Makes are free text an office typed, so "Toyota" and "toyota" are one make: grouped without
        // case, shown in the spelling that appears most (then alphabetically, so it is stable).
        var makes = await bookable
            .Select(vehicle => vehicle.Details.Make)
            .ToListAsync(cancellationToken);
        var makeChoices = makes
            .Where(make => !string.IsNullOrWhiteSpace(make))
            .Select(make => make.Trim())
            .GroupBy(make => make, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .GroupBy(spelling => spelling, StringComparer.Ordinal)
                .OrderByDescending(spelling => spelling.Count())
                .ThenBy(spelling => spelling.Key, StringComparer.Ordinal)
                .First().Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var fuelTypes = await bookable
            .Select(vehicle => vehicle.Details.FuelType)
            .Distinct()
            .ToListAsync(cancellationToken);

        var years = await bookable
            .Select(vehicle => vehicle.Details.Year)
            .Distinct()
            .OrderByDescending(year => year)
            .ToListAsync(cancellationToken);

        // Per type, how many cars it lists. Grouped over `bookable` itself, so this count and the
        // total a search narrowed to the type reports are one predicate and cannot disagree.
        var typeCounts = await bookable
            .GroupBy(vehicle => vehicle.CarTypeId)
            .Select(group => new { CarTypeId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        // And one cover per type: the primary photo of its newest listed car that HAS one, in the
        // order the search calls Newest (created DESC, id DESC) so the tile shows the car its page
        // opens on. A newer car without a primary is skipped rather than blanking the tile.
        //
        // A correlated FirstOrDefault per type, in the shape ToListing() already uses for a car's
        // cover, because that shape translates on both Postgres and the SQLite the tests run on. One
        // round trip for every type, not one per type.
        var covers = await bookable
            .Select(vehicle => vehicle.CarTypeId)
            .Distinct()
            .Select(carTypeId => new
            {
                CarTypeId = carTypeId,
                Cover = bookable
                    .Where(vehicle => vehicle.CarTypeId == carTypeId && vehicle.Images.Any(image => image.IsPrimary))
                    .OrderByDescending(vehicle => vehicle.CreatedAt)
                    .ThenByDescending(vehicle => vehicle.Id)
                    .Select(vehicle => vehicle.Images
                        .Where(image => image.IsPrimary)
                        .Select(image => VehicleImageDto.PublicPath + "/" + image.StorageKey)
                        .FirstOrDefault())
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        var coverByType = covers.ToDictionary(row => row.CarTypeId.Value, row => row.Cover);

        return new CatalogueFacets(
            seats,
            [.. carTypeIds.Select(id => id.Value).Order()],
            makeChoices,
            [.. fuelTypes.OrderBy(type => type.Id).Select(type => type.Name)],
            years,
            // Ordered as CarTypeIds is, by id: deterministic, and meaningless by design.
            [.. typeCounts
                .OrderBy(row => row.CarTypeId.Value)
                .Select(row => new CarTypeFacet(
                    row.CarTypeId.Value,
                    row.Count,
                    coverByType.GetValueOrDefault(row.CarTypeId.Value)))]);
    }

    public async Task<PagedResult<PublicGalleryCard>> ListGalleriesAsync(
        GalleryDirectoryFilter filter,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        var approved = DealerVerificationStatus.Approved;
        var visible = context.Dealers
            .AsNoTracking()
            .Where(dealer => dealer.VerificationStatus == approved && !dealer.IsSuspended);
        if (filter.CityId is { } city)
            visible = visible.Where(dealer => dealer.CityId == city);
        // The office's switch alone, which is what the card's delivery badge shows. The car search's
        // DeliveryOnly also asks the CAR, and there is no car here to ask.
        if (filter.DeliveryOnly)
            visible = visible.Where(dealer => dealer.Delivery.IsEnabled);

        // The count is the search's own predicate, so the card and the office's page agree. Ordered
        // by what an office offers, then by name, then by id so the order is total.
        //
        // Ordered in memory: the business name sits behind a value converter EF cannot sort by. The
        // set is every licensed office that may trade — hundreds at the very most, three columns each —
        // so reading it whole is cheaper than a second spelling of the name in SQL would be to keep right.
        var bookable = Bookable();
        var all = await visible
            .Select(dealer => new
            {
                dealer.Id,
                dealer.BusinessName,
                Listed = bookable.Count(vehicle => vehicle.DealerId == dealer.Id),
            })
            .ToListAsync(cancellationToken);

        // The name search is in memory for the same reason the order is: the converted name does not
        // translate. And an ordinal Contains rather than a LIKE, so `%` and `_` are the characters a
        // customer typed rather than wildcards — there is no pattern language here to escape.
        var matching = all.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var term = filter.Text.Trim();
            matching = matching.Where(row => row.BusinessName.Value.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var ordered = matching
            .OrderByDescending(row => row.Listed)
            .ThenBy(row => row.BusinessName.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Id.Value)
            .ToList();

        // Counted from the FILTERED set, after the name search. A count taken in SQL before it would
        // report every office in the city and page over a handful, promising pages that come back
        // empty.
        var totalCount = ordered.Count;
        if (totalCount == 0)
            return PagedResult.Empty<PublicGalleryCard>(page.Page, page.PageSize);

        var rows = ordered
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToList();

        var ids = rows.Select(row => row.Id).ToList();
        var dealers = await context.Dealers
            .AsNoTracking()
            .Where(dealer => ids.Contains(dealer.Id))
            .ToDictionaryAsync(dealer => dealer.Id, cancellationToken);

        var cards = rows
            .Where(row => dealers.ContainsKey(row.Id))
            .Select(row =>
            {
                var dealer = dealers[row.Id];
                return new PublicGalleryCard(
                    dealer.Id.Value,
                    dealer.BusinessName.Value,
                    dealer.CityId?.Value,
                    Branding(dealer.Id, dealer.LogoStorageKey),
                    Branding(dealer.Id, dealer.CoverStorageKey),
                    DeliveryOf(dealer),
                    // Placeholders: the handler writes the published rating on (CatalogueRatings).
                    null,
                    0,
                    row.Listed);
            })
            .ToList();

        return new PagedResult<PublicGalleryCard>(cards, page.Page, page.PageSize, totalCount);
    }

    /// <summary>
    /// A search in the order the customer chose, always ending in newest listing then id.
    /// </summary>
    private static IOrderedQueryable<Vehicle> Ordered(IQueryable<Vehicle> query, CatalogueSort sort)
    {
        IOrderedQueryable<Vehicle> ordered;
        if (sort == CatalogueSort.PriceLowToHigh)
            ordered = query.OrderBy(vehicle => vehicle.DailyRate.Amount).ThenByDescending(vehicle => vehicle.CreatedAt);
        else if (sort == CatalogueSort.PriceHighToLow)
            ordered = query.OrderByDescending(vehicle => vehicle.DailyRate.Amount).ThenByDescending(vehicle => vehicle.CreatedAt);
        else if (sort == CatalogueSort.YearNewest)
            ordered = query.OrderByDescending(vehicle => vehicle.Details.Year).ThenByDescending(vehicle => vehicle.CreatedAt);
        else
            ordered = query.OrderByDescending(vehicle => vehicle.CreatedAt);

        // Id breaks the tie because nothing above is unique, and a non-total order lets a page
        // boundary drop a car or show it twice.
        return ordered.ThenByDescending(vehicle => vehicle.Id);
    }

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

    public async Task<IReadOnlyList<OfficeSchedule>> OfficeSchedulesAsync(
        Id? cityId,
        Id? dealerId,
        CancellationToken cancellationToken = default)
    {
        // The offices Bookable() admits: approved and not suspended. Hours are one stored text column nothing queries
        // into, so they are read whole and judged in memory by the handler, as the office directory already does.
        var approved = DealerVerificationStatus.Approved;
        var dealers = context.Dealers.AsNoTracking()
            .Where(dealer => dealer.VerificationStatus == approved && !dealer.IsSuspended);
        if (cityId is { } city)
            dealers = dealers.Where(dealer => dealer.CityId == city);
        if (dealerId is { } office)
            dealers = dealers.Where(dealer => dealer.Id == office);

        var rows = await dealers
            .Select(dealer => new { dealer.Id, dealer.OperatingHours, dealer.Delivery.IsEnabled })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new OfficeSchedule(row.Id, row.OperatingHours, row.IsEnabled)).ToList();
    }

    private async Task<PublicGallery?> LoadGalleryEmbedAsync(Id dealerId, CancellationToken cancellationToken)
    {
        var dealer = await VisibleDealerAsync(dealerId, cancellationToken);
        if (dealer is null)
            return null;

        return new PublicGallery(
            dealer.Id.Value,
            dealer.BusinessName.Value,
            dealer.CityId?.Value,
            dealer.Location.Latitude,
            dealer.Location.Longitude,
            Branding(dealer.Id, dealer.LogoStorageKey),
            Branding(dealer.Id, dealer.CoverStorageKey),
            Schedule(dealer),
            DeliveryOf(dealer),
            // Placeholders: the handler writes the published rating on (CatalogueRatings).
            null,
            0);
    }

    /// <summary>
    /// The gallery a customer may be shown, or null.
    /// </summary>
    /// <remarks>
    /// Loaded as an aggregate rather than projected: OperatingHours is a value object behind a
    /// converter and its Days collection is computed, and the customer page's sections come from
    /// `Dealer.VisiblePublicProfile()` — neither can be shaped in SQL. One gallery is one row, and
    /// mapping it in memory costs nothing.
    ///
    /// This is also where "may a customer see this office at all" is decided, once. The aggregate
    /// decides what of the page is shown; it does not decide whether the office may trade.
    /// </remarks>
    private async Task<Dealer?> VisibleDealerAsync(Id dealerId, CancellationToken cancellationToken)
    {
        var approved = DealerVerificationStatus.Approved;

        return await context.Dealers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == dealerId &&
                    candidate.VerificationStatus == approved &&
                    !candidate.IsSuspended,
                cancellationToken);
    }

    private static IReadOnlyList<GalleryDaySchedule> Schedule(Dealer dealer) =>
        [.. dealer.OperatingHours.Days.Select(day => new GalleryDaySchedule(
            day.Day.ToString(),
            day.IsClosed,
            day.IsClosed ? null : day.OpensAt,
            day.IsClosed ? null : day.ClosesAt))];

    private static GalleryDelivery DeliveryOf(Dealer dealer) =>
        new(dealer.Delivery.IsEnabled, dealer.Delivery.RadiusKm, MoneyDto.FromOptional(dealer.Delivery.Fee));

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
