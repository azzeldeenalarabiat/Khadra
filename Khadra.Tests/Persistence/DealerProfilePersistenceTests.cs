using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers;
using Khadra.Application.Dealers.Dtos;
using Khadra.Application.Dealers.GetMyDealer;
using Khadra.Application.Dealers.UpdateProfile;
using Khadra.Application.Fleet.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Fleet;
using Khadra.Domain.PlatformSettings;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Saving the dealer page, through the real handler and the real repositories, onto the real model.
/// </summary>
/// <remarks>
/// <para>
/// Every save of this page used to erase the office's city and address: the request carried no
/// location, the command's defaults supplied nulls, and the aggregate stored them. The consequence
/// that matters is at the far end of the chain — the catalogue filters by the office's city, so its
/// whole fleet dropped out of every city search, silently. So these run the chain that broke, not
/// its halves: handler, repository, unit of work, SQLite, then a fresh context and the reader.
/// </para>
/// <para>
/// The request-to-command half is pinned in <c>DealerProfileEndpointTests</c>, against the real
/// controller.
/// </para>
/// </remarks>
public sealed class DealerProfilePersistenceTests : IDisposable
{
    private static readonly Id OwnerId = Id.New();

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    private readonly City _amman = City.Create("Amman", "عمّان", 1, Build.Now).Value;
    private readonly City _irbid = City.Create("Irbid", "إربد", 2, Build.Now).Value;
    private readonly City _zarqa = City.Create("Zarqa", "الزرقاء", 3, Build.Now).Value;
    private readonly CarType _sedan = CarType.Create("Sedan", "سيدان", 1, Build.Now).Value;

    public DealerProfilePersistenceTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new KhadraDbContext(_options);
        context.Database.EnsureCreated();
        context.Cities.AddRange(_amman, _irbid, _zarqa);
        context.CarTypes.Add(_sedan);
        context.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private KhadraDbContext NewContext() => new(_options);

    private static PageRequest Page => PageRequest.From(1, 50);

    /// <summary>The page's handlers as the API runs them, over one context.</summary>
    private static DealerProfileHandlers Handlers(KhadraDbContext context) => new(
        new DealerMembershipResolver(new DealerRepository(context)),
        new CityRepository(context),
        Substitute.For<IUploadTicketService>(),
        Substitute.For<IDocumentStorage>(),
        FakeDocumentPolicy.Default,
        new TestClock(Build.Now),
        new UnitOfWork(context, Substitute.For<IDomainEventDispatcher>()));

    private static IReadOnlyList<DayScheduleInput> Week(string fridayOpens = "14:00") =>
    [
        new("Sunday", false, "08:00", "20:00"),
        new("Monday", false, "08:00", "20:00"),
        new("Tuesday", false, "08:00", "20:00"),
        new("Wednesday", false, "08:00", "20:00"),
        new("Thursday", false, "08:00", "20:00"),
        new("Friday", false, fridayOpens, "20:00"),
        new("Saturday", true, null, null),
    ];

    /// <summary>An approved office in Amman, with an address and one car a customer could book.</summary>
    private async Task<(Dealer Dealer, Vehicle Car)> AnOfficeInAmmanWithACar()
    {
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        Assert.True(dealer.UpdateProfile(
            dealer.BusinessName,
            dealer.Location,
            dealer.OperatingHours,
            _amman.Id,
            DealerAddress.Create("Abdoun", "Zahran Street").Value).IsSuccess);

        var car = Build.Vehicle(dealer.Id, carTypeId: _sedan.Id);
        car.AddImage("cars/front.jpg", Build.Now);
        car.Publish(dealerCanTrade: true, Build.Now);
        car.ClearDomainEvents();

        await using var context = NewContext();
        context.Dealers.Add(dealer);
        context.Vehicles.Add(car);
        await context.SaveChangesAsync();
        return (dealer, car);
    }

