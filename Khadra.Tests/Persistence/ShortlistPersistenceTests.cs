using Khadra.Application.Fleet.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.PlatformSettings;
using Khadra.Domain.Fleet;
using Khadra.Domain.Shortlist;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// A customer's saved cars, against the real EF model.
/// </summary>
/// <remarks>
/// Two things are being proved. That the aggregate round-trips with its children and its unique
/// index holds — the index is what makes a double tap impossible, because the handler's
/// read-then-write loses that race. And that a saved car which has since stopped being BOOKABLE
/// keeps its row, comes back named, and comes back the SAME whichever way it stopped — which is the
/// whole reason this reader exists rather than the screen asking the catalogue itself.
/// </remarks>
public sealed class ShortlistPersistenceTests : IDisposable
{
    private static readonly DateTimeOffset Now = Build.Now;

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;
    private readonly Id _customerId = Id.New();

    public ShortlistPersistenceTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new KhadraDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private KhadraDbContext NewContext() => new(_options);

    private async Task<Id> SaveAsync(params Id[] vehicleIds)
    {
        await using var context = NewContext();
        var shortlist = CustomerShortlist.Start(_customerId, Now);
        var moment = Now;
        foreach (var vehicleId in vehicleIds)
        {
            Assert.True(shortlist.Add(vehicleId, TestBusinessRules.MaxShortlistEntries, moment).IsSuccess);
            // Distinct instants, so the newest-first order is a real assertion rather than a tie.
            moment = moment.AddMinutes(1);
        }

        context.Shortlists.Add(shortlist);
        await context.SaveChangesAsync();
        return shortlist.Id;
    }

