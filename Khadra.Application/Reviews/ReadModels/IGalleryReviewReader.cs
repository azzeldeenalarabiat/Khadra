using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.Reviews;

namespace Khadra.Application.Reviews.ReadModels;

/// <summary>One customer's rating of a gallery, as anyone browsing that gallery sees it.</summary>
/// <remarks>
/// THERE IS NO REVIEWER NAME OR ID HERE, and that is the point of having a separate shape from
/// <see cref="ReviewDto"/>. A review is public; who left it is not something this platform has asked
/// a customer's permission to publish, and the pairing of a name with the dates of a rental is more
/// than either fact alone. A gallery that wants to know who said it can find the booking.
///
/// A hidden review keeps its RATING and loses its text (spec 3.2, 4.1): the score still counts
/// towards the average, so reporting a comment cannot erase a bad score. <see cref="IsHidden"/>
/// travels so a screen can say a comment was removed rather than silently showing a bare star.
/// </remarks>
public sealed record GalleryReviewDto(
    Guid ReviewId,
    int Rating,
    string? Comment,
    bool IsHidden,
    DateTimeOffset CreatedAt);

/// <summary>A review as the person who wrote it sees it.</summary>
public sealed record ReviewDto(
    Guid ReviewId,
    Guid BookingId,
    int Rating,
    string? Comment,
    bool IsHidden,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    public static ReviewDto From(Review review)
    {
        ArgumentNullException.ThrowIfNull(review);
        return new ReviewDto(
            review.Id.Value,
            review.BookingId.Value,
            review.Rating.Value,
            review.Comment,
            review.IsHidden,
            review.CreatedAt,
            review.UpdatedAt);
    }
}

/// <summary>A subject's rating, as SQL computed it.</summary>
/// <remarks>
/// <see cref="Average"/> is null when there are no reviews, never 0. Zero is a real rating on a
/// one-to-five scale and would render as the worst possible score for a gallery nobody has rated.
/// </remarks>
public sealed record RatingSummary(decimal? Average, int Count)
{
    public static readonly RatingSummary None = new(null, 0);
}

public interface IGalleryReviewReader
{
    Task<PagedResult<GalleryReviewDto>> ListForGalleryAsync(
        Id dealerId,
        PageRequest page,
        CancellationToken cancellationToken = default);

    Task<ReviewDto?> FindForBookingAsync(
        Id bookingId,
        ReviewDirection direction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ratings for many galleries in ONE query.
    /// </summary>
    /// <remarks>
    /// The catalogue shows a rating on every card, and asking per card is the N+1 that would make a
    /// twenty-result page twenty-one round trips. Galleries with no reviews are simply absent from
    /// the result rather than present with zeros, so a caller reads a missing key as "not rated".
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, RatingSummary>> SummariseAsync(
        IReadOnlyCollection<Id> dealerIds,
        CancellationToken cancellationToken = default);
}
