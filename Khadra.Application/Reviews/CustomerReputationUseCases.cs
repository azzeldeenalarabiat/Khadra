using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers;
using Khadra.Application.Reviews.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Reviews;
using Khadra.Domain.Reviews.Repositories;
using MediatR;

namespace Khadra.Application.Reviews;

/// <summary>
/// A gallery reading what the platform knows about the customer in front of them.
/// </summary>
/// <remarks>
/// <b>Keyed on the BOOKING, never on a customer id, and that is a security decision rather than a
/// convenience.</b> An endpoint taking a customer id would be a lookup oracle over the entire
/// customer base for anyone holding a dealer session, and no amount of checking inside it could undo
/// that — the check would have to be "do I have a booking with this person", which is exactly the
/// booking the caller could have named instead. Naming the booking makes the relationship the KEY, so
/// there is nothing to enumerate.
/// </remarks>
public sealed record GetCustomerReputationQuery(Id ActorUserId, Id BookingId)
    : IQuery<Result<CustomerReputation, Error>>;

/// <summary>
/// The gallery's rating of a customer they rented to (spec 5.6).
/// </summary>
/// <remarks>
/// <b>There is no comment, and none is stored.</b> The audience is other galleries, so free text here
/// would be unverified prose about a named private individual circulating between competing
/// businesses, unmoderated at the moment of writing, invisible to the person it describes, and
/// certain to contain phone numbers and plate numbers. The platform has already made this call twice:
/// rejection and cancellation reasons were free text, produced "asdf", and became closed codes.
/// </remarks>
public sealed record RateCustomerCommand(Id ActorUserId, Id BookingId, int Rating)
    : ICommand<Result<ReviewDto, Error>>;

/// <summary>The gallery's own rating of one booking, or null if they have not left one.</summary>
public sealed record GetMyCustomerRatingQuery(Id ActorUserId, Id BookingId)
    : IQuery<Result<ReviewDto?, Error>>;

/// <summary>
/// The customer's own view of what galleries see about them.
/// </summary>
/// <remarks>
/// A semi-private score about a person, which that person cannot see, is the thing privacy law
/// objects to most — and it is also the only way a customer learns they should dispute a wrong
/// no-show while the window is still open. Same shape, minus the with-this-gallery count, which is
/// meaningless without a gallery asking.
/// </remarks>
public sealed record GetMyReputationQuery(Id CustomerUserId) : IQuery<Result<CustomerReputation, Error>>;

public sealed class RateCustomerCommandValidator : AbstractValidator<RateCustomerCommand>
{
    public RateCustomerCommandValidator() =>
        RuleFor(command => command.Rating).InclusiveBetween(Rating.Minimum, Rating.Maximum);
}

