using Khadra.Application.Fleet.ReadModels;
using Khadra.Application.Reviews.ReadModels;
using Khadra.Domain.Common;

namespace Khadra.Application.Fleet.BrowseCatalogue;

/// <summary>
/// Puts a gallery's PUBLISHED rating onto the catalogue records that carry one.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue reader used to average <c>context.Reviews</c> itself, in three places, and none of
/// them honoured the blind window: a one-star still inside <c>Review.VisibleFrom</c> moved the number
/// on the office's card, its page and every car it lists, while the review list beside it rightly
/// showed nothing. That is the leak the window exists to close — a gallery watching its own average
/// could tell a new rating had arrived, and roughly what it was, before the customer could see
/// theirs. Three private copies of "what is a gallery's rating" had drifted from the one that was
/// right.
/// </para>
/// <para>
/// So there is now ONE definition, <see cref="IGalleryReviewReader.SummariseAsync"/>, judged at the
/// clock's now, and the catalogue reader leaves every rating as null and 0. The handlers compose the
/// two, sequentially (the readers share a scoped DbContext), and these overloads are the only place
/// the answer is written onto a record. Keyed by dealer id; a gallery absent from the summary is
/// UNRATED — null and 0, never a zero-star score, which on a one-to-five scale would render as the
/// worst office on the platform.
/// </para>
/// <para>
/// Hidden reviews still count: moderation removes abusive text, never the score (spec 3.2, 4.1).
/// That rule lives in the summary, not here, so it cannot be decided twice.
/// </para>
/// </remarks>
public static class CatalogueRatings
{
    /// <summary>
    /// The published ratings of these galleries, or an empty map without a round trip when there are none.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, RatingSummary>> ForAsync(
        IGalleryReviewReader reviews,
        IEnumerable<Guid> dealerIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reviews);
        ArgumentNullException.ThrowIfNull(dealerIds);

        var ids = dealerIds.Distinct().Select(Id.From).ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, RatingSummary>();

        return await reviews.SummariseAsync(ids, now, cancellationToken);
    }

    public static CatalogueListing Apply(CatalogueListing listing, IReadOnlyDictionary<Guid, RatingSummary> ratings)
    {
        ArgumentNullException.ThrowIfNull(listing);
        var rating = Of(listing.Gallery.DealerId, ratings);
        return listing with
        {
            Gallery = listing.Gallery with { AverageRating = rating.Average, ReviewCount = rating.Count },
        };
    }

    public static PublicGalleryCard Apply(PublicGalleryCard card, IReadOnlyDictionary<Guid, RatingSummary> ratings)
    {
        ArgumentNullException.ThrowIfNull(card);
        var rating = Of(card.DealerId, ratings);
        return card with { AverageRating = rating.Average, ReviewCount = rating.Count };
    }

    public static PublicGalleryPage Apply(PublicGalleryPage page, IReadOnlyDictionary<Guid, RatingSummary> ratings)
    {
        ArgumentNullException.ThrowIfNull(page);
        var rating = Of(page.DealerId, ratings);
        return page with { AverageRating = rating.Average, ReviewCount = rating.Count };
    }

    /// <summary>The car itself is not rated — the platform rates offices — so only its embedded gallery changes.</summary>
    public static CatalogueVehicle Apply(CatalogueVehicle vehicle, IReadOnlyDictionary<Guid, RatingSummary> ratings)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        var rating = Of(vehicle.Gallery.DealerId, ratings);
        return vehicle with
        {
            Gallery = vehicle.Gallery with { AverageRating = rating.Average, ReviewCount = rating.Count },
        };
    }

    private static RatingSummary Of(Guid dealerId, IReadOnlyDictionary<Guid, RatingSummary> ratings)
    {
        ArgumentNullException.ThrowIfNull(ratings);
        return ratings.TryGetValue(dealerId, out var rating) ? rating : RatingSummary.None;
    }
}
