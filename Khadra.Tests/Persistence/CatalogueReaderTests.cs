using Khadra.Application.Common;
using Khadra.Application.Fleet.ReadModels;
using Khadra.Domain.Bookings;
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
/// The customer catalogue, against the real EF model on SQLite.
/// </summary>
/// <remarks>
/// This suite exists for one reason above all others: the reader spells `Vehicle.IsBookable` and
/// `Dealer.CanTrade` out as SQL, because neither computed property translates. That spelling is a
/// SECOND definition of who may see what, on the platform's only endpoints that need no sign-in.
/// If it drifts from the domain's, the failure is silent and the wrong way round — a draft listing
/// or a suspended gallery's cars, in front of the public.
///
/// So the matrix test below does not assert a hand-written list of expectations. It asks the DOMAIN
/// what each combination should be and holds the reader to that answer.
/// </remarks>
public sealed class CatalogueReaderTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public CatalogueReaderTests()
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

    private static PageRequest Page => PageRequest.From(1, 50);

    private int _plate;

    /// <summary>A listed car: published, which requires a photo and a gallery that may trade.</summary>
    private Vehicle Listed(Id dealerId, Id carTypeId, bool isDeliveryEligible = true)
    {
        var vehicle = Build.Vehicle(
            dealerId,
            carTypeId: carTypeId,
            isDeliveryEligible: isDeliveryEligible,
            plateNumber: $"12-{34567 + ++_plate}");
        vehicle.AddImage("cars/front.jpg", Build.Now);
        vehicle.Publish(dealerCanTrade: true, Build.Now);
        vehicle.ClearDomainEvents();
        return vehicle;
    }

    [Fact]
    public async Task Only_cars_a_customer_could_actually_book_are_listed()
    {
        var carType = CarType.Create("Sedan", "سيدان", 1, Build.Now).Value;

        var approved = Build.ApprovedDealer(commercialRegistration: "100001");
        var pending = Build.Dealer(commercialRegistration: "100002");
        var rejected = Build.Dealer(commercialRegistration: "100003");
        Build.AttachAllDocuments(rejected);
        rejected.Reject(Id.New(), "Licence expired.", Build.Now);
        var suspended = Build.ApprovedDealer(commercialRegistration: "100004");
        suspended.Suspend(Id.New(), "Complaints.", Build.Now);

        // One car per gallery state, all of them published.
        var onApproved = Listed(approved.Id, carType.Id);
        var onPending = Listed(pending.Id, carType.Id);
        var onRejected = Listed(rejected.Id, carType.Id);
        var onSuspended = Listed(suspended.Id, carType.Id);

        // Every other vehicle state, all on the gallery that CAN trade, so the only thing being
        // judged is the car.
        var draft = Build.Vehicle(approved.Id);
        var hidden = Listed(approved.Id, carType.Id);
        hidden.Hide(Build.Now);
        var maintenance = Listed(approved.Id, carType.Id);
        maintenance.SendToMaintenance(Build.Now);
        var deleted = Listed(approved.Id, carType.Id);
        deleted.Delete(Build.Now);

        await using (var context = NewContext())
        {
            context.CarTypes.Add(carType);
            context.Dealers.AddRange(approved, pending, rejected, suspended);
            context.Vehicles.AddRange(onApproved, onPending, onRejected, onSuspended, draft, hidden, maintenance, deleted);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var page = await new CatalogueReader(reader).SearchAsync(new CatalogueFilter(), Page);
        var listed = page.Items.Select(item => item.VehicleId).ToHashSet();

        // The domain decides; the reader is held to it. A new VehicleStatus or a new reason a gallery
        // cannot trade fails here rather than leaking through a public endpoint.
        var cases = new (Vehicle Vehicle, Dealer Dealer, string Label)[]
        {
            (onApproved, approved, "listed car, approved gallery"),
            (onPending, pending, "listed car, gallery still awaiting review"),
            (onRejected, rejected, "listed car, rejected gallery"),
            (onSuspended, suspended, "listed car, suspended gallery"),
            (draft, approved, "draft car"),
            (hidden, approved, "hidden car"),
            (maintenance, approved, "car in maintenance"),
            (deleted, approved, "soft-deleted car"),
        };

        foreach (var (vehicle, dealer, label) in cases)
        {
            Assert.True(
                vehicle.IsBookable(dealer.CanTrade) == listed.Contains(vehicle.Id.Value),
                $"The catalogue and the domain disagree about: {label}.");
        }

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(onApproved.Id.Value, page.Items.Single().VehicleId);
    }

    /// <summary>
    /// The category name is correlated, not joined, so a car type an administrator has since retired
    /// still names itself on the cars listed under it (pre-launch checklist item 29).
    /// </summary>
    [Fact]
    public async Task A_retired_car_type_still_has_a_name_on_the_cars_that_use_it()
    {
        var carType = CarType.Create("Sedan", "سيدان", 1, Build.Now).Value;
        var dealer = Build.ApprovedDealer();
        var vehicle = Listed(dealer.Id, carType.Id);

        await using (var context = NewContext())
        {
            carType.Deactivate();
            context.CarTypes.Add(carType);
            context.Dealers.Add(dealer);
            context.Vehicles.Add(vehicle);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var page = await new CatalogueReader(reader).SearchAsync(new CatalogueFilter(), Page);

        var row = page.Items.Single();
        Assert.NotNull(row.CarType);
        Assert.Equal("Sedan", row.CarType.NameEn);
        Assert.Equal("سيدان", row.CarType.NameAr);
    }

    [Fact]
    public async Task A_car_held_across_the_requested_dates_is_not_offered()
    {
        var dealer = Build.ApprovedDealer();
        var vehicle = Listed(dealer.Id, Id.New());
        var start = Build.Now.AddDays(10);
        var held = Build.Period(start, days: 3);

        await using (var context = NewContext())
        {
            var booking = Build.Booking(vehicleId: vehicle.Id, period: Build.Period(start, days: 3));
            booking.ConfirmDepositPaid(Id.New(), Build.Now);
            booking.Approve(Id.New(), Build.Now);
            context.Dealers.Add(dealer);
            context.Vehicles.Add(vehicle);
            context.Bookings.Add(booking);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var catalogue = new CatalogueReader(reader);
        var gap = Build.TurnaroundBuffer;

        // Browsing with no dates: the car is listed, because "free" has no meaning without a period.
        Assert.Single((await catalogue.SearchAsync(new CatalogueFilter(), Page)).Items);

        // The same dates: gone.
        Assert.Empty((await catalogue.SearchAsync(
            new CatalogueFilter(Window: new AvailabilityWindow(held, Build.Now, gap)), Page)).Items);

        // Straight after it comes back, inside the turnaround gap: still gone. The catalogue and the
        // booking guard read the same predicate, so a customer is never offered a car that would be
        // refused at the point of booking.
        var tooSoon = DateRange.Create(held.End, held.End.AddDays(2)).Value;
        Assert.Empty((await catalogue.SearchAsync(
            new CatalogueFilter(Window: new AvailabilityWindow(tooSoon, Build.Now, gap)), Page)).Items);

        // Once the gap has passed: offered again.
        var afterTheGap = DateRange.Create(held.End.Add(gap), held.End.AddDays(2)).Value;
        Assert.Single((await catalogue.SearchAsync(
            new CatalogueFilter(Window: new AvailabilityWindow(afterTheGap, Build.Now, gap)), Page)).Items);
    }

    /// <summary>
    /// Nothing expires abandoned checkouts yet (pre-launch checklist item 4). Without the payment
    /// deadline in the hold predicate, one customer who opened a booking and walked away would take
    /// a car off the market permanently.
    /// </summary>
    [Fact]
    public async Task An_abandoned_checkout_stops_hiding_the_car_once_its_deadline_passes()
    {
        var dealer = Build.ApprovedDealer();
        var vehicle = Listed(dealer.Id, Id.New());
        var start = Build.Now.AddDays(10);
        var wanted = Build.Period(start, days: 3);

        await using (var context = NewContext())
        {
            context.Dealers.Add(dealer);
            context.Vehicles.Add(vehicle);
            // Left in PendingPayment: never paid, never expired.
            context.Bookings.Add(Build.Booking(vehicleId: vehicle.Id, period: Build.Period(start, days: 3)));
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var catalogue = new CatalogueReader(reader);
        var gap = Build.TurnaroundBuffer;

        Assert.Empty((await catalogue.SearchAsync(
            new CatalogueFilter(Window: new AvailabilityWindow(wanted, Build.Now, gap)), Page)).Items);

        Assert.Single((await catalogue.SearchAsync(
            new CatalogueFilter(Window: new AvailabilityWindow(wanted, Build.Now.AddHours(1), gap)), Page)).Items);
    }

    [Fact]
    public async Task One_car_answers_nothing_at_all_when_a_customer_may_not_see_it()
    {
        var dealer = Build.ApprovedDealer();
        var listed = Listed(dealer.Id, Id.New());
        var draft = Build.Vehicle(dealer.Id);

        await using (var context = NewContext())
        {
            context.Dealers.Add(dealer);
            context.Vehicles.AddRange(listed, draft);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var catalogue = new CatalogueReader(reader);

        Assert.NotNull(await catalogue.GetAsync(listed.Id, window: null));
        // Not "a car with status Draft" and not a 403: an anonymous caller must not be able to tell
        // an unpublished listing from an id that was never real.
        Assert.Null(await catalogue.GetAsync(draft.Id, window: null));
        Assert.Null(await catalogue.GetAsync(Id.New(), window: null));
    }

    [Fact]
    public async Task A_gallery_that_cannot_trade_has_no_public_page()
    {
        var trading = Build.ApprovedDealer(commercialRegistration: "200001");
        var suspended = Build.ApprovedDealer(commercialRegistration: "200002");
        suspended.Suspend(Id.New(), "Complaints.", Build.Now);

        await using (var context = NewContext())
        {
            context.Dealers.AddRange(trading, suspended);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var catalogue = new CatalogueReader(reader);

        var page = await catalogue.GetGalleryAsync(trading.Id);
        Assert.NotNull(page);
        Assert.Equal("Petra Rentals", page.BusinessName);
        // Reviews has a domain model and no table (checklist item 3). Null is the honest answer, and
        // a client that sees it must say "no ratings yet" rather than invent a score.
        Assert.Null(page.AverageRating);
        Assert.Equal(0, page.ReviewCount);

        Assert.Null(await catalogue.GetGalleryAsync(suspended.Id));
    }

    [Fact]
    public async Task Delivery_needs_both_the_gallery_and_the_car_to_offer_it()
    {
        var withDelivery = Build.ApprovedDealer(commercialRegistration: "300001");
        withDelivery.EnableDelivery(30m, Money.Jod(10m), Build.Now);
        var withoutDelivery = Build.ApprovedDealer(commercialRegistration: "300002");

        var deliverable = Listed(withDelivery.Id, Id.New());
        var notEligible = Listed(withDelivery.Id, Id.New(), isDeliveryEligible: false);
        var galleryDoesNotDeliver = Listed(withoutDelivery.Id, Id.New());

        await using (var context = NewContext())
        {
            context.Dealers.AddRange(withDelivery, withoutDelivery);
            context.Vehicles.AddRange(deliverable, notEligible, galleryDoesNotDeliver);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var catalogue = new CatalogueReader(reader);

        var all = await catalogue.SearchAsync(new CatalogueFilter(), Page);
        Assert.Equal(3, all.TotalCount);
        Assert.True(all.Items.Single(item => item.VehicleId == deliverable.Id.Value).IsDeliveryAvailable);
        Assert.False(all.Items.Single(item => item.VehicleId == notEligible.Id.Value).IsDeliveryAvailable);
        Assert.False(all.Items.Single(item => item.VehicleId == galleryDoesNotDeliver.Id.Value).IsDeliveryAvailable);

        var delivered = await catalogue.SearchAsync(new CatalogueFilter(DeliveryOnly: true), Page);
        Assert.Equal(deliverable.Id.Value, Assert.Single(delivered.Items).VehicleId);
    }

    [Fact]
    public async Task A_search_term_matches_the_make_or_the_model_and_treats_wildcards_literally()
    {
        var dealer = Build.ApprovedDealer();
        var corolla = Listed(dealer.Id, Id.New());

        await using (var context = NewContext())
        {
            context.Dealers.Add(dealer);
            context.Vehicles.Add(corolla);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var catalogue = new CatalogueReader(reader);

        Assert.Single((await catalogue.SearchAsync(new CatalogueFilter(Text: "toyo"), Page)).Items);
        Assert.Single((await catalogue.SearchAsync(new CatalogueFilter(Text: "COROLLA"), Page)).Items);
        Assert.Empty((await catalogue.SearchAsync(new CatalogueFilter(Text: "hilux"), Page)).Items);
        // A bare wildcard is a search for a percent sign, not a search for everything.
        Assert.Empty((await catalogue.SearchAsync(new CatalogueFilter(Text: "%"), Page)).Items);
    }
}
