using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Fleet;
using Khadra.Domain.Shortlist;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using NSubstitute;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Taking a child out of an aggregate and saving it.
/// </summary>
/// <remarks>
/// <para>
/// Both of these shipped broken and neither had a test that could have caught it. The aggregates did
/// their part correctly, and every handler test passed, because `IUnitOfWork` was a substitute in
/// each of them and EF's change tracker never ran. Against the real model, removing a child from a
/// required collection threw <c>InvalidOperationException</c> — "the association between entity
/// types … has been severed" — out of <c>SaveChangesAsync</c>, so a dealer removing a vehicle photo
/// and a customer un-hearting a saved car each got a 500, every time, on buttons both screens offer.
/// </para>
/// <para>
/// What makes these tests able to fail is the SECOND context. An orphan that was <c>Added</c> in the
/// same context is silently detached when it is severed; only one loaded as <c>Unchanged</c> takes
/// the path that threw. A test that adds and removes in one context passes against the broken
/// mapping and proves nothing.
/// </para>
/// </remarks>
public sealed class RemovingAChildFromAnAggregateTests : IDisposable
{
    private static readonly DateTimeOffset Now = Build.Now;

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public RemovingAChildFromAnAggregateTests()
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

    private static UnitOfWork UnitOfWorkOn(KhadraDbContext context) =>
        new(context, Substitute.For<IDomainEventDispatcher>());

    // ---------------------------------------------------------------- vehicle photographs

    /// <summary>Three photographs on one car, saved and forgotten about.</summary>
    private async Task<Vehicle> GivenACarWithThreePhotosAsync()
    {
        await using var context = NewContext();
        var vehicle = Build.Vehicle();
        for (var index = 0; index < 3; index++)
            Assert.True(vehicle.AddImage($"vehicles/{vehicle.Id}/photo-{index}.jpg", Now).IsSuccess);

        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        return vehicle;
    }

