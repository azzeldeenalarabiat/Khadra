using Khadra.Domain.PlatformSettings.Repositories;
using Khadra.Domain.PlatformSettings;
using Khadra.Application.Dealers;
using Khadra.Application.Common;
using Khadra.Application.Fleet.ManageVehicles;
using Khadra.Application.Fleet.VehicleImages;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Fleet;
using Khadra.Domain.Fleet.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Fleet;

// Spec 4.3: a dealer manages their OWN cars. The two things worth holding hardest are that an
// unapproved dealer cannot list anything, and that one dealer can never touch another's fleet.
public sealed class FleetManagementTests
{
    private static readonly Id OwnerId = Id.New();
    private static readonly Id OtherOwnerId = Id.New();

    private sealed class Context
    {
        public IVehicleRepository Vehicles { get; } = Substitute.For<IVehicleRepository>();
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public FakeDocumentStorage Storage { get; } = new();
        public ICarTypeRepository CarTypes { get; } = Substitute.For<ICarTypeRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public TestClock Clock { get; } = new(Users.Now);
        public List<Vehicle> Added { get; } = [];

        public Context()
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            // Every car type these tests name is real and offered, unless a test says otherwise:
            // they are about the fleet rules, not about the platform's category list.
            CarTypes.GetByIdAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>())
                .Returns(CarType.Create("Sedan", "سيدان", 1, Users.Now).Value);
            Vehicles.When(repository => repository.AddAsync(Arg.Any<Vehicle>(), Arg.Any<CancellationToken>()))
                .Do(call => Added.Add(call.Arg<Vehicle>()));
        }

        public Dealer GivenDealer(Dealer dealer, Id ownerUserId)
        {
            Dealers.GetByOwnerUserIdAsync(ownerUserId, Arg.Any<CancellationToken>()).Returns(dealer);
            return dealer;
        }

        public Vehicle GivenVehicle(Vehicle vehicle)
        {
            Vehicles.GetByIdAsync(vehicle.Id, Arg.Any<CancellationToken>()).Returns(vehicle);
            return vehicle;
        }

        /// <summary>
        /// <paramref name="earliestModelYear"/> is the platform's configured floor, so a test can
        /// prove that moving it changes which cars are accepted.
        /// </summary>
        public VehicleHandlers Handlers(
            int earliestModelYear = TestBusinessRules.EarliestVehicleModelYear) =>
            new(
                Vehicles,
                Dealers,
                new DealerMembershipResolver(Dealers),
                Clock,
                TestBusinessRules.Provider(earliestVehicleModelYear: earliestModelYear),
                CarTypes,
                UnitOfWork);

        public VehicleImageHandlers Images() => new(
            Vehicles, Dealers, new StubUploadTickets(), Storage, FakeDocumentPolicy.Default, Clock, UnitOfWork);
    }

    private sealed class StubUploadTickets : Khadra.Application.Common.Ports.IUploadTicketService
    {
        public Khadra.Application.Common.Ports.UploadTicket Issue(string storageKey, string contentType, DateTimeOffset now) =>
            new($"/api/v1/uploads/token-for-{storageKey}", storageKey, now.AddMinutes(15));

        public bool TryRedeem(string token, DateTimeOffset now, out Khadra.Application.Common.Ports.RedeemedUpload upload)
        {
            upload = new Khadra.Application.Common.Ports.RedeemedUpload(string.Empty, string.Empty);
            return false;
        }
    }

    private static VehicleDetailsInput Details(string plate = "1234567") => new(
        CarTypeId: Guid.CreateVersion7(),
        Make: "Toyota",
        Model: "Corolla",
        Year: 2024,
        Color: "White",
        Seats: 5,
        Transmission: "Automatic",
        FuelType: "Petrol",
        Description: "Clean, low mileage, ideal for city driving.",
        PlateNumber: plate,
        DailyRate: 32m,
        SecurityDeposit: 150m,
        IsDeliveryEligible: true,
        Mileage: new MileagePolicyInput(IsUnlimited: false, DailyLimitKm: 200, ExcessFeePerKm: 0.15m),
        FuelPolicy: "FullToFull");

    /// <summary>
    /// An older car is an ordinary listing.
    ///
    /// The year was bounded by a `const` of 1990 in the domain and a twelve-entry dropdown in the
    /// console, so a 1988 car could be neither chosen nor saved — and nothing said why it was 1990.
    /// The floor is <c>BusinessRules.EarliestVehicleModelYear</c> now, which is why this test can
    /// set it.
    /// </summary>
    [Theory]
    [InlineData(1972)]
    [InlineData(1988)]
    [InlineData(1995)]
    public async Task A_car_older_than_the_old_hardcoded_floor_can_be_listed(int year)
    {
        var context = new Context();
        context.GivenDealer(Build.ApprovedDealer(ownerUserId: OwnerId), OwnerId);

        var result = await context.Handlers(earliestModelYear: 1970).Handle(
            new AddVehicleCommand(OwnerId, Details() with { Year = year }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(year, result.Value.Year);
    }

    /// <summary>The floor is a real bound, not decoration: below it the car is refused.</summary>
    [Fact]
    public async Task A_year_below_the_platforms_floor_is_refused()
    {
        var context = new Context();
        context.GivenDealer(Build.ApprovedDealer(ownerUserId: OwnerId), OwnerId);

        var result = await context.Handlers(earliestModelYear: 1990).Handle(
            new AddVehicleCommand(OwnerId, Details() with { Year = 1989 }),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("vehicle.invalid_year", result.Error.Code);
    }

    /// <summary>
    /// Moving the configured floor moves what is accepted — which is the whole point of it being
    /// configuration rather than a constant.
    /// </summary>
    [Fact]
    public async Task Lowering_the_floor_admits_a_year_that_was_refused_before()
    {
        var context = new Context();
        context.GivenDealer(Build.ApprovedDealer(ownerUserId: OwnerId), OwnerId);

        var refused = await context.Handlers(earliestModelYear: 1990).Handle(
            new AddVehicleCommand(OwnerId, Details() with { Year = 1975 }),
            CancellationToken.None);
        var admitted = await context.Handlers(earliestModelYear: 1970).Handle(
            new AddVehicleCommand(OwnerId, Details(plate: "7654321") with { Year = 1975 }),
            CancellationToken.None);

        Assert.True(refused.IsFailure);
        Assert.True(admitted.IsSuccess);
    }

    /// <summary>
    /// A dealer who filed a car under the wrong category can put it right.
    ///
    /// The update command has always carried a CarTypeId and the handler always ignored it: the
    /// dealer picked a type, got a 200 and a "Car updated" toast, and the car kept the category it
    /// had. Nothing failed, which is exactly why nobody noticed.
    /// </summary>
    [Fact]
    public async Task Editing_a_car_changes_the_category_it_is_listed_under()
    {
        var context = new Context();
        var dealer = context.GivenDealer(Build.ApprovedDealer(ownerUserId: OwnerId), OwnerId);
        var car = context.GivenVehicle(Build.Vehicle(dealerId: dealer.Id));
        var wasType = car.CarTypeId;
        var nowType = Guid.CreateVersion7();

        var result = await context.Handlers().Handle(
            new UpdateVehicleCommand(OwnerId, car.Id, Details() with { CarTypeId = nowType }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(nowType, car.CarTypeId.Value);
        Assert.NotEqual(wasType, car.CarTypeId);
    }

    [Fact]
    public async Task A_new_car_starts_as_a_draft_and_is_not_yet_bookable()
    {
        // Publishing is a separate, deliberate act, and it needs a photo first.
        var context = new Context();
        context.GivenDealer(Build.ApprovedDealer(ownerUserId: OwnerId), OwnerId);

        var result = await context.Handlers().Handle(new AddVehicleCommand(OwnerId, Details()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Draft", result.Value.Status);
        Assert.False(result.Value.IsBookable);
        Assert.Equal("Clean, low mileage, ideal for city driving.", result.Value.Description);
        Assert.Equal(200, result.Value.Mileage.DailyLimitKm);
        Assert.True(result.Value.IsDeliveryEligible);
        Assert.Single(context.Added);
    }

    [Fact]
    public async Task A_plate_already_on_the_platform_is_refused()
    {
        // One car, one listing: the same plate under two dealers means a duplicate or a car that is
        // not theirs to list.
        var context = new Context();
        context.GivenDealer(Build.ApprovedDealer(ownerUserId: OwnerId), OwnerId);
        context.Vehicles.PlateNumberExistsAsync(Arg.Any<PlateNumber>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await context.Handlers().Handle(new AddVehicleCommand(OwnerId, Details()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("vehicle.plate_taken", result.Error.Code);
        Assert.Empty(context.Added);
    }

    [Fact]
    public async Task Publishing_needs_a_photo()
    {
        // A listing with no photo converts badly and looks fraudulent to a customer.
        var context = new Context();
        var dealer = context.GivenDealer(Build.ApprovedDealer(ownerUserId: OwnerId), OwnerId);
        var vehicle = context.GivenVehicle(Build.Vehicle(dealerId: dealer.Id));

        var result = await context.Handlers().Handle(
            new ChangeVehicleStatusCommand(OwnerId, vehicle.Id, VehicleStatusChange.Publish), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("vehicle.image_required", result.Error.Code);
    }

    [Fact]
    public async Task A_published_car_becomes_bookable_and_can_be_hidden_again()
    {
        var context = new Context();
        var dealer = context.GivenDealer(Build.ApprovedDealer(ownerUserId: OwnerId), OwnerId);
        var vehicle = context.GivenVehicle(Build.Vehicle(dealerId: dealer.Id));
        vehicle.AddImage("vehicles/abc/1.jpg", Users.Now);

        var published = await context.Handlers().Handle(
            new ChangeVehicleStatusCommand(OwnerId, vehicle.Id, VehicleStatusChange.Publish), CancellationToken.None);
        Assert.Equal("Active", published.Value.Status);
        Assert.True(published.Value.IsBookable);

        var hidden = await context.Handlers().Handle(
            new ChangeVehicleStatusCommand(OwnerId, vehicle.Id, VehicleStatusChange.Hide), CancellationToken.None);
        Assert.Equal("Hidden", hidden.Value.Status);
        Assert.False(hidden.Value.IsBookable);
    }

    [Fact]
    public async Task Maintenance_is_distinct_from_hidden_and_returns_to_hidden_not_to_active()
    {
        // Coming back from the garage should not silently relist the car; the dealer decides that.
        var context = new Context();
        var dealer = context.GivenDealer(Build.ApprovedDealer(ownerUserId: OwnerId), OwnerId);
        var vehicle = context.GivenVehicle(Build.Vehicle(dealerId: dealer.Id));
        vehicle.AddImage("vehicles/abc/1.jpg", Users.Now);
        await context.Handlers().Handle(
            new ChangeVehicleStatusCommand(OwnerId, vehicle.Id, VehicleStatusChange.Publish), CancellationToken.None);

        var maintenance = await context.Handlers().Handle(
            new ChangeVehicleStatusCommand(OwnerId, vehicle.Id, VehicleStatusChange.SendToMaintenance),
            CancellationToken.None);
        Assert.Equal("Maintenance", maintenance.Value.Status);
        Assert.False(maintenance.Value.IsBookable);

        var returned = await context.Handlers().Handle(
            new ChangeVehicleStatusCommand(OwnerId, vehicle.Id, VehicleStatusChange.ReturnFromMaintenance),
            CancellationToken.None);
        Assert.Equal("Hidden", returned.Value.Status);
    }

    [Fact]
    public async Task A_car_in_maintenance_cannot_be_published_straight_back_onto_the_platform()
    {
        var context = new Context();
        var dealer = context.GivenDealer(Build.ApprovedDealer(ownerUserId: OwnerId), OwnerId);
        var vehicle = context.GivenVehicle(Build.Vehicle(dealerId: dealer.Id));
        vehicle.AddImage("vehicles/abc/1.jpg", Users.Now);
        vehicle.Publish(dealerCanTrade: true, Users.Now);
        vehicle.SendToMaintenance(Users.Now);

        var result = await context.Handlers().Handle(
            new ChangeVehicleStatusCommand(OwnerId, vehicle.Id, VehicleStatusChange.Publish), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("vehicle.in_maintenance", result.Error.Code);
    }

    [Fact]
    public async Task A_car_is_never_bookable_while_its_dealer_cannot_trade()
    {
        // The listing may be Active, but a suspended dealer's cars must not reach customer search.
        var context = new Context();
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        dealer.Suspend(Id.New(), "Policy violation.", Users.Now);
        context.GivenDealer(dealer, OwnerId);
        var vehicle = context.GivenVehicle(Build.Vehicle(dealerId: dealer.Id));
        vehicle.AddImage("vehicles/abc/1.jpg", Users.Now);
        vehicle.Publish(dealerCanTrade: true, Users.Now);

        var result = await context.Handlers().Handle(new GetMyVehicleQuery(OwnerId, vehicle.Id), CancellationToken.None);

        Assert.Equal("Active", result.Value.Status);
        Assert.False(result.Value.IsBookable);
    }

    [Fact]
    public async Task One_dealer_cannot_reach_another_dealers_car_and_is_told_it_does_not_exist()
    {
        // Not "forbidden": a 403 would confirm the id is real, which is enough to enumerate a
        // competitor's fleet one guess at a time.
        var context = new Context();
        var mine = context.GivenDealer(Build.ApprovedDealer(ownerUserId: OwnerId), OwnerId);
        var theirs = Build.ApprovedDealer(ownerUserId: OtherOwnerId);
        context.GivenDealer(theirs, OtherOwnerId);
        var theirCar = context.GivenVehicle(Build.Vehicle(dealerId: theirs.Id));

        foreach (var result in new[]
                 {
                     await context.Handlers().Handle(new GetMyVehicleQuery(OwnerId, theirCar.Id), CancellationToken.None),
                     await context.Handlers().Handle(new UpdateVehicleCommand(OwnerId, theirCar.Id, Details()), CancellationToken.None),
                     await context.Handlers().Handle(new ChangeVehicleStatusCommand(OwnerId, theirCar.Id, VehicleStatusChange.Hide), CancellationToken.None),
                 })
        {
            Assert.True(result.IsFailure);
            Assert.Equal("vehicle.not_found", result.Error.Code);
            Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        }

        var deleted = await context.Handlers().Handle(
            new DeleteVehicleCommand(OwnerId, theirCar.Id), CancellationToken.None);
        Assert.Equal("vehicle.not_found", deleted.Error.Code);
        Assert.NotEqual(mine.Id, theirCar.DealerId);
    }

    [Fact]
    public async Task Deleting_a_car_hides_it_rather_than_removing_the_record()
    {
        // Bookings reference the vehicle by id; a hard delete would orphan a rental's history.
        var context = new Context();
        var dealer = context.GivenDealer(Build.ApprovedDealer(ownerUserId: OwnerId), OwnerId);
        var vehicle = context.GivenVehicle(Build.Vehicle(dealerId: dealer.Id));

        var result = await context.Handlers().Handle(
            new DeleteVehicleCommand(OwnerId, vehicle.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(vehicle.IsDeleted);
        Assert.Equal(VehicleStatus.Hidden, vehicle.Status);
        Assert.False(vehicle.IsBookable(dealerCanTrade: true));
    }

    [Fact]
    public async Task An_owner_with_no_dealer_application_has_no_fleet_to_manage()
    {
        var context = new Context();

        var result = await context.Handlers().Handle(new ListMyVehiclesQuery(OwnerId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("dealer.not_registered", result.Error.Code);
    }

    [Fact]
    public async Task An_upload_ticket_is_scoped_to_the_car_it_was_issued_for()
    {
        var context = new Context();
        var dealer = context.GivenDealer(Build.ApprovedDealer(ownerUserId: OwnerId), OwnerId);
        var vehicle = context.GivenVehicle(Build.Vehicle(dealerId: dealer.Id));

        var ticket = await context.Images().Handle(
            new RequestVehicleImageUploadCommand(OwnerId, vehicle.Id, "image/jpeg"), CancellationToken.None);

        Assert.True(ticket.IsSuccess);
        Assert.StartsWith($"vehicles/{vehicle.Id.Value}/", ticket.Value.StorageKey, StringComparison.Ordinal);
        Assert.EndsWith(".jpg", ticket.Value.StorageKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_key_from_outside_this_car_cannot_be_attached_to_it()
    {
        // Otherwise a dealer could point their listing at any key they could guess, including a
        // private identity document from another context.
        var context = new Context();
        var dealer = context.GivenDealer(Build.ApprovedDealer(ownerUserId: OwnerId), OwnerId);
        var vehicle = context.GivenVehicle(Build.Vehicle(dealerId: dealer.Id));

        var result = await context.Images().Handle(
            new AttachVehicleImageCommand(OwnerId, vehicle.Id, "customers/somebody-else/passport.jpg"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("vehicle.image_not_found", result.Error.Code);
        Assert.Empty(vehicle.Images);
    }

    [Fact]
    public async Task An_unsupported_image_type_never_gets_a_ticket()
    {
        var context = new Context();
        var dealer = context.GivenDealer(Build.ApprovedDealer(ownerUserId: OwnerId), OwnerId);
        var vehicle = context.GivenVehicle(Build.Vehicle(dealerId: dealer.Id));

        var result = await context.Images().Handle(
            new RequestVehicleImageUploadCommand(OwnerId, vehicle.Id, "application/x-msdownload"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("vehicle.invalid_image_type", result.Error.Code);
    }

    [Fact]
    public async Task The_first_photo_becomes_the_cover_and_removing_it_promotes_the_next()
    {
        var context = new Context();
        var dealer = context.GivenDealer(Build.ApprovedDealer(ownerUserId: OwnerId), OwnerId);
        var vehicle = context.GivenVehicle(Build.Vehicle(dealerId: dealer.Id));
        var key = $"vehicles/{vehicle.Id.Value}/";

        await context.Images().Handle(new AttachVehicleImageCommand(OwnerId, vehicle.Id, key + "a.jpg"), CancellationToken.None);
        var second = await context.Images().Handle(
            new AttachVehicleImageCommand(OwnerId, vehicle.Id, key + "b.jpg"), CancellationToken.None);

        Assert.Equal(2, second.Value.Images.Count);
        Assert.True(second.Value.Images[0].IsPrimary);

        var coverId = Id.From(second.Value.Images[0].ImageId);
        var afterRemoval = await context.Images().Handle(
            new RemoveVehicleImageCommand(OwnerId, vehicle.Id, coverId), CancellationToken.None);

        // The listing must never render without a cover photo.
        Assert.Single(afterRemoval.Value.Images);
        Assert.True(afterRemoval.Value.Images[0].IsPrimary);
        Assert.Contains(key + "a.jpg", context.Storage.Deleted);
    }

    /// <summary>
    /// The fleet endpoints admit any dealer staff, so the handler must resolve the dealership the
    /// same way. Resolving by owner alone told an employee they had no dealership at all, on a
    /// screen their own sidebar links to.
    /// </summary>
    [Fact]
    public async Task An_employee_can_read_the_fleet_of_the_dealership_they_work_for()
    {
        var context = new Context();
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        context.GivenDealer(dealer, OwnerId);
        var employeeUserId = Id.New();
        dealer.HireEmployee(employeeUserId, canViewReports: false, Build.Now);
        context.Dealers.GetByStaffUserIdAsync(employeeUserId, Arg.Any<CancellationToken>()).Returns(dealer);
        context.Vehicles.ListByDealerAsync(dealer.Id, Arg.Any<CancellationToken>())
            .Returns([Build.Vehicle(dealerId: dealer.Id)]);

        var listed = await context.Handlers().Handle(new ListMyVehiclesQuery(employeeUserId), CancellationToken.None);

        Assert.True(listed.IsSuccess);
        Assert.Single(listed.Value);
    }
}
