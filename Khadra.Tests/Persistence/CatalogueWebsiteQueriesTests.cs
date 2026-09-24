using Khadra.Application.Common;
using Khadra.Application.Fleet.BrowseCatalogue;
using Khadra.Application.Fleet.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Fleet;
using Khadra.Domain.PlatformSettings;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// What the customer website added to the catalogue (2026-09-23): an order the customer chooses, three
/// more filters, the facets those filters are built from, and a directory of rental offices — all on
/// the real EF model, and all through the same visibility predicate the search already had.
/// </summary>
public sealed class CatalogueWebsiteQueriesTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;
    private readonly City _amman = City.Create("Amman", "عمّان", 1, Build.Now).Value;
    private readonly City _aqaba = City.Create("Aqaba", "العقبة", 2, Build.Now).Value;
    private int _plate;
    private int _registration = 700000;

    public CatalogueWebsiteQueriesTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new KhadraDbContext(_options);
        context.Database.EnsureCreated();
        context.Cities.AddRange(_amman, _aqaba);
        context.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private KhadraDbContext NewContext() => new(_options);

    private static PageRequest Page => PageRequest.From(1, 50);

    private Dealer Office(string name, City? city = null)
    {
        var dealer = Build.ApprovedDealer(businessName: name, commercialRegistration: (++_registration).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (city is not null)
        {
            Assert.True(dealer.UpdateProfile(
                dealer.BusinessName, dealer.Location, dealer.OperatingHours, city.Id,
                DealerAddress.Create("Centre", null).Value).IsSuccess);
        }
        dealer.ClearDomainEvents();
        return dealer;
    }

    private Vehicle Car(
        Id dealerId,
        string make = "Toyota",
        int year = 2024,
        decimal rate = 30m,
        FuelType? fuel = null,
        int listedMinutesAgo = 0,
        Id? carTypeId = null,
        string image = "cars/front.jpg")
    {
        var listedAt = Build.Now.AddMinutes(-listedMinutesAgo);
        var vehicle = Vehicle.Add(
            dealerId,
            carTypeId ?? Id.New(),
            VehicleDetails.Create(make, "Model", year, 5, TransmissionType.Automatic, fuel ?? FuelType.Petrol, currentYear: 2026).Value,
            PlateNumber.Create($"55-{10000 + ++_plate}").Value,
            Money.Jod(rate),
            Money.Jod(200m),
            MileagePolicy.Unlimited(),
            FuelPolicy.FullToFull,
            true,
            listedAt).Value;
        vehicle.AddImage(image, listedAt);
        vehicle.Publish(dealerCanTrade: true, listedAt);
        vehicle.ClearDomainEvents();
        return vehicle;
    }

    private async Task Save(IEnumerable<Dealer> dealers, IEnumerable<Vehicle> vehicles)
    {
        await using var context = NewContext();
        context.Dealers.AddRange(dealers);
        context.Vehicles.AddRange(vehicles);
        await context.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<Guid>> Search(CatalogueFilter filter)
    {
        await using var context = NewContext();
        return [.. (await new CatalogueReader(context).SearchAsync(filter, Page)).Items.Select(item => item.VehicleId)];
    }

    [Fact]
    public async Task Each_order_the_customer_can_choose_is_the_order_they_get()
    {
        var office = Office("Order Rentals");
        var cheapOld = Car(office.Id, year: 2019, rate: 20m, listedMinutesAgo: 30);
        var dearNew = Car(office.Id, year: 2025, rate: 90m, listedMinutesAgo: 20);
        var middle = Car(office.Id, year: 2022, rate: 45m, listedMinutesAgo: 10);
        await Save([office], [cheapOld, dearNew, middle]);

        Assert.Equal([middle.Id.Value, dearNew.Id.Value, cheapOld.Id.Value], await Search(new CatalogueFilter()));
        Assert.Equal([middle.Id.Value, dearNew.Id.Value, cheapOld.Id.Value], await Search(new CatalogueFilter(Sort: CatalogueSort.Newest)));
        Assert.Equal([cheapOld.Id.Value, middle.Id.Value, dearNew.Id.Value], await Search(new CatalogueFilter(Sort: CatalogueSort.PriceLowToHigh)));
        Assert.Equal([dearNew.Id.Value, middle.Id.Value, cheapOld.Id.Value], await Search(new CatalogueFilter(Sort: CatalogueSort.PriceHighToLow)));
        Assert.Equal([dearNew.Id.Value, middle.Id.Value, cheapOld.Id.Value], await Search(new CatalogueFilter(Sort: CatalogueSort.YearNewest)));
    }

    [Fact]
    public async Task Cars_at_the_same_price_keep_one_order_so_a_page_boundary_cannot_lose_one()
    {
        var office = Office("Tie Rentals");
        var cars = Enumerable.Range(0, 5).Select(_ => Car(office.Id, rate: 30m)).ToList();
        await Save([office], cars);

        var first = await Search(new CatalogueFilter(Sort: CatalogueSort.PriceLowToHigh));
        var second = await Search(new CatalogueFilter(Sort: CatalogueSort.PriceLowToHigh));

        Assert.Equal(first, second);
        Assert.Equal(5, first.Distinct().Count());
    }

    [Fact]
    public async Task Fuel_make_and_year_narrow_the_search_and_an_unknown_fuel_matches_nothing()
    {
        var office = Office("Filter Rentals");
        var electricKia = Car(office.Id, make: "Kia", year: 2023, fuel: FuelType.Electric);
        var petrolToyota = Car(office.Id, make: "Toyota", year: 2020);
        var hybridToyota = Car(office.Id, make: "toyota", year: 2025, fuel: FuelType.Hybrid);
        await Save([office], [electricKia, petrolToyota, hybridToyota]);

        Assert.Equal([electricKia.Id.Value], await Search(new CatalogueFilter(FuelType: "electric")));
        Assert.Empty(await Search(new CatalogueFilter(FuelType: "Plutonium")));

        // A make is matched whole and without case, so the two spellings of Toyota are one make.
        Assert.Equal(
            new[] { petrolToyota.Id.Value, hybridToyota.Id.Value }.Order(),
            (await Search(new CatalogueFilter(Make: "TOYOTA"))).Order());
        Assert.Empty(await Search(new CatalogueFilter(Make: "Toy")));

        Assert.Equal(
            new[] { electricKia.Id.Value, hybridToyota.Id.Value }.Order(),
            (await Search(new CatalogueFilter(MinYear: 2021))).Order());
        Assert.Equal([petrolToyota.Id.Value], await Search(new CatalogueFilter(MaxYear: 2021)));
        Assert.Equal([electricKia.Id.Value], await Search(new CatalogueFilter(MinYear: 2023, MaxYear: 2023)));
    }

    [Fact]
    public async Task Facets_name_only_the_makes_fuels_and_years_a_customer_could_be_shown()
    {
        var office = Office("Facet Rentals");
        var suspended = Office("Suspended Rentals");
        suspended.Suspend(Id.New(), "Complaints.", Build.Now);
        var hidden = Car(office.Id, make: "Bentley", year: 2026, fuel: FuelType.Diesel);
        hidden.Hide(Build.Now);

        await Save(
            [office, suspended],
            [
                Car(office.Id, make: "Toyota", year: 2021),
                Car(office.Id, make: "Toyota", year: 2021),
                Car(office.Id, make: "toyota", year: 2024, fuel: FuelType.Hybrid),
                Car(office.Id, make: "Kia", year: 2023, fuel: FuelType.Electric),
                Car(suspended.Id, make: "Lada", year: 2010, fuel: FuelType.Diesel),
                hidden,
            ]);

        await using var context = NewContext();
        var facets = await new CatalogueReader(context).FacetsAsync();

        // One Toyota, in the spelling most offices used; nothing from a hidden car or a suspended office.
        Assert.Equal(["Kia", "Toyota"], facets.Makes);
        Assert.Equal(["Petrol", "Hybrid", "Electric"], facets.FuelTypes);
        Assert.Equal([2024, 2023, 2021], facets.Years);
    }

    [Fact]
    public async Task The_directory_lists_offices_that_may_trade_with_the_count_their_page_would_show()
    {
        var busy = Office("Busy Rentals", _amman);
        var quiet = Office("Quiet Rentals", _aqaba);
        var empty = Office("Empty Rentals", _amman);
        var pending = Build.Dealer(businessName: "Pending Rentals", commercialRegistration: "799999");
        var suspended = Office("Suspended Rentals", _amman);
        suspended.Suspend(Id.New(), "Complaints.", Build.Now);

        var hiddenCar = Car(busy.Id);
        hiddenCar.Hide(Build.Now);
        await Save(
            [busy, quiet, empty, pending, suspended],
            [Car(busy.Id), Car(busy.Id), hiddenCar, Car(quiet.Id), Car(suspended.Id)]);

        await using var context = NewContext();
        var reader = new CatalogueReader(context);
        var all = await reader.ListGalleriesAsync(new GalleryDirectoryFilter(), Page);

        // Most cars first, then by name; a hidden car is not counted, and only offices that may trade appear.
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(["Busy Rentals", "Quiet Rentals", "Empty Rentals"], all.Items.Select(card => card.BusinessName));
        Assert.Equal([2, 1, 0], all.Items.Select(card => card.ListedVehicleCount));

        // The count is exactly what the office's own car list holds.
        var busyCars = await reader.SearchAsync(new CatalogueFilter(DealerId: busy.Id), Page);
        Assert.Equal(busyCars.TotalCount, all.Items[0].ListedVehicleCount);

        var inAmman = await reader.ListGalleriesAsync(new GalleryDirectoryFilter(CityId: _amman.Id), Page);
        Assert.Equal(["Busy Rentals", "Empty Rentals"], inAmman.Items.Select(card => card.BusinessName));
        Assert.All(inAmman.Items, card => Assert.Equal(_amman.Id.Value, card.CityId));
    }

    [Fact]
    public async Task An_office_nobody_has_rated_says_so_rather_than_scoring_zero()
    {
        var office = Office("Unrated Rentals");
        await Save([office], [Car(office.Id)]);

        // Through the handler, which is where the rating is composed now: the reader alone always says
        // null (see CatalogueRatings), so asking it would prove nothing.
        await using var context = NewContext();
        var result = await new ListPublicGalleriesHandler(
                new CatalogueReader(context), new GalleryReviewReader(context), new TestClock(Build.Now))
            .Handle(new ListPublicGalleriesQuery(null, Page), CancellationToken.None);
        var card = result.Value.Items.Single();

        Assert.Null(card.AverageRating);
        Assert.Equal(0, card.ReviewCount);
    }

    // ── The office directory's name search and delivery filter (2026-09-24) ─────────────────────────

    private async Task<PagedResult<PublicGalleryCard>> Directory(GalleryDirectoryFilter filter, PageRequest? page = null)
    {
        await using var context = NewContext();
        return await new CatalogueReader(context).ListGalleriesAsync(filter, page ?? Page);
    }

    private static IReadOnlyList<string> Names(PagedResult<PublicGalleryCard> page) =>
        [.. page.Items.Select(card => card.BusinessName)];

    [Fact]
    public async Task The_name_search_matches_part_of_a_name_without_case_and_takes_wildcards_literally()
    {
        await Save(
            [Office("Petra Rentals"), Office("PETRA Cars"), Office("Wadi Rum 100% Cars"), Office("Aqaba_Drive"), Office("Amman Rent")],
            []);

        Assert.Equal(["PETRA Cars", "Petra Rentals"], Names(await Directory(new GalleryDirectoryFilter(Text: "petra"))));
        Assert.Equal(["PETRA Cars", "Petra Rentals"], Names(await Directory(new GalleryDirectoryFilter(Text: "  eTr  "))));

        // `%` and `_` are characters an office's name may hold, not patterns that match everything.
        Assert.Equal(["Wadi Rum 100% Cars"], Names(await Directory(new GalleryDirectoryFilter(Text: "%"))));
        Assert.Equal(["Aqaba_Drive"], Names(await Directory(new GalleryDirectoryFilter(Text: "_"))));
        Assert.Empty((await Directory(new GalleryDirectoryFilter(Text: "Zarqa"))).Items);

        // Blank is no filter at all, not a search for spaces.
        var blank = await Directory(new GalleryDirectoryFilter(Text: "   "));
        Assert.Equal(5, blank.TotalCount);
        Assert.Equal(5, blank.Items.Count);
    }

    [Fact]
    public async Task The_total_counts_the_offices_the_name_matched_so_the_pages_add_up()
    {
        var matches = Enumerable.Range(1, 5).Select(n => Office($"Match Rentals {n}")).ToList();
        var others = Enumerable.Range(1, 3).Select(n => Office($"Other Office {n}")).ToList();
        // Cars on two of the matches, so the order is by listed count first and by name after it.
        await Save(
            [.. matches, .. others],
            [Car(matches[3].Id), Car(matches[3].Id), Car(matches[1].Id), Car(others[0].Id), Car(others[0].Id), Car(others[0].Id)]);

        var filter = new GalleryDirectoryFilter(Text: "match");
        var pages = new[]
        {
            await Directory(filter, PageRequest.From(1, 2)),
            await Directory(filter, PageRequest.From(2, 2)),
            await Directory(filter, PageRequest.From(3, 2)),
        };

        // Every page reports the SAME total: the five that matched, not the eight in the directory.
        Assert.All(pages, page => Assert.Equal(5, page.TotalCount));
        Assert.All(pages, page => Assert.Equal(3, page.TotalPages));
        Assert.Equal([2, 2, 1], pages.Select(page => page.Items.Count));

        // And the pages, read in order, are the matches in the directory's usual order, each once.
        Assert.Equal(
            ["Match Rentals 4", "Match Rentals 2", "Match Rentals 1", "Match Rentals 3", "Match Rentals 5"],
            pages.SelectMany(page => Names(page)));
    }

    [Fact]
    public async Task Delivery_only_keeps_the_offices_with_delivery_switched_on_and_composes_with_city_and_name()
    {
        var ammanDelivers = Office("Amman Express", _amman);
        Assert.True(ammanDelivers.EnableDelivery(30m, Money.Jod(10m), Build.Now).IsSuccess);
        var ammanCollect = Office("Amman Collect", _amman);
        var aqabaDelivers = Office("Aqaba Express", _aqaba);
        Assert.True(aqabaDelivers.EnableDelivery(20m, Money.Jod(5m), Build.Now).IsSuccess);
        ammanDelivers.ClearDomainEvents();
        aqabaDelivers.ClearDomainEvents();
        // A car that is not delivery-eligible does not matter here: this is the office's switch.
        await Save([ammanDelivers, ammanCollect, aqabaDelivers], [Car(ammanDelivers.Id), Car(ammanCollect.Id)]);

        var delivering = await Directory(new GalleryDirectoryFilter(DeliveryOnly: true));
        Assert.Equal(2, delivering.TotalCount);
        Assert.Equal(["Amman Express", "Aqaba Express"], Names(delivering));
        // Exactly what the card shows, so a filtered list never holds a card saying it does not deliver.
        Assert.All(delivering.Items, card => Assert.True(card.Delivery.IsEnabled));

        Assert.Equal(
            ["Amman Express"],
            Names(await Directory(new GalleryDirectoryFilter(CityId: _amman.Id, DeliveryOnly: true))));
        Assert.Equal(
            ["Aqaba Express"],
            Names(await Directory(new GalleryDirectoryFilter(Text: "express", DeliveryOnly: true, CityId: _aqaba.Id))));

        // Off means everything, as before the filter existed.
        Assert.Equal(3, (await Directory(new GalleryDirectoryFilter(DeliveryOnly: false))).TotalCount);
    }

    // ── Car-type tiles in the facets (2026-09-24) ─────────────────────────────────────────────────

    /// <summary>Strips every photo's cover flag — a row state the domain never produces, but a
    /// reader must not assume the domain is the only writer.</summary>
    private async Task StripCover(Vehicle vehicle)
    {
        await using var context = NewContext();
        await context.Set<VehicleImage>()
            .Where(image => image.VehicleId == vehicle.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(image => image.IsPrimary, false));
    }

    private async Task<CatalogueFacets> Facets()
    {
        await using var context = NewContext();
        return await new CatalogueReader(context).FacetsAsync();
    }

    [Fact]
    public async Task Each_car_type_counts_what_a_search_for_it_would_total_and_nothing_a_customer_cannot_see()
    {
        var office = Office("Type Rentals");
        var suspended = Office("Suspended Types");
        suspended.Suspend(Id.New(), "Complaints.", Build.Now);
        var sedan = Id.New();
        var suv = Id.New();
        var onlyOnSuspended = Id.New();
        var hidden = Car(office.Id, carTypeId: sedan);
        hidden.Hide(Build.Now);

        await Save(
            [office, suspended],
            [
                Car(office.Id, carTypeId: sedan),
                Car(office.Id, carTypeId: sedan),
                hidden,
                Car(suspended.Id, carTypeId: sedan),
                Car(office.Id, carTypeId: suv),
                Car(suspended.Id, carTypeId: onlyOnSuspended),
            ]);

        var facets = await Facets();

        // The same types as CarTypeIds, in the same (meaningless, deterministic) order.
        Assert.Equal(facets.CarTypeIds, facets.CarTypes.Select(type => type.CarTypeId));
        Assert.DoesNotContain(onlyOnSuspended.Value, facets.CarTypes.Select(type => type.CarTypeId));
        Assert.Equal(facets.CarTypes.Select(type => type.CarTypeId).Order(), facets.CarTypes.Select(type => type.CarTypeId));

        // A hidden car and a suspended office's car add nothing: the tile says what its page will list.
        Assert.Equal(2, facets.CarTypes.Single(type => type.CarTypeId == sedan.Value).ListedVehicleCount);
        Assert.Equal(1, facets.CarTypes.Single(type => type.CarTypeId == suv.Value).ListedVehicleCount);
        foreach (var type in facets.CarTypes)
        {
            await using var context = NewContext();
            var search = await new CatalogueReader(context).SearchAsync(
                new CatalogueFilter(CarTypeId: Id.From(type.CarTypeId)), Page);
            Assert.Equal(search.TotalCount, type.ListedVehicleCount);
        }

        // Asked twice, answered the same way.
        Assert.Equal(facets.CarTypes, (await Facets()).CarTypes);
    }

    [Fact]
    public async Task A_car_types_cover_is_the_newest_listed_car_that_has_a_cover_photo()
    {
        var office = Office("Cover Rentals");
        var sedan = Id.New();
        var bare = Id.New();

        var older = Car(office.Id, carTypeId: sedan, listedMinutesAgo: 60, image: "cars/sedan-older.jpg");
        // The newest car with a cover, whose cover is its SECOND photo: the tile uses the primary, as the
        // search card does, not whatever sits at position 0.
        var newer = Car(office.Id, carTypeId: sedan, listedMinutesAgo: 30, image: "cars/sedan-newer-0.jpg");
        var second = newer.AddImage("cars/sedan-newer-1.jpg", Build.Now).Value;
        Assert.True(newer.SetPrimaryImage(second.Id).IsSuccess);
        // Newer still, but with no cover flag at all: skipped, not a blank tile.
        var uncovered = Car(office.Id, carTypeId: sedan, listedMinutesAgo: 10, image: "cars/sedan-uncovered.jpg");
        // Newest of all, but hidden: not a car a customer can open, so not the tile's photo either.
        var hidden = Car(office.Id, carTypeId: sedan, listedMinutesAgo: 1, image: "cars/sedan-hidden.jpg");
        hidden.Hide(Build.Now);

        var onlyBare = Car(office.Id, carTypeId: bare, image: "cars/bare.jpg");

        await Save([office], [older, newer, uncovered, hidden, onlyBare]);
        await StripCover(uncovered);
        await StripCover(onlyBare);

        var facets = await Facets();
        var sedanTile = facets.CarTypes.Single(type => type.CarTypeId == sedan.Value);
        var bareTile = facets.CarTypes.Single(type => type.CarTypeId == bare.Value);

        Assert.Equal("/api/v1/vehicle-images/cars/sedan-newer-1.jpg", sedanTile.CoverImageUrl);
        Assert.Equal(3, sedanTile.ListedVehicleCount);
        // The same URL the search card for that car carries, so tile and card cannot show different photos.
        await using (var context = NewContext())
        {
            var listing = (await new CatalogueReader(context).SearchAsync(new CatalogueFilter(CarTypeId: sedan), Page))
                .Items.Single(item => item.VehicleId == newer.Id.Value);
            Assert.Equal(listing.CoverImageUrl, sedanTile.CoverImageUrl);
        }

        // A type whose listed cars have no cover photo has no cover: null, never an invented image.
        Assert.Null(bareTile.CoverImageUrl);
        Assert.Equal(1, bareTile.ListedVehicleCount);
    }
}
