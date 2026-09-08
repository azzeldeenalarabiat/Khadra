using Khadra.Domain.Common;
using Khadra.Domain.Reviews;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Reviews;

public sealed class ReviewTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    private static Review Leave(
        ReviewDirection? direction = null,
        int rating = 5,
        string? comment = "Great car, smooth handover.",
        bool completed = true) =>
        Review.Leave(
            Id.New(),
            direction ?? ReviewDirection.CustomerRatesDealer,
            Id.New(),
            Id.New(),
            Rating.Create(rating).Value,
            comment,
            completed,
            revealAt: Now.AddDays(14),
            Now).Value;

    [Fact]
    public void A_booking_must_be_completed_before_it_can_be_reviewed()
    {
        var review = Review.Leave(
            Id.New(), ReviewDirection.CustomerRatesDealer, Id.New(), Id.New(),
            Rating.Create(5).Value, "Too soon", bookingIsCompleted: false, revealAt: Now.AddDays(14), now: Now);

        Assert.Equal("review.booking_not_completed", review.Error.Code);
    }

    [Fact]
    public void Only_the_customers_review_of_the_dealer_is_public()
    {
        Assert.True(ReviewDirection.CustomerRatesDealer.IsPublic);
        // Spec 5.6: the dealer's rating of the customer informs other dealers, it is not public.
        Assert.False(ReviewDirection.DealerRatesCustomer.IsPublic);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void A_rating_lives_between_one_and_five(int value) =>
        Assert.Equal("review.invalid_rating", Rating.Create(value).Error.Code);

    [Fact]
    public void A_comment_is_optional_and_length_limited()
    {
        Assert.Null(Leave(comment: "   ").Comment);

        var tooLong = Review.Leave(
            Id.New(), ReviewDirection.CustomerRatesDealer, Id.New(), Id.New(),
            Rating.Create(4).Value, new string('x', 2001), true, revealAt: Now.AddDays(14), now: Now);

        Assert.Equal("review.comment_too_long", tooLong.Error.Code);
    }

    [Fact]
    public void A_review_can_be_revised_briefly_and_then_becomes_final()
    {
        var review = Leave(rating: 2);
        var window = TimeSpan.FromHours(24);

        Assert.True(review.Revise(Rating.Create(4).Value, "Dealer fixed it.", window, Now.AddHours(1)).IsSuccess);
        Assert.Equal(4, review.Rating.Value);
        Assert.Equal(Now.AddHours(1), review.UpdatedAt);

        Assert.Equal("review.edit_window_closed",
            review.Revise(Rating.Create(1).Value, "Changed my mind again.", window, Now.AddDays(2)).Error.Code);
        Assert.Equal(4, review.Rating.Value);
    }

    [Fact]
    public void Hiding_a_review_removes_the_words_but_keeps_the_score()
    {
        var review = Leave(rating: 1, comment: "Abusive text.");

        review.Hide("Contains abuse.");

        Assert.True(review.IsHidden);
        Assert.Equal("Contains abuse.", review.HiddenReason);
        // The rating still counts: letting a dealer erase a bad score by reporting it would corrupt
        // the whole rating system.
        Assert.Equal(1, review.Rating.Value);

        review.Unhide();
        Assert.False(review.IsHidden);
        Assert.Null(review.HiddenReason);
    }

    [Fact]
    public void A_review_records_who_wrote_it_and_who_it_is_about()
    {
        var reviewer = Id.New();
        var subject = Id.New();
        var booking = Id.New();

        var review = Review.Leave(
            booking, ReviewDirection.DealerRatesCustomer, reviewer, subject,
            Rating.Create(3).Value, "Returned the car late.", true, revealAt: Now.AddDays(14), now: Now).Value;

        Assert.Equal(booking, review.BookingId);
        Assert.Equal(reviewer, review.ReviewerUserId);
        Assert.Equal(subject, review.SubjectId);
        Assert.Equal("3/5", review.Rating.ToString());
    }
}
