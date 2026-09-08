using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Reviews.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Reviews;
using Khadra.Domain.Reviews.Repositories;
using MediatR;

namespace Khadra.Application.Reviews;

/// <summary>
/// A customer rating the gallery they rented from (spec 4.1, 5.6).
/// </summary>
/// <remarks>
/// The SUBJECT is never named by the caller. It is read from the booking, because a client that could
/// name the gallery it was rating could rate a competitor's.
/// </remarks>
public sealed record LeaveReviewCommand(Id ReviewerUserId, Id BookingId, int Rating, string? Comment)
    : ICommand<Result<ReviewDto, Error>>;

/// <summary>The caller's own review of one booking, or null if they have not left one.</summary>
public sealed record GetMyReviewQuery(Id ReviewerUserId, Id BookingId) : IQuery<Result<ReviewDto?, Error>>;

/// <summary>A gallery's public reviews, newest first.</summary>
/// <remarks>
/// Anonymous: a rating is the main thing a customer weighs before booking, and a marketplace that
/// hides it until sign-up is asking to be trusted on nothing. Reviewer names are NOT here — see
/// <see cref="GalleryReviewDto"/>.
/// </remarks>
public sealed record ListGalleryReviewsQuery(Id DealerId, PageRequest Page)
    : IQuery<Result<PagedResult<GalleryReviewDto>, Error>>;

public sealed class LeaveReviewCommandValidator : AbstractValidator<LeaveReviewCommand>
{
    public LeaveReviewCommandValidator()
    {
        RuleFor(command => command.Rating).InclusiveBetween(Rating.Minimum, Rating.Maximum);
        RuleFor(command => command.Comment).MaximumLength(2000);
    }
}

public sealed class ReviewHandlers(
    IReviewRepository reviews,
    IBookingRepository bookings,
    IGalleryReviewReader reader,
    IClock clock,
    IUnitOfWork unitOfWork) :
    IRequestHandler<LeaveReviewCommand, Result<ReviewDto, Error>>,
    IRequestHandler<GetMyReviewQuery, Result<ReviewDto?, Error>>,
    IRequestHandler<ListGalleryReviewsQuery, Result<PagedResult<GalleryReviewDto>, Error>>
{
    public async Task<Result<ReviewDto, Error>> Handle(LeaveReviewCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var booking = await bookings.GetByIdAsync(request.BookingId, cancellationToken);

        // not_found rather than 403 for a booking that is somebody else's, exactly as everywhere
        // else a booking is addressed by id: a 403 would confirm the id is real.
        if (booking is null || booking.CustomerId != request.ReviewerUserId)
            return BookingErrors.NotFound;

        // Asked before Leave() so the caller gets "you already reviewed this" rather than a unique
        // constraint violation. The index is still what makes it impossible, because two taps on a
        // slow connection are two requests and this read loses that race.
        if (await reviews.ExistsForBookingAsync(request.BookingId, ReviewDirection.CustomerRatesDealer, cancellationToken))
            return ReviewErrors.AlreadyReviewed;

        var rating = Rating.Create(request.Rating);
        if (rating.IsFailure)
            return rating.Error;

        var review = Review.Leave(
            booking.Id,
            ReviewDirection.CustomerRatesDealer,
            request.ReviewerUserId,
            // From the booking, never from the request.
            booking.DealerId,
            rating.Value,
            request.Comment,
            // The aggregate decides what "completed" means; this only reports the status.
            booking.CanBeReviewed,
            clock.UtcNow);

        if (review.IsFailure)
            return review.Error;

        await reviews.AddAsync(review.Value, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ReviewDto.From(review.Value);
    }

    public async Task<Result<ReviewDto?, Error>> Handle(GetMyReviewQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var booking = await bookings.GetByIdAsync(request.BookingId, cancellationToken);
        if (booking is null || booking.CustomerId != request.ReviewerUserId)
            return BookingErrors.NotFound;

        var mine = await reader.FindForBookingAsync(
            request.BookingId, ReviewDirection.CustomerRatesDealer, cancellationToken);

        return Result.Success<ReviewDto?, Error>(mine);
    }

    public async Task<Result<PagedResult<GalleryReviewDto>, Error>> Handle(
        ListGalleryReviewsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await reader.ListForGalleryAsync(request.DealerId, request.Page, cancellationToken);
    }
}
