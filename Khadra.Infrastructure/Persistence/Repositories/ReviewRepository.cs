using Khadra.Domain.Common;
using Khadra.Domain.Reviews;
using Khadra.Domain.Reviews.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence.Repositories;

internal sealed class ReviewRepository(KhadraDbContext context) : IReviewRepository
{
    public Task<Review?> GetByIdAsync(Id id, CancellationToken cancellationToken = default) =>
        context.Reviews.FirstOrDefaultAsync(review => review.Id == id, cancellationToken);

    public Task<bool> ExistsForBookingAsync(
        Id bookingId,
        ReviewDirection direction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(direction);
        return context.Reviews.AnyAsync(
            review => review.BookingId == bookingId && review.Direction == direction,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Review>> ListForSubjectAsync(
        Id subjectId,
        ReviewDirection direction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(direction);
        return await context.Reviews
            .Where(review => review.SubjectId == subjectId && review.Direction == direction)
            .OrderByDescending(review => review.CreatedAt)
            .ThenByDescending(review => review.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The average and the count, computed by the database.
    /// </summary>
    /// <remarks>
    /// HIDDEN REVIEWS STILL COUNT. Spec 4.1 makes a dealer's rating something they can never edit,
    /// and moderation under spec 3.2 hides abusive TEXT — excluding the score with it would hand a
    /// dealer a way to erase a bad rating by reporting the comment attached to it.
    ///
    /// Aggregated in SQL rather than by loading the rows: a gallery with two thousand reviews would
    /// otherwise materialise all of them to produce one number, on a query the catalogue runs for
    /// every card on the screen.
    /// </remarks>
    public async Task<(double Average, int Count)> GetRatingSummaryAsync(
        Id subjectId,
        ReviewDirection direction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(direction);

        // Two aggregates rather than one GroupBy(_ => 1) projecting both. That shape translates on
        // Postgres and not on the SQLite the persistence tests run against, so it would ship a query
        // that only ever fails in production.
        var rated = context.Reviews
            .Where(review => review.SubjectId == subjectId && review.Direction == direction);

        var count = await rated.CountAsync(cancellationToken);
        if (count == 0)
            return (0d, 0);

        return (await rated.AverageAsync(review => (double)review.Rating.Value, cancellationToken), count);
    }

    public async Task AddAsync(Review review, CancellationToken cancellationToken = default) =>
        await context.Reviews.AddAsync(review, cancellationToken);
}
