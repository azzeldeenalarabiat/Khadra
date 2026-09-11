using Khadra.Application.Fleet.ReadModels;
using Khadra.Domain.Common;
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
/// read-then-write loses that race. And that a saved car which has since STOPPED being listed keeps
/// its row and comes back marked gone, which is the whole reason this reader exists rather than the
/// screen asking the catalogue itself.
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
            Assert.True(shortlist.Add(vehicleId, 50, moment).IsSuccess);
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
    /// A car that has stopped being listed keeps its row and comes back marked gone.
    /// </summary>
    /// <remarks>
    /// Maintenance → Hidden → Active is a normal round trip for a gallery. An entry removed on the
    /// way through would be a customer's list quietly editing itself, and a row rendered with a name
    /// would need either a snapshot that goes stale or a read that bypasses the visibility predicate
    /// — which is the enumeration oracle the catalogue is shaped to prevent.
    /// </remarks>
    [Fact]
    public async Task A_saved_car_that_is_no_longer_listed_survives_and_says_so()
    {
        var gone = Id.New();
        await SaveAsync(gone);

        await using var reader = NewContext();
        var saved = await new ShortlistReader(reader, new CatalogueReader(reader))
            .ListAsync(_customerId);

        var only = Assert.Single(saved);
        Assert.Equal(gone.Value, only.VehicleId);
        Assert.False(only.IsStillListed);
        // The row carries WHEN it was saved and nothing about the car. No name, and above all no
        // reason: a hidden car, a deleted one and a suspended gallery's must stay indistinguishable.
        Assert.Null(only.Listing);
        Assert.Equal(Now, only.SavedAt);
    }
}