    [Fact]
    public async Task A_shortlist_round_trips_with_its_entries()
    {
        var first = Id.New();
        var second = Id.New();
        await SaveAsync(first, second);

        await using var reader = NewContext();
        var loaded = await new ShortlistRepository(reader).GetAsync(_customerId);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Count);
        // Newest save first.
        Assert.Equal([second, first], loaded.Entries.Select(entry => entry.VehicleId));
    }

    /// <summary>
    /// The unique index, which is what actually stops a double tap.
    /// </summary>
    /// <remarks>
    /// The aggregate checks it too and gives the better answer, but two taps on a slow connection are
    /// two requests and the read-then-write between them loses that race. This is the guarantee.
    /// </remarks>
    [Fact]
    public async Task The_same_car_cannot_be_saved_twice_even_by_two_racing_requests()
    {
        var car = Id.New();
        var shortlistId = await SaveAsync(car);

        // A second request that read the list before the first one committed, and so believes the
        // car is not on it.
        await using var context = NewContext();
        context.Set<ShortlistEntry>().Add(
            Build.ShortlistEntry(shortlistId, car, Now.AddMinutes(5)));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task The_heart_query_answers_only_about_the_cars_it_was_asked_about()
    {
        var saved = Id.New();
        var alsoSaved = Id.New();
        var notSaved = Id.New();
        await SaveAsync(saved, alsoSaved);

        await using var reader = NewContext();
        var membership = await new ShortlistRepository(reader)
            .SavedAmongAsync(_customerId, [saved, notSaved]);

        Assert.Contains(saved, membership);
        Assert.DoesNotContain(notSaved, membership);
        // Not asked about, so not answered about — even though it IS saved.
        Assert.DoesNotContain(alsoSaved, membership);
    }

    [Fact]
    public async Task Another_customers_list_is_not_visible()
    {
        var car = Id.New();
        await SaveAsync(car);

        await using var reader = NewContext();
        var repository = new ShortlistRepository(reader);

        Assert.Null(await repository.GetAsync(Id.New()));
        Assert.Empty(await repository.SavedAmongAsync(Id.New(), [car]));
    }

    /// <summary>
    /// An id with no vehicle behind it keeps its row and names nothing.
    /// </summary>
    /// <remarks>
    /// Nothing in this platform hard-deletes a vehicle, so this is the shape of a database somebody
    /// has edited by hand. The row survives — a customer's list never edits itself — and the screen
    /// falls back to naming nothing, because a placeholder title would be a car the app invented.
    /// </remarks>
    [Fact]
    public async Task A_saved_id_with_no_car_behind_it_survives_and_names_nothing()
    {
        var gone = Id.New();
        await SaveAsync(gone);

        await using var reader = NewContext();
        var saved = await new ShortlistReader(reader, new CatalogueReader(reader))
            .ListAsync(_customerId);

        var only = Assert.Single(saved);
        Assert.Equal(gone.Value, only.VehicleId);
        Assert.False(only.IsStillListed);
        Assert.Null(only.Listing);
        Assert.Null(only.Identity);
        Assert.Equal(Now, only.SavedAt);
    }

    /// <summary>
    /// Every way a saved car can stop being bookable renders the SAME way: named, and not bookable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The owner settled this on 2026-09-11. The row stays, the car is still recognisable, and it
    /// says "Currently unavailable" — with no reason, because a hidden car, one in maintenance, a
    /// suspended gallery's and a soft-deleted one are answered identically everywhere else on this
    /// platform and this screen must not be the exception.
    /// </para>
    /// <para>
    /// The soft-deleted case is the one that makes the reader's <c>IgnoreQueryFilters</c> necessary
    /// rather than convenient: with the filter respected it would come back unnamed while the other
    /// three came back named, and deletion would become the single reason a customer could tell
    /// apart.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Every_way_a_saved_car_stops_being_bookable_reads_the_same()
    {
        var carType = CarType.Create("Sedan", "سيدان", 1, Now).Value;
        var gallery = Build.ApprovedDealer(commercialRegistration: "200001");
        var suspendedGallery = Build.ApprovedDealer(commercialRegistration: "200002");
        suspendedGallery.Suspend(Id.New(), "Complaints.", Now);

        var bookable = Listed(gallery.Id, carType.Id);
        var hidden = Listed(gallery.Id, carType.Id);
        hidden.Hide(Now);
        var maintenance = Listed(gallery.Id, carType.Id);
        maintenance.SendToMaintenance(Now);
        var deleted = Listed(gallery.Id, carType.Id);
        deleted.Delete(Now);
        var onSuspended = Listed(suspendedGallery.Id, carType.Id);

        await using (var context = NewContext())
        {
            context.CarTypes.Add(carType);
            context.Dealers.AddRange(gallery, suspendedGallery);
            context.Vehicles.AddRange(bookable, hidden, maintenance, deleted, onSuspended);
            await context.SaveChangesAsync();
        }

        await SaveAsync(bookable.Id, hidden.Id, maintenance.Id, deleted.Id, onSuspended.Id);

        await using var reader = NewContext();
        var saved = await new ShortlistReader(reader, new CatalogueReader(reader))
            .ListAsync(_customerId);
        var byId = saved.ToDictionary(entry => entry.VehicleId);

        Assert.Equal(5, saved.Count);

        // The one that is still bookable carries a listing AND the same name the listing has, which
        // is the drift check: two reads of one car must agree about what it is called.
        var live = byId[bookable.Id.Value];
        Assert.True(live.IsStillListed);
        Assert.NotNull(live.Identity);
        Assert.Equal(live.Listing!.Make, live.Identity!.Make);
        Assert.Equal(live.Listing.Model, live.Identity.Model);
        Assert.Equal(live.Listing.Year, live.Identity.Year);
        Assert.Equal(live.Listing.Gallery.BusinessName, live.Identity.GalleryName);

        var unbookable = new (Vehicle Vehicle, Dealer Gallery, string Label)[]
        {
            (hidden, gallery, "hidden car"),
            (maintenance, gallery, "car in maintenance"),
            (deleted, gallery, "soft-deleted car"),
            (onSuspended, suspendedGallery, "listed car, suspended gallery"),
        };

        foreach (var (vehicle, owner, label) in unbookable)
        {
            var entry = byId[vehicle.Id.Value];
            Assert.False(entry.IsStillListed, $"Still bookable: {label}.");
            Assert.Null(entry.Listing);
            // Named, identically, in all four cases.
            Assert.NotNull(entry.Identity);
            Assert.Equal(vehicle.Details.Make, entry.Identity!.Make);
            Assert.Equal(vehicle.Details.Model, entry.Identity.Model);
            Assert.Equal(vehicle.Details.Year, entry.Identity.Year);
            Assert.Equal(owner.BusinessName.Value, entry.Identity.GalleryName);
        }
    }

    /// <summary>The name is read LIVE, so a gallery correcting a listing corrects the saved row.</summary>
    /// <remarks>
    /// The alternative — a snapshot taken when the car was saved — shows last month's car, and would
    /// go on showing it for as long as the entry lived. This test is what stops one being introduced.
    /// </remarks>
    [Fact]
    public async Task A_correction_to_a_hidden_car_reaches_the_saved_row()
    {
        var carType = CarType.Create("Sedan", "سيدان", 1, Now).Value;
        var gallery = Build.ApprovedDealer(commercialRegistration: "200003");
        var car = Listed(gallery.Id, carType.Id);

        await using (var context = NewContext())
        {
            context.CarTypes.Add(carType);
            context.Dealers.Add(gallery);
            context.Vehicles.Add(car);
            await context.SaveChangesAsync();
        }

        await SaveAsync(car.Id);

        await using (var context = NewContext())
        {
            var stored = await context.Vehicles.SingleAsync(vehicle => vehicle.Id == car.Id);
            stored.Hide(Now);
            stored.UpdateDetails(
                stored.CarTypeId,
                VehicleDetails.Create(
                    "Toyota", "Camry", 2023, 5, TransmissionType.Automatic, FuelType.Petrol,
                    currentYear: 2026).Value,
                stored.PlateNumber);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var only = Assert.Single(
            await new ShortlistReader(reader, new CatalogueReader(reader)).ListAsync(_customerId));

        Assert.False(only.IsStillListed);
        Assert.Equal("Camry", only.Identity!.Model);
    }

    private int _plate;

    /// <summary>A listed car: published, which requires a photo and a gallery that may trade.</summary>
    private Vehicle Listed(Id dealerId, Id carTypeId)
    {
        var vehicle = Build.Vehicle(
            dealerId,
            carTypeId: carTypeId,
            plateNumber: $"12-{34567 + ++_plate}");
        vehicle.AddImage("cars/front.jpg", Now);
        vehicle.Publish(dealerCanTrade: true, Now);
        vehicle.ClearDomainEvents();
        return vehicle;
    }
}
