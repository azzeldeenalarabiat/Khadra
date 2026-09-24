using Khadra.Application.Common;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Common.Ports;
using Khadra.Application.Fleet.BrowseCatalogue;
using Khadra.Application.Fleet.ReadModels;
using Khadra.Application.Reviews.ReadModels;
using Khadra.Application.Shortlist;
using Khadra.Application.Shortlist.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Shortlist.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Fleet;

/// <summary>
/// Every handler that returns a gallery rating takes it from the review summary, at the clock's now,
/// and writes it onto the right gallery.
/// </summary>
/// <remarks>
/// The catalogue reader's own ratings are placeholders now (see <c>CatalogueRatings</c>), so each test
/// hands the handler a record carrying a WRONG rating from the reader and holds it to the summary's:
/// a handler that forgot to compose would pass the placeholder straight through and fail here.
/// </remarks>
public sealed class CatalogueRatingsHandlerTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    private readonly ICatalogueReader _catalogue = Substitute.For<ICatalogueReader>();
    private readonly IGalleryReviewReader _reviews = Substitute.For<IGalleryReviewReader>();
    private readonly TestClock _clock = new(Now);
    private readonly Guid _rated = Guid.NewGuid();
    private readonly Guid _unrated = Guid.NewGuid();

    public CatalogueRatingsHandlerTests()
    {
        // Only the rated gallery is in the summary; the other is absent, which means "nobody has rated it".
        _reviews
            .SummariseAsync(Arg.Any<IReadOnlyCollection<Id>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, RatingSummary> { [_rated] = new(4.5m, 2) });
    }

    // A rating the reader must never be trusted for: if it reaches the response, the handler skipped
    // the summary.
    private const decimal Stale = 1.0m;
    private const int StaleCount = 9;

    private static PageRequest Page => PageRequest.From(1, 20);

    private static CatalogueListing Listing(Guid dealerId) => new(
        Guid.NewGuid(), "Toyota", "Corolla", 2024, null, "Automatic", "Petrol", 5, null,
        new MoneyDto(30m, "JOD"), false,
        new CatalogueGalleryLabel(dealerId, "Office", null, null, Stale, StaleCount));

    private static PublicGallery Embed(Guid dealerId) => new(
        dealerId, "Office", null, 31.95, 35.91, null, null, [], new GalleryDelivery(false, 0m, null), Stale, StaleCount);

    private static PublicGalleryCard Card(Guid dealerId) => new(
        dealerId, "Office", null, null, null, new GalleryDelivery(false, 0m, null), Stale, StaleCount, 3);

    private static CatalogueVehicle Vehicle(Guid dealerId) => new(
        Guid.NewGuid(), "Toyota", "Corolla", 2024, null, null, null, "Automatic", "Petrol", 5,
        new MoneyDto(30m, "JOD"), new MoneyDto(200m, "JOD"), new MileagePolicyView(true, null, null),
        "FullToFull", false, [], Embed(dealerId), null);

    private static PublicGalleryPage GalleryPage(Guid dealerId) => new(
        dealerId, "Office", null, null, 31.95, 35.91, null, null, [], new GalleryDelivery(false, 0m, null),
        Stale, StaleCount, new GallerySections(null, null, null, null, null, null));

    /// <summary>The summary was asked about exactly these galleries, at the clock's now.</summary>
    private void AssertSummarisedAt(params Guid[] dealerIds) =>
        _ = _reviews.Received(1).SummariseAsync(
            Arg.Is<IReadOnlyCollection<Id>>(ids => ids.Select(id => id.Value).Order().SequenceEqual(dealerIds.Order())),
            Now,
            Arg.Any<CancellationToken>());

    [Fact]
    public async Task Search_rates_each_listing_by_its_own_gallery()
    {
        _catalogue.SearchAsync(Arg.Any<CatalogueFilter>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<CatalogueListing>(
                [Listing(_rated), Listing(_unrated), Listing(_rated)], 1, 20, 3));

        var result = await new SearchCatalogueHandler(
                _catalogue, _reviews, TestBusinessRules.Provider(), TestBusinessRules.Calendar(), _clock)
            .Handle(
                new SearchCatalogueQuery(null, null, null, null, null, null, null, false, null, null, null, Page),
                CancellationToken.None);

        var items = result.Value.Items;
        Assert.Equal(((decimal?)4.5m, 2), (items[0].Gallery.AverageRating, items[0].Gallery.ReviewCount));
        Assert.Equal(((decimal?)null, 0), (items[1].Gallery.AverageRating, items[1].Gallery.ReviewCount));
        Assert.Equal(((decimal?)4.5m, 2), (items[2].Gallery.AverageRating, items[2].Gallery.ReviewCount));
        Assert.Equal(3, result.Value.TotalCount);
        // One summary for the whole page, each gallery named once.
        AssertSummarisedAt(_rated, _unrated);
    }

    [Fact]
    public async Task An_empty_page_asks_the_summary_nothing()
    {
        _catalogue.SearchAsync(Arg.Any<CatalogueFilter>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(PagedResult.Empty<CatalogueListing>(1, 20));

        var result = await new SearchCatalogueHandler(
                _catalogue, _reviews, TestBusinessRules.Provider(), TestBusinessRules.Calendar(), _clock)
            .Handle(
                new SearchCatalogueQuery(null, null, null, null, null, null, null, false, null, null, null, Page),
                CancellationToken.None);

        Assert.Empty(result.Value.Items);
        await _reviews.DidNotReceiveWithAnyArgs().SummariseAsync(default!, default, default);
    }

    [Fact]
    public async Task A_cars_embedded_gallery_carries_the_published_rating()
    {
        var vehicle = Vehicle(_rated);
        _catalogue.GetAsync(Arg.Any<Id>(), Arg.Any<AvailabilityWindow?>(), Arg.Any<Language>(), Arg.Any<CancellationToken>())
            .Returns(vehicle);

        var result = await new GetCatalogueVehicleHandler(
                _catalogue, _reviews, TestBusinessRules.Provider(), TestBusinessRules.Calendar(), _clock)
            .Handle(new GetCatalogueVehicleQuery(Id.From(vehicle.VehicleId), null, null, Language.English), CancellationToken.None);

        Assert.Equal(4.5m, result.Value.Gallery.AverageRating);
        Assert.Equal(2, result.Value.Gallery.ReviewCount);
        AssertSummarisedAt(_rated);
    }

    [Fact]
    public async Task A_gallery_page_nobody_has_rated_reads_unrated_whatever_the_reader_said()
    {
        _catalogue.GetGalleryAsync(Arg.Any<Id>(), Arg.Any<Language>(), Arg.Any<CancellationToken>())
            .Returns(GalleryPage(_unrated));

        var result = await new GetPublicGalleryHandler(_catalogue, _reviews, _clock)
            .Handle(new GetPublicGalleryQuery(Id.From(_unrated), Language.English), CancellationToken.None);

        Assert.Null(result.Value.AverageRating);
        Assert.Equal(0, result.Value.ReviewCount);
        AssertSummarisedAt(_unrated);
    }

    [Fact]
    public async Task A_gallery_page_carries_the_published_rating()
    {
        _catalogue.GetGalleryAsync(Arg.Any<Id>(), Arg.Any<Language>(), Arg.Any<CancellationToken>())
            .Returns(GalleryPage(_rated));

        var result = await new GetPublicGalleryHandler(_catalogue, _reviews, _clock)
            .Handle(new GetPublicGalleryQuery(Id.From(_rated), Language.English), CancellationToken.None);

        Assert.Equal(4.5m, result.Value.AverageRating);
        Assert.Equal(2, result.Value.ReviewCount);
    }

    [Fact]
    public async Task The_directory_rates_each_card_and_passes_the_filter_through()
    {
        _catalogue.ListGalleriesAsync(Arg.Any<GalleryDirectoryFilter>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<PublicGalleryCard>([Card(_unrated), Card(_rated)], 1, 20, 2));
        var city = Guid.NewGuid();

        var result = await new ListPublicGalleriesHandler(_catalogue, _reviews, _clock)
            .Handle(new ListPublicGalleriesQuery(city, Page, Text: "petra", DeliveryOnly: true), CancellationToken.None);

        Assert.Equal(((decimal?)null, 0), (result.Value.Items[0].AverageRating, result.Value.Items[0].ReviewCount));
        Assert.Equal(((decimal?)4.5m, 2), (result.Value.Items[1].AverageRating, result.Value.Items[1].ReviewCount));
        AssertSummarisedAt(_unrated, _rated);
        await _catalogue.Received(1).ListGalleriesAsync(
            Arg.Is<GalleryDirectoryFilter>(filter =>
                filter.CityId == Id.From(city) && filter.Text == "petra" && filter.DeliveryOnly),
            Page,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_saved_list_rates_the_cars_still_listed_and_leaves_the_rest_alone()
    {
        var reader = Substitute.For<IShortlistReader>();
        var customer = Id.New();
        var listed = new SavedVehicle(Guid.NewGuid(), Now, null, Listing(_rated));
        var gone = new SavedVehicle(Guid.NewGuid(), Now, new SavedVehicleIdentity("Kia", "Rio", 2020, "Office"), null);
        reader.ListAsync(customer, Arg.Any<CancellationToken>()).Returns([listed, gone]);

        var handlers = new ShortlistHandlers(
            Substitute.For<IShortlistRepository>(),
            _catalogue,
            reader,
            _reviews,
            TestBusinessRules.Provider(),
            _clock,
            Substitute.For<IUnitOfWork>());

        var result = await handlers.Handle(new ListMyShortlistQuery(customer), CancellationToken.None);

        Assert.Equal(2, result.Value.Count);
        Assert.Equal(4.5m, result.Value[0].Listing!.Gallery.AverageRating);
        Assert.Equal(2, result.Value[0].Listing!.Gallery.ReviewCount);
        // The unlisted entry keeps its row and its identity, and has no listing to rate.
        Assert.Same(gone, result.Value[1]);
        AssertSummarisedAt(_rated);
    }
}