    [Fact]
    public async Task Removing_a_photo_deletes_its_row_rather_than_throwing()
    {
        var saved = await GivenACarWithThreePhotosAsync();
        var doomed = saved.Images.Single(image => image.Position == 1);

        await using (var context = NewContext())
        {
            var vehicle = await new VehicleRepository(context).GetByIdAsync(saved.Id);
            Assert.NotNull(vehicle);
            Assert.True(vehicle.RemoveImage(doomed.Id).IsSuccess);
            // This is the line that used to throw.
            await UnitOfWorkOn(context).SaveChangesAsync();
        }

        await using var reader = NewContext();
        var rows = await reader.Set<VehicleImage>()
            .AsNoTracking()
            .Where(image => image.VehicleId == saved.Id)
            .OrderBy(image => image.Position)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, image => image.Id == doomed.Id);
        // Reindex reached the database, so the positions are still 0..n-1 with no hole.
        Assert.Equal([0, 1], rows.Select(image => image.Position));
        // And the car itself is untouched: this removes a photo, not a listing.
        Assert.NotNull(await reader.Vehicles.AsNoTracking().SingleOrDefaultAsync(car => car.Id == saved.Id));
    }

    /// <summary>
    /// The cover photo leaving promotes the next one, and the promotion is PERSISTED.
    /// </summary>
    /// <remarks>
    /// Promotion is by position, not by the order the repository happened to return the rows in.
    /// That distinction is invisible in memory, where list order and position order agree, and it is
    /// exactly what this proves: the surviving primary is the one the customer sees first.
    /// </remarks>
    [Fact]
    public async Task Removing_the_cover_photo_promotes_the_next_one_by_position()
    {
        var saved = await GivenACarWithThreePhotosAsync();
        var cover = saved.Images.Single(image => image.IsPrimary);
        Assert.Equal(0, cover.Position);

        await using (var context = NewContext())
        {
            var vehicle = await new VehicleRepository(context).GetByIdAsync(saved.Id);
            Assert.True(vehicle!.RemoveImage(cover.Id).IsSuccess);
            await UnitOfWorkOn(context).SaveChangesAsync();
        }

        await using var reader = NewContext();
        var rows = await reader.Set<VehicleImage>()
            .AsNoTracking()
            .Where(image => image.VehicleId == saved.Id)
            .OrderBy(image => image.Position)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Single(rows, image => image.IsPrimary);
        Assert.True(rows[0].IsPrimary);
    }

    /// <summary>
    /// A published listing keeps its last photograph.
    /// </summary>
    /// <remarks>
    /// Publish refuses a car with no photo, so this is that rule read from the other end. It could
    /// not be reached before, because removal always failed; the fix is what makes it matter.
    /// </remarks>
    [Fact]
    public async Task A_published_listing_will_not_give_up_its_last_photo()
    {
        Id vehicleId;
        Id imageId;
        await using (var context = NewContext())
        {
            var vehicle = Build.Vehicle();
            var image = vehicle.AddImage($"vehicles/{vehicle.Id}/only.jpg", Now).Value;
            Assert.True(vehicle.Publish(dealerCanTrade: true, Now).IsSuccess);
            context.Vehicles.Add(vehicle);
            await context.SaveChangesAsync();
            (vehicleId, imageId) = (vehicle.Id, image.Id);
        }

        await using (var context = NewContext())
        {
            var vehicle = await new VehicleRepository(context).GetByIdAsync(vehicleId);
            var refused = vehicle!.RemoveImage(imageId);

            Assert.True(refused.IsFailure);
            Assert.Equal(FleetErrors.LastImageOfPublishedVehicle.Code, refused.Error.Code);
            await UnitOfWorkOn(context).SaveChangesAsync();
        }

        await using var reader = NewContext();
        Assert.Equal(
            1,
            await reader.Set<VehicleImage>().CountAsync(image => image.VehicleId == vehicleId));
    }

    /// <summary>A hidden listing may be stripped bare; only a LIVE one may not.</summary>
    [Fact]
    public async Task A_listing_that_is_not_published_may_lose_its_last_photo()
    {
        Id vehicleId;
        Id imageId;
        await using (var context = NewContext())
        {
            var vehicle = Build.Vehicle();
            var image = vehicle.AddImage($"vehicles/{vehicle.Id}/only.jpg", Now).Value;
            context.Vehicles.Add(vehicle);
            await context.SaveChangesAsync();
            (vehicleId, imageId) = (vehicle.Id, image.Id);
        }

        await using (var context = NewContext())
        {
            var vehicle = await new VehicleRepository(context).GetByIdAsync(vehicleId);
            Assert.True(vehicle!.RemoveImage(imageId).IsSuccess);
            await UnitOfWorkOn(context).SaveChangesAsync();
        }

        await using var reader = NewContext();
        Assert.Equal(
            0,
            await reader.Set<VehicleImage>().CountAsync(image => image.VehicleId == vehicleId));
    }

    // ---------------------------------------------------------------- saved cars

    [Fact]
    public async Task Un_saving_a_car_deletes_its_row_rather_than_throwing()
    {
        var customerId = Id.New();
        var kept = Id.New();
        var dropped = Id.New();

        await using (var context = NewContext())
        {
            var shortlist = CustomerShortlist.Start(customerId, Now);
            Assert.True(shortlist.Add(dropped, TestBusinessRules.MaxShortlistEntries, Now).IsSuccess);
            Assert.True(shortlist.Add(kept, TestBusinessRules.MaxShortlistEntries, Now.AddMinutes(1)).IsSuccess);
            context.Shortlists.Add(shortlist);
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var shortlist = await new ShortlistRepository(context).GetAsync(customerId);
            Assert.NotNull(shortlist);
            shortlist.Remove(dropped);
            // This is the line that used to throw.
            await UnitOfWorkOn(context).SaveChangesAsync();
        }

        await using var reader = NewContext();
        var rows = await reader.Set<ShortlistEntry>()
            .AsNoTracking()
            .Where(entry => entry.ShortlistId == customerId)
            .ToListAsync();

        Assert.Single(rows);
        Assert.Equal(kept, rows[0].VehicleId);
        // The list itself survives being emptied of one car.
        Assert.NotNull(await reader.Shortlists.AsNoTracking().SingleOrDefaultAsync(list => list.Id == customerId));
    }

    [Fact]
    public async Task Un_saving_the_last_car_leaves_an_empty_list_rather_than_no_list()
    {
        var customerId = Id.New();
        var only = Id.New();

        await using (var context = NewContext())
        {
            var shortlist = CustomerShortlist.Start(customerId, Now);
            Assert.True(shortlist.Add(only, TestBusinessRules.MaxShortlistEntries, Now).IsSuccess);
            context.Shortlists.Add(shortlist);
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var shortlist = await new ShortlistRepository(context).GetAsync(customerId);
            shortlist!.Remove(only);
            await UnitOfWorkOn(context).SaveChangesAsync();
        }

        await using var reader = NewContext();
        Assert.Equal(0, await reader.Set<ShortlistEntry>().CountAsync(entry => entry.ShortlistId == customerId));
        Assert.NotNull(await reader.Shortlists.AsNoTracking().SingleOrDefaultAsync(list => list.Id == customerId));
    }

    // ---------------------------------------------------------------- a dealer's documents

    /// <summary>
    /// Re-uploading a document type replaces the old file, on a dealer loaded from the database (pre-launch item 121).
    /// </summary>
    /// <remarks>
    /// <c>AttachDocument</c> removes the old document of that type before adding the new one. Its only caller ran on a
    /// brand-new dealer, where nothing is tracked, so the <c>Restrict</c> mapping never threw — but a re-upload after a
    /// clarification request loads a tracked dealer, and the first save would have been a 500.
    /// </remarks>
    [Fact]
    public async Task Replacing_a_dealers_document_deletes_the_old_row_rather_than_throwing()
    {
        Id dealerId;
        await using (var context = NewContext())
        {
            var dealer = Build.Dealer();
            Build.AttachAllDocuments(dealer);
            context.Dealers.Add(dealer);
            await context.SaveChangesAsync();
            dealerId = dealer.Id;
        }

        await using (var context = NewContext())
        {
            var dealer = await new DealerRepository(context).GetByIdAsync(dealerId);
            Assert.True(dealer!.AttachDocument(DealerDocumentType.CommercialRegistration, "dealers/replaced.pdf", Now).IsSuccess);
            await UnitOfWorkOn(context).SaveChangesAsync();
        }

        await using var reader = NewContext();
        var registrations = await reader.Set<DealerDocument>()
            .AsNoTracking()
            .Where(document => document.DealerId == dealerId && document.Type == DealerDocumentType.CommercialRegistration)
            .ToListAsync();
        Assert.Equal("dealers/replaced.pdf", Assert.Single(registrations).StorageKey);
        Assert.Equal(DealerDocumentType.Required.Count, await reader.Set<DealerDocument>().CountAsync(document => document.DealerId == dealerId));
    }

    // ---------------------------------------------------------------- the rule itself

    /// <summary>
    /// The platform's rule, pinned in the model rather than in prose.
    /// </summary>
    /// <remarks>
    /// Every foreign key is <c>ON DELETE RESTRICT</c> in the database — that half has never moved,
    /// and <c>ClientCascade</c> emits exactly the same constraint. What the second half says is that
    /// a child collection an aggregate REMOVES from is mapped <c>ClientCascade</c>, so EF deletes the
    /// orphan instead of trying to null a foreign key that cannot be null. Writing <c>Restrict</c>
    /// there is not stricter, it is a 500.
    /// </remarks>
    [Fact]
    public void No_foreign_key_cascades_in_the_database_and_removable_children_cascade_in_the_client()
    {
        using var context = NewContext();
        var model = context.Model.GetRelationalModel();

        var cascading = model.Tables
            .SelectMany(table => table.ForeignKeyConstraints)
            .Where(constraint => constraint.OnDeleteAction is not (ReferentialAction.Restrict or ReferentialAction.NoAction))
            .Select(constraint => $"{constraint.Table.Name}.{constraint.Name} -> {constraint.OnDeleteAction}")
            .ToList();

        Assert.Empty(cascading);

        Assert.Equal(DeleteBehavior.ClientCascade, DeleteBehaviourOf<Vehicle>(context, nameof(Vehicle.Images)));
        Assert.Equal(DeleteBehavior.ClientCascade, DeleteBehaviourOf<CustomerShortlist>(context, "_entries"));
    }

    /// <summary>
    /// Every required child collection an aggregate owns is <c>ClientCascade</c> (pre-launch item 121), so the next
    /// aggregate that removes a child is not a 500 waiting for its first customer.
    /// </summary>
    /// <remarks>
    /// Two kinds are left alone, each for a reason a test can state. An <see cref="IAppendOnly"/> child is never removed
    /// at all — the guard in <c>KhadraDbContext</c> refuses it — so the mapping is never reached. And
    /// <see cref="RestrictedByDecision"/> names the collections whose mapping belongs to a context that needs the owner's
    /// approval to change; each is one its aggregate never removes from.
    /// </remarks>
    [Fact]
    public void Every_child_collection_an_aggregate_owns_deletes_its_orphans()
    {
        using var context = NewContext();

        var restrictive = context.Model.GetEntityTypes()
            .Where(type => typeof(AggregateRoot).IsAssignableFrom(type.ClrType))
            .SelectMany(type => type.GetNavigations())
            .Where(navigation => navigation.IsCollection && !navigation.IsOnDependent)
            .Where(navigation => navigation.ForeignKey.IsRequired && !navigation.ForeignKey.IsOwnership)
            .Where(navigation => !typeof(IAppendOnly).IsAssignableFrom(navigation.TargetEntityType.ClrType))
            .Select(navigation => (Name: $"{navigation.DeclaringEntityType.ClrType.Name}.{navigation.Name}", navigation.ForeignKey.DeleteBehavior))
            .Where(child => child.DeleteBehavior != DeleteBehavior.ClientCascade && !RestrictedByDecision.Contains(child.Name))
            .Select(child => $"{child.Name} -> {child.DeleteBehavior}")
            .ToList();

        Assert.Empty(restrictive);
    }

    /// <summary>
    /// <c>Payment.Refunds</c>: the Payments context, which is not changed without the owner (CLAUDE.md). A refund is
    /// recorded and settled on its payment and never taken off it.
    /// </summary>
    private static readonly HashSet<string> RestrictedByDecision = ["Payment.Refunds"];

    private static DeleteBehavior DeleteBehaviourOf<TAggregate>(KhadraDbContext context, string navigation) =>
        context.Model
            .FindEntityType(typeof(TAggregate))!
            .FindNavigation(navigation)!
            .ForeignKey
            .DeleteBehavior;
}
