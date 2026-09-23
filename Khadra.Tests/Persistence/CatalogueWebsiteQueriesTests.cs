using Khadra.Application.Common;
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
        int listedMinutesAgo = 0)
    {
        var listedAt = Build.Now.AddMinutes(-listedMinutesAgo);
        var vehicle = Vehicle.Add(
            dealerId,
            Id.New(),
            VehicleDetails.Create(make, "Model", year, 5, TransmissionType.Automatic, fuel ?? FuelType.Petrol, currentYear: 2026).Value,
            PlateNumber.Create($"55-{10000 + ++_plate}").Value,
            Money.Jod(rate),
            Money.Jod(200m),
            MileagePolicy.Unlimited(),
            FuelPolicy.FullToFull,
            true,
            listedAt).Value;
        vehicle.AddImage("cars/front.jpg", listedAt);
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
        var all = await reader.ListGalleriesAsync(null, Page);

        // Most cars first, then by name; a hidden car is not counted, and only offices that may trade appear.
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(["Busy Rentals", "Quiet Rentals", "Empty Rentals"], all.Items.Select(card => card.BusinessName));
        Assert.Equal([2, 1, 0], all.Items.Select(card => card.ListedVehicleCount));

        // The count is exactly what the office's own car list holds.
        var busyCars = await reader.SearchAsync(new CatalogueFilter(DealerId: busy.Id), Page);
        Assert.Equal(busyCars.TotalCount, all.Items[0].ListedVehicleCount);

        var inAmman = await reader.ListGalleriesAsync(_amman.Id, Page);
        Assert.Equal(["Busy Rentals", "Empty Rentals"], inAmman.Items.Select(card => card.BusinessName));
        Assert.All(inAmman.Items, card => Assert.Equal(_amman.Id.Value, card.CityId));
    }

    [Fact]
    public async Task An_office_nobody_has_rated_says_so_rather_than_scoring_zero()
    {
        var office = Office("Unrated Rentals");
        await Save([office], [Car(office.Id)]);

        await using var context = NewContext();
        var card = (await new CatalogueReader(context).ListGalleriesAsync(null, Page)).Items.Single();

        Assert.Null(card.AverageRating);
        Assert.Equal(0, card.ReviewCount);
    }
}
