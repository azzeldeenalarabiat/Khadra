using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.Reviews;

public static class ReviewErrors
{
    public static readonly Error InvalidRating =
        Error.Validation("review.invalid_rating", "A rating must be a whole number between 1 and 5.");

    public static readonly Error BookingNotCompleted =
        Error.Conflict("review.booking_not_completed", "A booking can only be reviewed once it is completed.");

    public static readonly Error CommentTooLong =
        Error.Validation("review.comment_too_long", "A review comment must be at most 2000 characters.");

    public static readonly Error AlreadyReviewed =
        Error.Conflict("review.already_reviewed", "You have already reviewed this booking.");

    public static readonly Error EditWindowClosed =
        Error.Conflict("review.edit_window_closed", "A review can no longer be edited.");

    public static readonly Error ReviewWindowClosed =
        Error.Conflict("review.window_closed", "The time to review this booking has passed.");

    public static readonly Error ReputationNotAvailable = Error.Conflict(
        "review.reputation_not_available",
        "A customer's history is only visible while you have a live booking with them.");

    public static readonly Error NotFound = Error.NotFound("review.not_found", "No such review.");

    public static readonly Error AlreadyHidden =
        Error.Conflict("review.already_hidden", "This review is already hidden.");

    public static readonly Error NotHidden =
        Error.Conflict("review.not_hidden", "This review is not hidden.");
}

/// <summary>
/// Why an administrator hid a review (pre-launch item 81): a closed list, the four policy reasons the design names.
/// </summary>
/// <remarks>
/// A code rather than prose for the same reason rejections and cancellations became codes: a reason nobody has to read
/// to count, which each console words in its reader's language, and which writes no administrator's sentence into the
/// append-only audit trail. Stored by name, so the list is add-only (item 19; PersistedEnumerationNamesTests holds it).
/// </remarks>
public sealed class ReviewHideReason : Enumeration
{
    public static readonly ReviewHideReason PersonalContactDetails = new(1, "PersonalContactDetails");
    public static readonly ReviewHideReason AbusiveLanguage = new(2, "AbusiveLanguage");
    public static readonly ReviewHideReason NotAboutThisRental = new(3, "NotAboutThisRental");
    public static readonly ReviewHideReason SpamOrPromotion = new(4, "SpamOrPromotion");

    private ReviewHideReason(int id, string name) : base(id, name)
    {
    }
}

public sealed class ReviewDirection : Enumeration
{
    // Feeds the dealer's public rating (spec 4.1), which the dealer can never edit.
    public static readonly ReviewDirection CustomerRatesDealer = new(1, "CustomerRatesDealer");
    // Spec 5.6: mutual. Visible to other dealers so they can judge a booking request.
    public static readonly ReviewDirection DealerRatesCustomer = new(2, "DealerRatesCustomer");

    private ReviewDirection(int id, string name) : base(id, name)
    {
    }

    public bool IsPublic => this == CustomerRatesDealer;

    /// <summary>
    /// Whether hiding a review's text leaves its SCORE counting.
    /// </summary>
    /// <remarks>
    /// True for the public direction and false for the private one, and the asymmetry is deliberate.
    /// Spec 4.1 makes a gallery's rating something it can never edit, so keeping the score when a
    /// comment is moderated is what stops a gallery erasing a bad rating by reporting it.
    ///
    /// The dealer's rating of a customer has no text to moderate -- it never had one -- so the only
    /// thing an administrator could be hiding is the SCORE itself, and the only reason to hide that is
    /// that it was wrong. Keeping a retaliatory one-star counting against a real person at every
    /// future approval, while telling them it had been dealt with, would be no remedy at all.
    /// </remarks>
    public bool HiddenScoreStillCounts => this == CustomerRatesDealer;
}

public sealed class Rating : ValueObject
{
    public const int Minimum = 1;
    public const int Maximum = 5;

    public int Value { get; }

    private Rating(int value)
    {
        Value = value;
    }

    public static Result<Rating, Error> Create(int value) =>
        value is < Minimum or > Maximum ? ReviewErrors.InvalidRating : new Rating(value);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => $"{Value}/{Maximum}";
}

// One review per booking per direction (spec 5.6). Ratings are aggregated into the dealer's public
// score and the customer's reputation as read models, recomputed from these rows, so a rating is
// never stored twice and can never drift from its source.
public sealed class Review : AggregateRoot
{
    public Id BookingId { get; private set; }
    public ReviewDirection Direction { get; private set; } = null!;
    public Id ReviewerUserId { get; private set; }
    // The dealer being rated, or the customer being rated.
    public Id SubjectId { get; private set; }
    public Rating Rating { get; private set; } = null!;
    public string? Comment { get; private set; }

