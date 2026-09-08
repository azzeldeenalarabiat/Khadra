using Khadra.Application.Common;
using Khadra.Application.Reviews;
using Khadra.Application.Reviews.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Reviews;
using Khadra.Domain.Reviews.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Reviews;

/// <summary>
/// Spec 4.1 and 5.6: the customer rates the rental office, and the office can never edit that score.
///
/// The rules worth holding: the subject comes from the booking rather than the request, a rating is
/// only possible once the booking has actually settled, and one booking yields one review.
/// </summary>
public sealed class ReviewUseCaseTests
{
    private static readonly Id CustomerId = Id.New();

    private sealed class Context
    {
        public IReviewRepository Reviews { get; } = Substitute.For<IReviewRepository>();
        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IGalleryReviewReader Reader { get; } = Substitute.For<IGalleryReviewReader>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public TestClock Clock { get; } = new(Build.Now);

        public Context() => UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        public Booking Given(Booking booking)
        {
            Bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
            return booking;
        }

        public ReviewHandlers Handlers() => new(Reviews, Bookings, Reader, Clock, UnitOfWork);
    }

    /// <summary>A booking runs its whole course and settles, which is when a rating unlocks.</summary>
    private static Booking Completed(Context context)
    {
        var booking = Build.ConfirmedBooking(customerId: CustomerId);
        booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start);
        booking.RecordReturn(BookingParty.Dealer, Id.New(), booking.Period.End);
        booking.Settle(booking.Period.End.Add(booking.Terms.PostReturnSettlementWindow), hasOpenDispute: false);
        booking.ClearDomainEvents();
        return context.Given(booking);
    }

    [Fact]
    public async Task A_customer_rates_the_gallery_behind_their_completed_booking()
    {
        var context = new Context();
        var booking = Completed(context);

        var result = await context.Handlers().Handle(
            new LeaveReviewCommand(CustomerId, booking.Id, 5, "Car was spotless and ready on time."),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(5, result.Value.Rating);
        await context.Reviews.Received(1).AddAsync(
            Arg.Is<Review>(review =>
                // The SUBJECT came from the booking. A request that could name it could rate a
                // competitor's gallery.
                review.SubjectId == booking.DealerId &&
                review.ReviewerUserId == CustomerId &&
                review.Direction == ReviewDirection.CustomerRatesDealer),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Completed is the settlement window having elapsed with no dispute. Rating before that would let
    /// a score be used as leverage while money is still in question.
    /// </summary>
    [Fact]
    public async Task A_booking_that_has_not_settled_cannot_be_rated()
    {
        var context = new Context();
        var live = context.Given(Build.ConfirmedBooking(customerId: CustomerId));

        var result = await context.Handlers().Handle(
            new LeaveReviewCommand(CustomerId, live.Id, 5, null), CancellationToken.None);

        Assert.Equal("review.booking_not_completed", result.Error.Code);
    }

    [Fact]
    public async Task Somebody_elses_booking_is_indistinguishable_from_one_that_does_not_exist()
    {
        var context = new Context();
        var theirs = context.Given(Build.ConfirmedBooking(customerId: Id.New()));

        var mine = await context.Handlers().Handle(
            new LeaveReviewCommand(CustomerId, theirs.Id, 5, null), CancellationToken.None);
        var missing = await context.Handlers().Handle(
            new LeaveReviewCommand(CustomerId, Id.New(), 5, null), CancellationToken.None);

        Assert.Equal("booking.not_found", mine.Error.Code);
        Assert.Equal("booking.not_found", missing.Error.Code);
    }

    [Fact]
    public async Task One_booking_yields_one_review()
    {
        var context = new Context();
        var booking = Completed(context);
        context.Reviews
            .ExistsForBookingAsync(booking.Id, ReviewDirection.CustomerRatesDealer, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await context.Handlers().Handle(
            new LeaveReviewCommand(CustomerId, booking.Id, 4, null), CancellationToken.None);

        Assert.Equal("review.already_reviewed", result.Error.Code);
        await context.Reviews.DidNotReceive().AddAsync(Arg.Any<Review>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void A_rating_outside_the_scale_is_refused_before_the_handler(int rating)
    {
        var validator = new LeaveReviewCommandValidator();

        var result = validator.Validate(new LeaveReviewCommand(CustomerId, Id.New(), rating, null));

        Assert.False(result.IsValid);
    }

    /// <summary>
    /// "You have not reviewed this yet" is a legitimate answer about a booking that does exist, and a
    /// 404 would make a client unable to tell it from a booking that is not theirs — which it needs,
    /// to decide whether to offer the form at all.
    /// </summary>
    [Fact]
    public async Task Not_having_reviewed_yet_is_a_success_with_no_review_rather_than_a_404()
    {
        var context = new Context();
        var booking = Completed(context);
        context.Reader
            .FindForBookingAsync(booking.Id, ReviewDirection.CustomerRatesDealer, Arg.Any<CancellationToken>())
            .Returns((ReviewDto?)null);

        var result = await context.Handlers().Handle(
            new GetMyReviewQuery(CustomerId, booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    /// <summary>
    /// Moderation removes abusive TEXT and never the score (spec 3.2, 4.1) — otherwise reporting a
    /// comment would be a way for a gallery to erase the rating attached to it.
    /// </summary>
    [Fact]
    public void Hiding_a_review_keeps_its_rating()
    {
        var review = Review.Leave(
            Id.New(), ReviewDirection.CustomerRatesDealer, CustomerId, Id.New(),
            Rating.Create(1).Value, "Unrepeatable.", bookingIsCompleted: true, Build.Now).Value;

        review.Hide("Abusive language.");

        Assert.True(review.IsHidden);
        Assert.Equal(1, review.Rating.Value);
    }
}