    /// <summary>A save that changes the hours and states the location it was given.</summary>
    private static UpdateDealerProfileCommand Saving(
        Dealer dealer, Id? cityId, string? area, string? street, string fridayOpens = "15:00") =>
        new(OwnerId, dealer.BusinessName.Value, 31.95, 35.91, Week(fridayOpens), cityId, area, street);

    private async Task<Result<DealerProfileDto, Error>> Save(UpdateDealerProfileCommand command)
    {
        await using var context = NewContext();
        return await Handlers(context).Handle(command, CancellationToken.None);
    }

    private async Task<Dealer> Reload(Id dealerId)
    {
        await using var context = NewContext();
        return await context.Dealers.AsNoTracking().SingleAsync(dealer => dealer.Id == dealerId);
    }

    private async Task<IReadOnlySet<Guid>> CarsInCity(Id cityId)
    {
        await using var context = NewContext();
        var page = await new CatalogueReader(context).SearchAsync(new CatalogueFilter(CityId: cityId), Page);
        return page.Items.Select(item => item.VehicleId).ToHashSet();
    }

    [Fact]
    public async Task An_unrelated_save_keeps_the_city_and_the_address_on_the_row()
    {
        var (dealer, _) = await AnOfficeInAmmanWithACar();

        var saved = await Save(Saving(dealer, _amman.Id, "Abdoun", "Zahran Street"));

        Assert.True(saved.IsSuccess, saved.IsFailure ? saved.Error.Code : null);
        var row = await Reload(dealer.Id);
        Assert.Equal(new TimeOnly(15, 0), row.OperatingHours.For(DayOfWeek.Friday).OpensAt);
        // Each field on its own: an owned type left to convention loses fields with no error.
        Assert.Equal(_amman.Id, row.CityId);
        Assert.Equal("Abdoun", row.Address?.Area);
        Assert.Equal("Zahran Street", row.Address?.Street);
    }

    [Fact]
    public async Task The_fleet_stays_in_its_city_search_after_an_unrelated_save_and_leaves_it_only_when_told_to()
    {
        var (dealer, car) = await AnOfficeInAmmanWithACar();
        Assert.Contains(car.Id.Value, await CarsInCity(_amman.Id));

        // The consequence the defect had: after changing one opening time, the office's cars were
        // still bookable by link and absent from every search a customer actually browses.
        Assert.True((await Save(Saving(dealer, _amman.Id, "Abdoun", "Zahran Street"))).IsSuccess);
        Assert.Contains(car.Id.Value, await CarsInCity(_amman.Id));

        // The control: stating no city does take the office out of the city's results. This is what
        // proves the assertion above can fail, and it records that null still means "no city".
        Assert.True((await Save(Saving(dealer, null, "Abdoun", "Zahran Street"))).IsSuccess);
        Assert.DoesNotContain(car.Id.Value, await CarsInCity(_amman.Id));
    }

    [Fact]
    public async Task A_move_to_another_offered_city_is_stored_and_read_back_the_same()
    {
        var (dealer, car) = await AnOfficeInAmmanWithACar();

        var saved = await Save(Saving(dealer, _irbid.Id, "University Street area", null));

        Assert.True(saved.IsSuccess, saved.IsFailure ? saved.Error.Code : null);
        Assert.Equal(_irbid.Id.Value, saved.Value.CityId);

        // Read after write, through the query `GET /dealers/me` runs, on a context that never saw
        // the save.
        await using var context = NewContext();
        var read = await new GetMyDealerHandler(new DealerMembershipResolver(new DealerRepository(context)))
            .Handle(new GetMyDealerQuery(OwnerId), CancellationToken.None);
        Assert.True(read.IsSuccess, read.IsFailure ? read.Error.Code : null);
        Assert.Equal(_irbid.Id.Value, read.Value.CityId);
        Assert.Equal("University Street area", read.Value.Address?.Area);
        Assert.Null(read.Value.Address?.Street);

        Assert.Contains(car.Id.Value, await CarsInCity(_irbid.Id));
        Assert.DoesNotContain(car.Id.Value, await CarsInCity(_amman.Id));
    }

