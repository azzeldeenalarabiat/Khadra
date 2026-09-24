using Khadra.Application.Common;
using Khadra.Application.Fleet.BrowseCatalogue;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Fleet;
using Khadra.Domain.Reviews;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// A gallery's rating on every public surface that shows one, through the real handlers and readers.
/// </summary>
/// <remarks>
/// The defect this pins (2026-09-24): the catalogue reader averaged the reviews table itself, in three
/// places, and none of them honoured the blind window. A one-star still inside
/// <c>Review.VisibleFrom</c> moved the office's card, its page and the gallery embedded in every car,
/// while the review list rightly showed nothing — so a gallery could watch its own average and learn a
/// rating before the customer could read theirs. The rating now has one definition,
/// <c>IGalleryReviewReader.SummariseAsync</c>, and these tests hold every surface to it.
/// </remarks>
public sealed class CatalogueRatingsTests : IDisposable
{
    private static readonly DateTimeOffset Now = Build.Now;

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;
    private readonly TestClock _clock = new(Now);
    private readonly Dealer _office = Build.ApprovedDealer(businessName: "Rated Rentals", commercialRegistration: "910001");
    private readonly Vehicle _car;

    public CatalogueRatingsTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;

        _office.ClearDomainEvents();
        _car = Build.Vehicle(_office.Id, plateNumber: "91-10001");
        _car.AddImage("cars/front.jpg", Now);
        _car.Publish(dealerCanTrade: true, Now);
        _car.ClearDomainEvents();

        using var context = new KhadraDbContext(_options);
        context.Database.EnsureCreated();
        context.Dealers.Add(_office);
        context.Vehicles.Add(_car);
        context.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private KhadraDbContext NewContext() => new(_options);

    private static PageRequest Page => PageRequest.From(1, 50);

    /// <summary>A customer's rating of the office, published at <paramref name="visibleFrom"/>.</summary>
    private async Task Rate(int stars, DateTimeOffset visibleFrom, bool hidden = false)
    {
        var review = Review.Leave(
            Id.New(),
            ReviewDirection.CustomerRatesDealer,
            Id.New(),
            _office.Id,
            Rating.Create(stars).Value,
            comment: "A comment.",
            bookingIsCompleted: true,
            revealAt: visibleFrom,
            now: visibleFrom.AddDays(-1)).Value;
        if (hidden)
            review.Hide("Abusive.");

        await using var context = NewContext();
        context.Reviews.Add(review);
        await context.SaveChangesAsync();
    }

    /// <summary>The four surfaces a gallery's rating appears on, each through its own handler.</summary>
    private async Task<(decimal? Average, int Count)[]> ReadAllSurfacesAsync()
    {
        await using var context = NewContext();
        var catalogue = new CatalogueReader(context);
        var reviews = new GalleryReviewReader(context);

        var directory = await new ListPublicGalleriesHandler(catalogue, reviews, _clock)
            .Handle(new ListPublicGalleriesQuery(null, Page), CancellationToken.None);
        var card = directory.Value.Items.Single();

        var search = await new SearchCatalogueHandler(
                catalogue, reviews, TestBusinessRules.Provider(), TestBusinessRules.Calendar(), _clock)
            .Handle(
                new SearchCatalogueQuery(null, null, null, null, null, null, null, false, null, null, null, Page),
                CancellationToken.None);
        var label = search.Value.Items.Single().Gallery;

        var page = (await new GetPublicGalleryHandler(catalogue, reviews, _clock)
            .Handle(new GetPublicGalleryQuery(_office.Id, Language.English), CancellationToken.None)).Value;

        var vehicle = (await new GetCatalogueVehicleHandler(
                catalogue, reviews, TestBusinessRules.Provider(), TestBusinessRules.Calendar(), _clock)
            .Handle(new GetCatalogueVehicleQuery(_car.Id, null, null, Language.English), CancellationToken.None)).Value;

        return
        [
            (card.AverageRating, card.ReviewCount),
            (label.AverageRating, label.ReviewCount),
            (page.AverageRating, page.ReviewCount),
            (vehicle.Gallery.AverageRating, vehicle.Gallery.ReviewCount),
        ];
    }

    [Fact]
    public async Task A_rating_inside_its_blind_window_moves_nothing_until_it_is_published()
    {
        await Rate(2, visibleFrom: Now.AddDays(1));

        // Before the reveal: unrated, on every surface. Not 2.0 with a count of 1, which is what the
        // card, the listing, the page and the car all used to say.
        Assert.All(await ReadAllSurfacesAsync(), rating =>
        {
            Assert.Null(rating.Average);
            Assert.Equal(0, rating.Count);
        });

        // After it: the same review, now published, on every surface.
        _clock.Advance(TimeSpan.FromDays(2));
        Assert.All(await ReadAllSurfacesAsync(), rating =>
        {
            Assert.Equal(2.0m, rating.Average);
            Assert.Equal(1, rating.Count);
        });
    }

    [Fact]
    public async Task A_hidden_review_still_counts_toward_the_rating()
    {
        // Moderation removes the text, never the score (spec 3.2, 4.1).
        await Rate(5, visibleFrom: Now.AddHours(-1), hidden: true);
        await Rate(2, visibleFrom: Now.AddHours(-1));

        Assert.All(await ReadAllSurfacesAsync(), rating =>
        {
            Assert.Equal(3.5m, rating.Average);
            Assert.Equal(2, rating.Count);
        });
    }

    [Fact]
    public async Task The_count_on_the_page_is_the_number_of_reviews_the_page_lists()
    {
        await Rate(4, visibleFrom: Now.AddHours(-1));
        await Rate(1, visibleFrom: Now.AddHours(6));

        await using var context = NewContext();
        var catalogue = new CatalogueReader(context);
        var reviews = new GalleryReviewReader(context);

        var page = (await new GetPublicGalleryHandler(catalogue, reviews, _clock)
            .Handle(new GetPublicGalleryQuery(_office.Id, Language.English), CancellationToken.None)).Value;
        var listed = await reviews.ListForGalleryAsync(_office.Id, Page, _clock.UtcNow);

        Assert.Equal(1, page.ReviewCount);
        Assert.Equal(listed.TotalCount, page.ReviewCount);
    }

    [Fact]
    public async Task The_reader_on_its_own_never_states_a_rating()
    {
        // The catalogue reader is no longer a definition of the rating at all, published or not: a
        // caller that forgets the summary gets "unrated", never a number that ignored the window.
        await Rate(3, visibleFrom: Now.AddHours(-1));

        await using var context = NewContext();
        var catalogue = new CatalogueReader(context);

        var page = await catalogue.GetGalleryAsync(_office.Id, Language.English);
        Assert.NotNull(page);
        Assert.Null(page.AverageRating);
        Assert.Equal(0, page.ReviewCount);
    }
}
