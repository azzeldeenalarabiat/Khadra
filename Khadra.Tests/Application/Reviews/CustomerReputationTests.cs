using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers;
using Khadra.Application.Reviews;
using Khadra.Application.Reviews.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Reviews;
using Khadra.Domain.Reviews.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Reviews;

/// <summary>
/// A gallery reading a customer's history, and rating them afterwards.
/// </summary>
/// <remarks>
/// Almost every test here is about somebody NOT being allowed to see something. That is the shape of
/// the feature: the reputation is unverified information about a named private individual, circulated
/// between competing businesses, which the individual cannot see, and the access rules are the only
/// thing standing between that and a lookup service.
/// </remarks>
public sealed class CustomerReputationTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    private sealed class Context
    {
        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IReviewRepository Reviews { get; } = Substitute.For<IReviewRepository>();
        public ICustomerReputationReader Reputation { get; } = Substitute.For<ICustomerReputationReader>();
        public IGalleryReviewReader ReviewReader { get; } = Substitute.For<IGalleryReviewReader>();
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public TestClock Clock { get; } = new(Now);

        public Dealer Dealer { get; }
        public Id OwnerUserId { get; }

        public List<Review> Added { get; } = [];

        public Context(bool trading = true)
        {
            OwnerUserId = Id.New();
            Dealer = trading
                ? Build.ApprovedDealer(Now, ownerUserId: OwnerUserId)
                : Suspended(OwnerUserId);
            Dealers.GetByOwnerUserIdAsync(OwnerUserId, Arg.Any<CancellationToken>()).Returns(Dealer);

            Reputation.GetAsync(Arg.Any<Id>(), Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(new CustomerReputation(RatingSummary.None, 0, 0, 0, 0, 0, Now));

            Reviews.When(repository => repository.AddAsync(Arg.Any<Review>(), Arg.Any<CancellationToken>()))
                .Do(call => Added.Add(call.Arg<Review>()));
        }

        private static Dealer Suspended(Id ownerUserId)
        {
            var dealer = Build.ApprovedDealer(Now, ownerUserId: ownerUserId);
            dealer.Suspend(Id.New(), "Under investigation.", Now);
            return dealer;
        }

        public Booking GivenBooking(Booking booking)
        {
            Bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
            return booking;
        }

        public CustomerReputationHandlers Handlers() => new(
            Bookings,
            Reviews,
            Reputation,
            ReviewReader,
            new DealerMembershipResolver(Dealers),
            TestBusinessRules.Provider(),
            Clock,
            UnitOfWork);
    }

    private static Booking Requested(Context context) =>
        context.GivenBooking(Build.RequestedBooking(Now) is var booking && booking.DealerId == context.Dealer.Id
            ? booking
            : Build.Booking(Now, dealerId: context.Dealer.Id));

    private static Booking Completed(Context context)
    {
        var booking = Build.ConfirmedBooking(Now, dealerId: context.Dealer.Id);
        var start = booking.Period.Start;
        booking.RecordPickup(BookingParty.Dealer, context.OwnerUserId, start);
        booking.RecordReturn(BookingParty.Dealer, context.OwnerUserId, booking.Period.End);
        booking.Settle(booking.Period.End.Add(booking.Terms.PostReturnSettlementWindow), hasOpenDispute: false);
        booking.ClearDomainEvents();
        return context.GivenBooking(booking);
    }

    // ---------------------------------------------------------------- reading a reputation

    [Fact]
    public async Task A_gallery_reads_the_reputation_of_a_customer_asking_them_for_a_car()
    {
        var context = new Context();
        var booking = Requested(context);

        var result = await context.Handlers().Handle(
            new GetCustomerReputationQuery(context.OwnerUserId, booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        // The customer and the viewing gallery both come from the BOOKING, never from the request.
        await context.Reputation.Received(1).GetAsync(
            booking.CustomerId, context.Dealer.Id, Now, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The rule in one line: while they are deciding, and no longer.
    /// </summary>
    /// <remarks>
    /// A gallery that has finished with a customer keeps no standing to look them up. Without this the
    /// endpoint becomes a permanent read on everyone a dealership has ever met.
    /// </remarks>
    [Fact]
    public async Task A_gallery_cannot_read_the_reputation_of_a_customer_whose_booking_has_ended()
    {
        var context = new Context();
        var booking = Completed(context);

        var result = await context.Handlers().Handle(
            new GetCustomerReputationQuery(context.OwnerUserId, booking.Id), CancellationToken.None);

        Assert.Equal("review.reputation_not_available", result.Error.Code);
    }

    /// <summary>
    /// A request past its decision deadline is over, whatever the row still says. Access ends with the
    /// claim on the car, not with a settlement job catching up.
    /// </summary>
    [Fact]
    public async Task Access_ends_at_the_deadline_rather_than_at_the_status()
    {
        var context = new Context();
        var booking = context.GivenBooking(Build.Booking(Now, dealerId: context.Dealer.Id));
        context.Clock.UtcNow = booking.DecisionDeadline;

        var result = await context.Handlers().Handle(
            new GetCustomerReputationQuery(context.OwnerUserId, booking.Id), CancellationToken.None);

        Assert.Same(BookingStatus.Requested, booking.Status);
        Assert.Equal("review.reputation_not_available", result.Error.Code);
    }

    /// <summary>
    /// The endpoint takes a BOOKING, so another dealership's booking is simply not found -- there is
    /// no customer id to substitute and nothing to enumerate.
    /// </summary>
    [Fact]
    public async Task Another_dealerships_booking_is_not_found()
    {
        var context = new Context();
        var someoneElses = context.GivenBooking(Build.RequestedBooking(Now));

        var result = await context.Handlers().Handle(
            new GetCustomerReputationQuery(context.OwnerUserId, someoneElses.Id), CancellationToken.None);

        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        await context.Reputation.DidNotReceive().GetAsync(
            Arg.Any<Id>(), Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The same gate that guards approve and reject. Reputation exists to inform that decision, so
    /// anyone who cannot make the decision has no business reading it.
    /// </summary>
    [Fact]
    public async Task A_suspended_dealership_cannot_read_a_reputation()
    {
        var context = new Context(trading: false);
        var booking = Requested(context);

        var result = await context.Handlers().Handle(
            new GetCustomerReputationQuery(context.OwnerUserId, booking.Id), CancellationToken.None);

        Assert.Equal("booking.actor_cannot_decide", result.Error.Code);
    }

    [Fact]
    public async Task Somebody_with_no_dealership_gets_nothing()
    {
        var context = new Context();
        var booking = Requested(context);

        var result = await context.Handlers().Handle(
            new GetCustomerReputationQuery(Id.New(), booking.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        await context.Reputation.DidNotReceive().GetAsync(
            Arg.Any<Id>(), Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------- rating a customer

    [Fact]
    public async Task A_gallery_rates_the_customer_on_its_own_completed_booking()
    {
        var context = new Context();
        var booking = Completed(context);

        var result = await context.Handlers().Handle(
            new RateCustomerCommand(context.OwnerUserId, booking.Id, 4), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var review = Assert.Single(context.Added);
        Assert.Same(ReviewDirection.DealerRatesCustomer, review.Direction);
        // The subject comes from the booking; the reviewer is the person who pressed it.
        Assert.Equal(booking.CustomerId, review.SubjectId);
        Assert.Equal(context.OwnerUserId, review.ReviewerUserId);
        Assert.Equal(4, review.Rating.Value);
        // No comment, ever. The command has no field for one and the handler passes null.
        Assert.Null(review.Comment);
    }

    [Fact]
    public async Task A_booking_that_has_not_completed_cannot_be_rated()
    {
        var context = new Context();
        var booking = Requested(context);

        var result = await context.Handlers().Handle(
            new RateCustomerCommand(context.OwnerUserId, booking.Id, 4), CancellationToken.None);

        Assert.Equal("review.booking_not_completed", result.Error.Code);
        Assert.Empty(context.Added);
    }

    [Fact]
    public async Task A_gallery_cannot_rate_a_customer_twice_on_one_booking()
    {
        var context = new Context();
        var booking = Completed(context);
        context.Reviews.ExistsForBookingAsync(
                booking.Id, ReviewDirection.DealerRatesCustomer, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await context.Handlers().Handle(
            new RateCustomerCommand(context.OwnerUserId, booking.Id, 1), CancellationToken.None);

        Assert.Equal("review.already_reviewed", result.Error.Code);
        Assert.Empty(context.Added);
    }

    // ---------------------------------------------------------------- the blind window

    /// <summary>
    /// A first review waits out the window. Nobody but its author can read it in the meantime, which
    /// is what stops the second party answering it rather than judging the rental.
    /// </summary>
    [Fact]
    public async Task A_first_rating_is_hidden_until_the_window_closes()
    {
        var context = new Context();
        var booking = Completed(context);

        await context.Handlers().Handle(
            new RateCustomerCommand(context.OwnerUserId, booking.Id, 2), CancellationToken.None);

        var review = Assert.Single(context.Added);
        var finished = booking.FinishedAt ?? booking.ReturnedAt!.Value;
        Assert.Equal(finished.AddDays(14), review.VisibleFrom);
        Assert.False(review.IsVisibleAt(context.Clock.UtcNow));
        Assert.True(review.IsVisibleAt(review.VisibleFrom));
    }

    /// <summary>
    /// The second review reveals BOTH, because neither can be a reply to the other once both are in.
    /// </summary>
    [Fact]
    public async Task The_second_rating_reveals_them_both_at_once()
    {
        var context = new Context();
        var booking = Completed(context);
        var customersReview = Review.Leave(
            booking.Id,
            ReviewDirection.CustomerRatesDealer,
            booking.CustomerId,
            booking.DealerId,
            Rating.Create(5).Value,
            "Excellent.",
            bookingIsCompleted: true,
            revealAt: Now.AddDays(14),
            now: Now).Value;
        context.Reviews.GetForBookingAsync(
                booking.Id, ReviewDirection.CustomerRatesDealer, Arg.Any<CancellationToken>())
            .Returns(customersReview);
        context.Clock.UtcNow = Now.AddDays(3);

        await context.Handlers().Handle(
            new RateCustomerCommand(context.OwnerUserId, booking.Id, 4), CancellationToken.None);

        Assert.Equal(Now.AddDays(3), customersReview.VisibleFrom);
        Assert.True(customersReview.IsVisibleAt(context.Clock.UtcNow));
        // And the gallery's own, written at the same instant, is visible too.
        Assert.True(Assert.Single(context.Added).IsVisibleAt(context.Clock.UtcNow));
    }

    /// <summary>
    /// The second party's deadline IS the first review's reveal. Past it, the counterpart is readable,
    /// so anything written now could be an answer to it -- which is the one thing the window forbids.
    /// </summary>
    [Fact]
    public async Task Nobody_may_rate_once_the_counterpart_has_been_revealed()
    {
        var context = new Context();
        var booking = Completed(context);
        var customersReview = Review.Leave(
            booking.Id,
            ReviewDirection.CustomerRatesDealer,
            booking.CustomerId,
            booking.DealerId,
            Rating.Create(1).Value,
            "Terrible.",
            bookingIsCompleted: true,
            revealAt: Now.AddDays(14),
            now: Now).Value;
        context.Reviews.GetForBookingAsync(
                booking.Id, ReviewDirection.CustomerRatesDealer, Arg.Any<CancellationToken>())
            .Returns(customersReview);
        context.Clock.UtcNow = Now.AddDays(14);

        var result = await context.Handlers().Handle(
            new RateCustomerCommand(context.OwnerUserId, booking.Id, 1), CancellationToken.None);

        Assert.Equal("review.window_closed", result.Error.Code);
        Assert.Empty(context.Added);
    }

    // ---------------------------------------------------------------- the customer's own view

    /// <summary>
    /// A score about a person that the person cannot see is the thing privacy law objects to -- and
    /// it is the only way they learn to dispute a wrong no-show while the window is open.
    /// </summary>
    [Fact]
    public async Task A_customer_can_read_their_own_reputation_without_naming_a_gallery()
    {
        var context = new Context();
        var customerId = Id.New();

        var result = await context.Handlers().Handle(
            new GetMyReputationQuery(customerId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // No viewing gallery: the with-this-gallery count is against an empty id and comes back zero,
        // which is honest -- there is no gallery asking.
        await context.Reputation.Received(1).GetAsync(
            customerId, Id.Empty, Now, Arg.Any<CancellationToken>());
    }
}
