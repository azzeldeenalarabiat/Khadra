using Khadra.Domain.Common;

namespace Khadra.Domain.Reviews.Repositories;

public interface IReviewRepository
{
    Task<Review?> GetByIdAsync(Id id, CancellationToken cancellationToken = default);

    // Backed by a unique index on (booking_id, direction).
    Task<bool> ExistsForBookingAsync(
        Id bookingId,
        ReviewDirection direction,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Review>> ListForSubjectAsync(
        Id subjectId,
        ReviewDirection direction,
        CancellationToken cancellationToken = default);

    // Aggregated in SQL: never load every review just to average them.
    Task<(double Average, int Count)> GetRatingSummaryAsync(
        Id subjectId,
        ReviewDirection direction,
        CancellationToken cancellationToken = default);

    Task AddAsync(Review review, CancellationToken cancellationToken = default);
}
