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

    /// <summary>
    /// The tracked review for one booking in one direction, so it can be CHANGED.
    /// </summary>
    /// <remarks>
    /// Distinct from the reader's <c>FindForBookingAsync</c>, which returns a DTO and therefore cannot
    /// be revealed. Writing the second half of a mutual review has to bring the first half forward in
    /// the same transaction, and that needs the aggregate.
    /// </remarks>
    Task<Review?> GetForBookingAsync(
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
