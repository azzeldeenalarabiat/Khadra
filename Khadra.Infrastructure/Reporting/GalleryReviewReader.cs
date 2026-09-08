using Khadra.Application.Common;
using Khadra.Application.Reviews.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Reviews;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

internal sealed class GalleryReviewReader(KhadraDbContext context) : IGalleryReviewReader
{
    public async Task<PagedResult<GalleryReviewDto>> ListForGalleryAsync(
        Id dealerId,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        var query = context.Reviews
            .AsNoTracking()
            .Where(review =>
                review.SubjectId == dealerId &&
                review.Direction == ReviewDirection.CustomerRatesDealer);

        var total = await query.CountAsync(cancellationToken);

        // (created_at DESC, id DESC). created_at alone is not a total order, and a non-total order
        // lets a page boundary drop a row — the defect the audit log documents and orders around.
        var items = await query
            .OrderByDescending(review => review.CreatedAt)
            .ThenByDescending(review => review.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(review => new GalleryReviewDto(
                review.Id.Value,
                review.Rating.Value,
                // The text of a hidden review never leaves the database. Filtering it in the
                // projection rather than after it means a moderated comment is not even loaded.
                review.IsHidden ? null : review.Comment,
                review.IsHidden,
                review.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<GalleryReviewDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<ReviewDto?> FindForBookingAsync(
        Id bookingId,
        ReviewDirection direction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(direction);

        return await context.Reviews
            .AsNoTracking()
            .Where(review => review.BookingId == bookingId && review.Direction == direction)
            .Select(review => new ReviewDto(
                review.Id.Value,
                review.BookingId.Value,
                review.Rating.Value,
                review.Comment,
                review.IsHidden,
                review.CreatedAt,
                review.UpdatedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// One GROUP BY for every gallery on the page.
    /// </summary>
    /// <remarks>
    /// Hidden reviews are counted, deliberately: hiding removes abusive TEXT and never the score, or
    /// a gallery could erase a bad rating by reporting the comment attached to it (spec 3.2, 4.1).
    ///
    /// The average is rounded to one decimal HERE, in the reader, because a rating is displayed to
    /// one decimal and rounding it on the client would be a screen deciding a number. The count is
    /// exact.
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, RatingSummary>> SummariseAsync(
        IReadOnlyCollection<Id> dealerIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dealerIds);

        if (dealerIds.Count == 0)
            return new Dictionary<Guid, RatingSummary>();

        var ids = dealerIds.Distinct().ToList();

        var rows = await context.Reviews
            .AsNoTracking()
            .Where(review =>
                review.Direction == ReviewDirection.CustomerRatesDealer &&
                ids.Contains(review.SubjectId))
            .GroupBy(review => review.SubjectId)
            .Select(group => new
            {
                SubjectId = group.Key,
                Average = group.Average(review => (decimal)review.Rating.Value),
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            row => row.SubjectId.Value,
            row => new RatingSummary(Math.Round(row.Average, 1, MidpointRounding.AwayFromZero), row.Count));
    }
}