    [Fact]
    public async Task An_office_filed_under_a_city_that_was_retired_since_can_still_save_its_hours()
    {
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        Assert.True(dealer.UpdateProfile(
            dealer.BusinessName, dealer.Location, dealer.OperatingHours, _zarqa.Id,
            DealerAddress.Create("Jabal Tariq", null).Value).IsSuccess);
        await using (var context = NewContext())
        {
            context.Dealers.Add(dealer);
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            // An administrator retires the city after the office was filed under it.
            var zarqa = await context.Cities.SingleAsync(city => city.Id == _zarqa.Id);
            Assert.True(zarqa.Deactivate().IsSuccess);
            await context.SaveChangesAsync();
        }

        var saved = await Save(Saving(dealer, _zarqa.Id, "Jabal Tariq", null));

        Assert.True(saved.IsSuccess, saved.IsFailure ? saved.Error.Code : null);
        var row = await Reload(dealer.Id);
        Assert.Equal(_zarqa.Id, row.CityId);
        Assert.Equal("Jabal Tariq", row.Address?.Area);
        Assert.Equal(new TimeOnly(15, 0), row.OperatingHours.For(DayOfWeek.Friday).OpensAt);
    }

    [Fact]
    public async Task A_newly_chosen_retired_or_unknown_city_is_refused_and_the_row_is_untouched()
    {
        var (dealer, _) = await AnOfficeInAmmanWithACar();
        await using (var context = NewContext())
        {
            var zarqa = await context.Cities.SingleAsync(city => city.Id == _zarqa.Id);
            Assert.True(zarqa.Deactivate().IsSuccess);
            await context.SaveChangesAsync();
        }

        var toRetired = await Save(Saving(dealer, _zarqa.Id, "Abdoun", "Zahran Street"));
        var toNowhere = await Save(Saving(dealer, Id.New(), "Abdoun", "Zahran Street"));

        Assert.Equal("dealer.unknown_city", toRetired.Error.Code);
        Assert.Equal("dealer.unknown_city", toNowhere.Error.Code);
        var row = await Reload(dealer.Id);
        Assert.Equal(_amman.Id, row.CityId);
        Assert.Equal("Abdoun", row.Address?.Area);
        Assert.Equal("Zahran Street", row.Address?.Street);
        // Refused before anything was written: the hours did not change either.
        Assert.Equal(new TimeOnly(9, 0), row.OperatingHours.For(DayOfWeek.Friday).OpensAt);
    }

    [Fact]
    public async Task The_longest_address_the_domain_allows_is_stored_whole()
    {
        var (dealer, _) = await AnOfficeInAmmanWithACar();
        var area = new string('a', DealerAddress.AreaMaxLength);
        var street = new string('s', DealerAddress.StreetMaxLength);

        Assert.True((await Save(Saving(dealer, _amman.Id, area, street))).IsSuccess);

        var row = await Reload(dealer.Id);
        Assert.Equal(area, row.Address?.Area);
        Assert.Equal(street, row.Address?.Street);
    }

    [Fact]
    public void The_address_columns_hold_exactly_what_the_domain_allows()
    {
        // SQLite has no lengths to violate, so the round trip above cannot see a column narrower than
        // the domain's limit. Postgres can: a valid address would fail with 22001 on save. Pinned on
        // the model instead, which is what the migrations are generated from.
        using var context = NewContext();
        var address = context.Model.FindEntityType(typeof(Dealer))!
            .FindNavigation(nameof(Dealer.Address))!
            .TargetEntityType;

        Assert.Equal(DealerAddress.AreaMaxLength, address.FindProperty(nameof(DealerAddress.Area))!.GetMaxLength());
        Assert.Equal(DealerAddress.StreetMaxLength, address.FindProperty(nameof(DealerAddress.Street))!.GetMaxLength());
    }
}