    /// <summary>
    /// When this review becomes visible to anyone but its author.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The blind window, and it stops being optional the moment reviews are mutual. Without it a
    /// gallery reads its new one-star, finds the booking it came from, and rates that customer one
    /// star before their reputation reaches any other gallery -- a retaliation loop with the
    /// platform's own machinery doing the work.
    /// </para>
    /// <para>
    /// The invariant is "nobody sees the counterpart before submitting", and it holds BY CONSTRUCTION
    /// rather than by checking: the first review sets its own reveal instant, the second party's
    /// deadline to submit IS that instant, and a second review that does arrive reveals both at once.
    /// So a party can only ever have submitted while the other was still hidden.
    /// </para>
    /// <para>
    /// A real column, not a computation over the window in force today: a window the owner shortens
    /// tomorrow must not retroactively expose a review written under a longer one.
    /// </para>
    /// </remarks>
    public DateTimeOffset VisibleFrom { get; private set; }

    public bool IsHidden { get; private set; }
    public ReviewHideReason? HiddenReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private Review()
    {
    }

    private Review(Id id) : base(id)
    {
    }

    public static Result<Review, Error> Leave(
        Id bookingId,
        ReviewDirection direction,
        Id reviewerUserId,
        Id subjectId,
        Rating rating,
        string? comment,
        bool bookingIsCompleted,
        DateTimeOffset revealAt,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(direction);
        ArgumentNullException.ThrowIfNull(rating);
        if (bookingId.IsEmpty || reviewerUserId.IsEmpty || subjectId.IsEmpty)
            throw new DomainException("A review requires a booking, a reviewer and a subject.");

        // Passed in rather than navigated to: Booking is another context and is referenced by id only.
        if (!bookingIsCompleted)
            return ReviewErrors.BookingNotCompleted;
        if (comment is { Length: > 2000 })
            return ReviewErrors.CommentTooLong;
        // The counterpart is already readable, so anything written now could be a reply to it. This is
        // the guard that makes the blind window an invariant rather than a hope.
        if (now >= revealAt)
            return ReviewErrors.ReviewWindowClosed;

        return new Review(Id.New())
        {
            BookingId = bookingId,
            Direction = direction,
            ReviewerUserId = reviewerUserId,
            SubjectId = subjectId,
            Rating = rating,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            VisibleFrom = revealAt,
            CreatedAt = now
        };
    }

    /// <summary>Whether anyone but the author may see this review yet.</summary>
    public bool IsVisibleAt(DateTimeOffset now) => now >= VisibleFrom;

    /// <summary>
    /// Brings the reveal forward, because the counterpart has now been written.
    /// </summary>
    /// <remarks>
    /// Only ever EARLIER. Pushing a reveal back would let a late second review hide a first one
    /// somebody had already read.
    /// </remarks>
    public void Reveal(DateTimeOffset now)
    {
        if (now < VisibleFrom)
            VisibleFrom = now;
    }

    // A short grace period to fix a rating left in haste; after that the score is stable.
    public UnitResult<Error> Revise(Rating rating, string? comment, TimeSpan editWindow, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rating);

        if (now > CreatedAt.Add(editWindow))
            return UnitResult.Failure(ReviewErrors.EditWindowClosed);
        // And never once the reveal has passed, whatever the edit window says. After that an edit is a
        // reply to the counterpart, which is the exact thing the blind window exists to prevent.
        if (IsVisibleAt(now))
            return UnitResult.Failure(ReviewErrors.EditWindowClosed);
        if (comment is { Length: > 2000 })
            return UnitResult.Failure(ReviewErrors.CommentTooLong);

        Rating = rating;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        UpdatedAt = now;
        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// Admin moderation (spec 3.2; pre-launch item 81). What hiding takes away depends on the direction
    /// (<see cref="ReviewDirection.HiddenScoreStillCounts"/>): a customer's review of a gallery loses its text and keeps
    /// its score, so a gallery cannot erase a bad rating by reporting it; a gallery's rating of a customer stops counting
    /// at all, because the score is the only thing it has.
    /// </summary>
    /// <remarks>
    /// Refused on a review already hidden, rather than quietly re-hidden: every hide is an audit entry, and a second
    /// click must not write a second one claiming another decision was made.
    /// </remarks>
    public UnitResult<Error> Hide(ReviewHideReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (IsHidden)
            return UnitResult.Failure(ReviewErrors.AlreadyHidden);

        IsHidden = true;
        HiddenReason = reason;
        return UnitResult.Success<Error>();
    }

    /// <summary>Puts a hidden review back as it was. Refused on one that is not hidden, for the same reason.</summary>
    public UnitResult<Error> Unhide()
    {
        if (!IsHidden)
            return UnitResult.Failure(ReviewErrors.NotHidden);

        IsHidden = false;
        HiddenReason = null;
        return UnitResult.Success<Error>();
    }
}
