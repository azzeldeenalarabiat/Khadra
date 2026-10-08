using Khadra.Application.Common;
using Khadra.Application.Reviews.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Reviews;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Reporting;

/// <summary>Both directions of review, for the administrator who moderates them (pre-launch item 81).</summary>
internal sealed class ReviewModerationReader(KhadraDbContext context) : IReviewModerationReader
{
    public async Task<PagedResult<ModerationReview>> ListAsync(
        ReviewModerationFilter filter,
        PageRequest page,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);

        var query = Filtered(filter);
        var total = await query.CountAsync(cancellationToken);
        if (total == 0)
            return PagedResult.Empty<ModerationReview>(page.Page, page.PageSize);

        // (created_at DESC, id DESC): a total order, so a page boundary never drops a row.
        var items = await Project(
                query
                    .OrderByDescending(review => review.CreatedAt)
                    .ThenByDescending(review => review.Id)
                    .Skip(page.Skip)
                    .Take(page.PageSize),
                now)
            .ToListAsync(cancellationToken);

        return new PagedResult<ModerationReview>(items, page.Page, page.PageSize, total);
    }

    public Task<ModerationReview?> GetAsync(Id reviewId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        Project(context.Reviews.AsNoTracking().Where(review => review.Id == reviewId), now)
            .FirstOrDefaultAsync(cancellationToken);

    private IQueryable<Review> Filtered(ReviewModerationFilter filter)
    {
        var query = context.Reviews.AsNoTracking();

        if (filter.Hidden is { } hidden)
            query = query.Where(review => review.IsHidden == hidden);
        if (filter.Direction is { } direction)
            query = query.Where(review => review.Direction == direction);
        if (filter.Rating is { } rating)
            query = query.Where(review => review.Rating.Value == rating);

        if (filter.Search is { } search)
        {
            // The comment, case-insensitively, or a booking by its exact reference — what an administrator pastes from a
            // ticket or an email. Names are not searched: a name is a person, and this screen lists what was SAID.
            var pattern = "%" + search
                .Replace(@"\", @"\\", StringComparison.Ordinal)
                .Replace("%", @"\%", StringComparison.Ordinal)
                .Replace("_", @"\_", StringComparison.Ordinal)
                .ToLowerInvariant() + "%";
            var reference = BookingReference.Create(search.ToUpperInvariant());

            // As CatalogueReader: ToLower here is an expression tree EF turns into SQL LOWER(), and the invariant form the
            // analyzer would accept is the call that does not translate.
#pragma warning disable CA1304, CA1311, CA1862
            query = reference.IsSuccess
                ? query.Where(review =>
                    (review.Comment != null && EF.Functions.Like(review.Comment.ToLower(), pattern, @"\")) ||
                    context.Bookings.Any(booking => booking.Id == review.BookingId && booking.Reference == reference.Value))
                : query.Where(review => review.Comment != null && EF.Functions.Like(review.Comment.ToLower(), pattern, @"\"));
#pragma warning restore CA1304, CA1311, CA1862
        }

        return query;
    }

    private IQueryable<ModerationReview> Project(IQueryable<Review> reviews, DateTimeOffset now)
    {
        var customerRatesDealer = ReviewDirection.CustomerRatesDealer;

        return reviews.Select(review => new ModerationReview(
            review.Id.Value,
            review.Direction.Name,
            review.Rating.Value,
            review.Comment,
            review.IsHidden,
            review.HiddenReason != null ? review.HiddenReason.Name : null,
            review.CreatedAt,
            review.VisibleFrom,
            review.VisibleFrom <= now,
            review.BookingId.Value,
            context.Bookings
                .Where(booking => booking.Id == review.BookingId)
                .Select(booking => booking.Reference.Value)
                .FirstOrDefault(),
            // The reviewer: the customer for a review of a gallery; the gallery itself — the office the booking was
            // with — for a rating of a customer.
            review.Direction == customerRatesDealer
                ? review.ReviewerUserId.Value
                : context.Bookings
                    .Where(booking => booking.Id == review.BookingId)
                    .Select(booking => booking.DealerId.Value)
                    .FirstOrDefault(),
            review.Direction == customerRatesDealer
                ? context.Users
                    .Where(user => user.Id == review.ReviewerUserId)
                    .Select(user => user.Name.Value)
                    .FirstOrDefault()
                : context.Bookings
                    .Where(booking => booking.Id == review.BookingId)
                    .SelectMany(booking => context.Dealers
                        .Where(dealer => dealer.Id == booking.DealerId)
                        .Select(dealer => dealer.BusinessName.Value))
                    .FirstOrDefault(),
            review.SubjectId.Value,
            review.Direction == customerRatesDealer
                ? context.Dealers
                    .Where(dealer => dealer.Id == review.SubjectId)
                    .Select(dealer => dealer.BusinessName.Value)
                    .FirstOrDefault()
                : context.Users
                    .Where(user => user.Id == review.SubjectId)
                    .Select(user => user.Name.Value)
                    .FirstOrDefault()));
    }
}