public sealed class CustomerReputationHandlers(
    IBookingRepository bookings,
    IReviewRepository reviews,
    ICustomerReputationReader reputation,
    IGalleryReviewReader reviewReader,
    DealerMembershipResolver membership,
    IBusinessRulesProvider rules,
    IClock clock,
    IUnitOfWork unitOfWork) :
    IRequestHandler<GetCustomerReputationQuery, Result<CustomerReputation, Error>>,
    IRequestHandler<RateCustomerCommand, Result<ReviewDto, Error>>,
    IRequestHandler<GetMyCustomerRatingQuery, Result<ReviewDto?, Error>>,
    IRequestHandler<GetMyReputationQuery, Result<CustomerReputation, Error>>
{
    public async Task<Result<CustomerReputation, Error>> Handle(
        GetCustomerReputationQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await ResolveAsync(request.ActorUserId, request.BookingId, cancellationToken);
        if (access.IsFailure)
            return access.Error;

        var now = clock.UtcNow;

        // The access rule, in one line: for as long as this gallery is deciding about, or holding, a
        // booking with this customer -- and no longer. `Booking.IsLive` is the in-memory twin of the
        // availability predicate, so a gallery's access ends at exactly the instant its claim on the
        // car does, rather than at a status a settlement job has not caught up with yet.
        if (!access.Value.Booking.IsLive(now))
            return ReviewErrors.ReputationNotAvailable;

        return await reputation.GetAsync(
            access.Value.Booking.CustomerId,
            access.Value.DealerId,
            now,
            cancellationToken);
    }

    public async Task<Result<CustomerReputation, Error>> Handle(
        GetMyReputationQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // No viewing gallery, so the with-this-gallery count is against the caller's own id and comes
        // back zero. Honest: there is no gallery asking.
        return await reputation.GetAsync(
            request.CustomerUserId,
            Id.Empty,
            clock.UtcNow,
            cancellationToken);
    }

    public async Task<Result<ReviewDto, Error>> Handle(RateCustomerCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await ResolveAsync(request.ActorUserId, request.BookingId, cancellationToken);
        if (access.IsFailure)
            return access.Error;
        var booking = access.Value.Booking;

        // Asked before Leave() so the caller reads "you already rated this" rather than a unique
        // constraint violation. The index is still what makes it impossible: two taps on a slow
        // connection are two requests, and this read loses that race.
        if (await reviews.ExistsForBookingAsync(booking.Id, ReviewDirection.DealerRatesCustomer, cancellationToken))
            return ReviewErrors.AlreadyReviewed;

        var rating = Rating.Create(request.Rating);
        if (rating.IsFailure)
            return rating.Error;

        var now = clock.UtcNow;
        var counterpart = await reviews.GetForBookingAsync(
            booking.Id, ReviewDirection.CustomerRatesDealer, cancellationToken);
        var revealAt = await RevealInstantAsync(booking, counterpart, cancellationToken);

        var review = Review.Leave(
            booking.Id,
            ReviewDirection.DealerRatesCustomer,
            // The person who pressed it, for accountability (spec 4.2). The unique index on
            // (booking, direction) makes the RATING the dealership's, whoever that was.
            request.ActorUserId,
            // From the booking, never from the request: a caller that could name the subject could
            // rate somebody else's customer.
            booking.CustomerId,
            rating.Value,
            // Deliberately no comment. See the command's remarks.
            comment: null,
            booking.CanBeReviewed,
            revealAt,
            now);

        if (review.IsFailure)
            return review.Error;

        // Both sides are in, so neither can be a reply to the other. Reveal them together, in this
        // same save, rather than making the second party wait out a window that no longer protects
        // anything.
        // BOTH of them: revealing only the first would leave the pair asymmetric, with one side
        // published and the other still hidden for the rest of a window that is now protecting
        // nothing.
        if (counterpart is not null)
        {
            counterpart.Reveal(now);
            review.Value.Reveal(now);
        }

        await reviews.AddAsync(review.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ReviewDto.From(review.Value);
    }

    public async Task<Result<ReviewDto?, Error>> Handle(
        GetMyCustomerRatingQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var access = await ResolveAsync(request.ActorUserId, request.BookingId, cancellationToken);
        if (access.IsFailure)
            return access.Error;

        var mine = await reviewReader.FindForBookingAsync(
            request.BookingId, ReviewDirection.DealerRatesCustomer, cancellationToken);

        return Result.Success<ReviewDto?, Error>(mine);
    }

    /// <summary>
    /// When a review written now would become visible.
    /// </summary>
    /// <remarks>
    /// The counterpart's instant if there is one, so both sides of a booking share one deadline and
    /// the second party's window to write IS the first one's reveal. That equality is what makes
    /// "nobody sees the counterpart before submitting" true by construction rather than by checking.
    ///
    /// Otherwise the window from the moment the rental FINISHED, not from now: a gallery that rates
    /// on the last possible day must not thereby give the customer a fresh fortnight.
    /// </remarks>
    private async Task<DateTimeOffset> RevealInstantAsync(
        Booking booking,
        Review? counterpart,
        CancellationToken cancellationToken)
    {
        if (counterpart is not null)
            return counterpart.VisibleFrom;

        var window = TimeSpan.FromDays((await rules.GetAsync(cancellationToken)).ReviewWindowDays);
        // FinishedAt is null on a Completed booking that settled from Returned, so the return is the
        // fallback; CreatedAt cannot happen on a reviewable booking and is there so the expression is
        // total rather than throwing on a row nobody expected.
        var finished = booking.FinishedAt ?? booking.ReturnedAt ?? booking.CreatedAt;
        return finished.Add(window);
    }

    /// <summary>
    /// The caller's booking, or not found.
    /// </summary>
    /// <remarks>
    /// <c>not_found</c> for a booking belonging to another dealership, exactly as everywhere else a
    /// booking is addressed by id: a 403 would confirm the id is real. The membership resolver
    /// answers for the trading gate too, so a suspended dealership cannot browse reputations.
    /// </remarks>
    private async Task<Result<DealerBooking, Error>> ResolveAsync(
        Id actorUserId,
        Id bookingId,
        CancellationToken cancellationToken)
    {
        var member = await membership.ResolveAsync(actorUserId, cancellationToken);
        if (member.IsFailure)
            return member.Error;
        // The SAME gate that guards approve and reject. Reputation exists to inform that decision, so
        // anyone who cannot make the decision has no business reading it -- and a suspended dealership
        // cannot make it.
        if (!member.Value.CanActOnBookings)
            return BookingErrors.ActorCannotDecide;

        var booking = await bookings.GetByIdAsync(bookingId, cancellationToken);
        if (booking is null || booking.DealerId != member.Value.Dealer.Id)
            return BookingErrors.NotFound;

        return new DealerBooking(booking, member.Value.Dealer.Id);
    }

    private sealed record DealerBooking(Booking Booking, Id DealerId);
}
