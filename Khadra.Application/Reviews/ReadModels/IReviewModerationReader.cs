using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.Reviews;

namespace Khadra.Application.Reviews.ReadModels;

/// <summary>Which reviews an administrator is looking at. Every field is optional and they compose.</summary>
/// <param name="Hidden">True for hidden reviews only, false for those still shown, null for both.</param>
/// <param name="Search">Matched against the comment and the booking reference.</param>
public sealed record ReviewModerationFilter(
    bool? Hidden = null,
    ReviewDirection? Direction = null,
    int? Rating = null,
    string? Search = null);

/// <summary>
/// One review as the moderator sees it (pre-launch item 81): the whole comment, hidden or not, because judging it is
/// the point, and who wrote it about whom.
/// </summary>
/// <remarks>
/// <para>
/// Both directions share this shape. For a customer's review of a gallery the reviewer is the customer and the subject
/// the gallery; for a gallery's rating of a customer the reviewer is the gallery — the office, not the member of staff,
/// the same way the customer's reputation counts it — and the subject the customer. <see cref="ReviewerId"/> and
/// <see cref="SubjectId"/> are the user or dealership ids the console links to; which is which follows from
/// <see cref="Direction"/>.
/// </para>
/// <para>
/// A name that no longer resolves arrives as null, and the console words that. <see cref="IsPublished"/> is whether
/// the blind window has passed: a review inside it is not yet visible to anybody but its author, hidden or not.
/// </para>
/// </remarks>
public sealed record ModerationReview(
    Guid ReviewId,
    string Direction,
    int Rating,
    string? Comment,
    bool IsHidden,
    string? HiddenReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset VisibleFrom,
    bool IsPublished,
    Guid BookingId,
    string? BookingReference,
    Guid ReviewerId,
    string? ReviewerName,
    Guid SubjectId,
    string? SubjectName);

public interface IReviewModerationReader
{
    /// <summary>Newest first, in a total order (created, then id), so a page boundary never drops a row.</summary>
    Task<PagedResult<ModerationReview>> ListAsync(
        ReviewModerationFilter filter,
        PageRequest page,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<ModerationReview?> GetAsync(Id reviewId, DateTimeOffset now, CancellationToken cancellationToken = default);
}
