using Khadra.Application.Common.Dtos;
using Khadra.Application.Common.Ports;
using Khadra.Application.Fleet.ReadModels;
using Khadra.Application.Shortlist;
using Khadra.Application.Shortlist.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Shortlist;
using Khadra.Domain.Shortlist.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Shortlist;

/// <summary>
/// Saving and forgetting a car.
/// </summary>
/// <remarks>
/// The test that matters most here is the one about a car the customer may not see. Saving is the
/// cheapest enumeration oracle this platform has: "saved" on an id the public catalogue answers 404
/// to would confirm the id exists, and anyone with an account could walk a competitor's unpublished
/// inventory a request at a time. The handler therefore asks the CATALOGUE's own reader, whose
/// predicate is the one the search uses, rather than checking visibility itself.
/// </remarks>
public sealed class ShortlistUseCaseTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    private sealed class Context
    {
        public IShortlistRepository Shortlists { get; } = Substitute.For<IShortlistRepository>();
        public ICatalogueReader Catalogue { get; } = Substitute.For<ICatalogueReader>();
        public IShortlistReader Reader { get; } = Substitute.For<IShortlistReader>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public Id CustomerId { get; } = Id.New();
        public CustomerShortlist? Stored { get; set; }

        public Context()
        {
            Shortlists.GetAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(Stored));
            Shortlists.When(repository => repository.Add(Arg.Any<CustomerShortlist>()))
                .Do(call => Stored = call.Arg<CustomerShortlist>());
        }

        /// <summary>
        /// Makes this car one the customer may see.
        /// </summary>
        /// <remarks>
        /// Only NULL-or-not matters to this handler: it asks the catalogue whether the customer may
        /// see the car and does nothing else with the answer. The values are the smallest thing that
        /// constructs a visible one.
        /// </remarks>
        public void Publish(Id vehicleId) =>
            Catalogue
                .GetAsync(vehicleId, Arg.Any<AvailabilityWindow?>(), Arg.Any<CancellationToken>())
                .Returns(Visible(vehicleId));

        private static CatalogueVehicle Visible(Id vehicleId) => new(
            vehicleId.Value,
            "Toyota",
            "Corolla",
            2024,
            null,
            null,
            null,
            "Automatic",
            "Petrol",
            5,
            new MoneyDto(35m, "JOD"),
            new MoneyDto(200m, "JOD"),
            new MileagePolicyView(true, null, null),
            "FullToFull",
            false,
            [],
            new PublicGallery(
                Id.New().Value,
                "A gallery",
                null,
                null,
                31.95,
                35.91,
                null,
                null,
                [],
                new GalleryDelivery(false, 0m, null),
                null,
                0),
            null);

        public ShortlistHandlers Handlers(int maxEntries = TestBusinessRules.MaxShortlistEntries) =>
            new(Shortlists,
                Catalogue,
                Reader,
                TestBusinessRules.Provider(maxShortlistEntries: maxEntries),
                new TestClock(Now),
                UnitOfWork);
    }

    [Fact]
    public async Task Saving_a_car_starts_a_list_on_the_first_one()
    {
        var context = new Context();
        var car = Id.New();
        context.Publish(car);

        var result = await context.Handlers()
            .Handle(new SaveVehicleCommand(context.CustomerId, car), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(context.Stored);
        // Keyed BY the customer, so a second first-save collides on the primary key rather than
        // creating a second list.
        Assert.Equal(context.CustomerId, context.Stored.Id);
        Assert.True(context.Stored.Contains(car));
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The enumeration guard, and the reason the visibility check is not written here.
    /// </summary>
    [Fact]
    public async Task A_car_the_customer_cannot_see_is_refused_the_way_the_catalogue_refuses_it()
    {
        var context = new Context();
        // Not published: the catalogue reader answers null, exactly as it does for a draft, a hidden
        // car, one in maintenance, a suspended gallery's, a deleted one and an unknown id.
        var invisible = Id.New();

        var result = await context.Handlers()
            .Handle(new SaveVehicleCommand(context.CustomerId, invisible), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("shortlist.vehicle_not_found", result.Error.Code);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Null(context.Stored);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Saving_what_is_already_saved_changes_nothing_and_succeeds()
    {
        var context = new Context();
        var car = Id.New();
        context.Publish(car);

        await context.Handlers()
            .Handle(new SaveVehicleCommand(context.CustomerId, car), CancellationToken.None);
        var again = await context.Handlers()
            .Handle(new SaveVehicleCommand(context.CustomerId, car), CancellationToken.None);

        Assert.True(again.IsSuccess);
        Assert.Equal(1, context.Stored!.Count);
    }

    [Fact]
    public async Task A_full_list_refuses_and_the_refusal_names_the_platforms_figure()
    {
        var context = new Context();
        var first = Id.New();
        var second = Id.New();
        context.Publish(first);
        context.Publish(second);

        await context.Handlers(maxEntries: 1)
            .Handle(new SaveVehicleCommand(context.CustomerId, first), CancellationToken.None);
        var refused = await context.Handlers(maxEntries: 1)
            .Handle(new SaveVehicleCommand(context.CustomerId, second), CancellationToken.None);

        Assert.True(refused.IsFailure);
        Assert.Equal("shortlist.full", refused.Error.Code);
        Assert.Contains("1", refused.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Forgetting does NOT check that the car is still visible.
    /// </summary>
    /// <remarks>
    /// An entry for a listing that has since been withdrawn is precisely the one a customer most
    /// wants gone, and a visibility check here would trap it on their list for ever.
    /// </remarks>
    [Fact]
    public async Task A_car_that_is_no_longer_listed_can_still_be_removed()
    {
        var context = new Context();
        var car = Id.New();
        context.Publish(car);
        await context.Handlers()
            .Handle(new SaveVehicleCommand(context.CustomerId, car), CancellationToken.None);

        // The gallery withdraws it: the catalogue stops answering for that id.
        context.Catalogue
            .GetAsync(car, Arg.Any<AvailabilityWindow?>(), Arg.Any<CancellationToken>())
            .Returns((CatalogueVehicle?)null);

        var result = await context.Handlers()
            .Handle(new ForgetVehicleCommand(context.CustomerId, car), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, context.Stored!.Count);
    }

    [Fact]
    public async Task Forgetting_something_never_saved_succeeds_and_writes_nothing()
    {
        var context = new Context();

        var result = await context.Handlers()
            .Handle(new ForgetVehicleCommand(context.CustomerId, Id.New()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>The heart-per-card query answers only about ids the caller named.</summary>
    [Fact]
    public async Task Membership_asks_the_repository_only_about_the_ids_it_was_given()
    {
        var context = new Context();
        var saved = Id.New();
        var notSaved = Id.New();

        context.Shortlists
            .SavedAmongAsync(context.CustomerId, Arg.Any<IReadOnlyCollection<Id>>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Id> { saved });

        var result = await context.Handlers().Handle(
            new WhichAreSavedQuery(context.CustomerId, [saved, notSaved]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(saved, result.Value);
        Assert.DoesNotContain(notSaved, result.Value);
    }

    [Fact]
    public async Task An_empty_membership_question_asks_the_database_nothing()
    {
        var context = new Context();

        var result = await context.Handlers().Handle(
            new WhichAreSavedQuery(context.CustomerId, []), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        await context.Shortlists.DidNotReceive().SavedAmongAsync(
            Arg.Any<Id>(), Arg.Any<IReadOnlyCollection<Id>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// What a saved car's name is allowed to carry, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A reflection test, because the thing being protected is a SHAPE. The name exists so a car the
    /// customer can no longer book is still recognisable to them; every field anybody might add to it
    /// next undoes something the rest of this context is built on:
    /// </para>
    /// <list type="bullet">
    /// <item>a reason or a status would distinguish hidden from deleted from suspended, which the
    /// catalogue, <c>ShortlistErrors</c> and <c>SaveVehicleCommand</c> all refuse to do;</item>
    /// <item>an image URL would be a hidden car's photograph on the open internet, because vehicle
    /// images are served from static storage by key;</item>
    /// <item>a dealer id would invite a tap-through to a gallery page that answers 404;</item>
    /// <item>a price would be a figure about a car nobody can book.</item>
    /// </list>
    /// </remarks>
    [Fact]
    public void The_name_on_a_saved_car_carries_nothing_else()
    {
        var fields = typeof(SavedVehicleIdentity)
            .GetProperties()
            .Select(property => property.Name)
            // A positional record's compiler-generated member, not a field anybody declared.
            .Where(name => name != "EqualityContract")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["GalleryName", "Make", "Model", "Year"], fields);
    }
}
