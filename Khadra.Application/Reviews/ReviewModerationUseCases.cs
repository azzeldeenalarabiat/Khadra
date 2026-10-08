using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Reviews.ReadModels;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.Reviews;
using Khadra.Domain.Reviews.Repositories;
using MediatR;

namespace Khadra.Application.Reviews;

// Review moderation (spec 3.2; pre-launch item 81). `Review.Hide` and `Unhide` existed and every reader honoured them;
// nothing could reach them. An administrator now can, in both directions, and each decision is audited in the same
// save as the change.

/// <param name="Visibility">"hidden", "visible", or null for both.</param>
/// <param name="Direction">A <see cref="ReviewDirection"/> name, or null for both.</param>
public sealed record ListReviewsForModerationQuery(
    string? Visibility,
    string? Direction,
    int? Rating,
    string? Search,
    PageRequest Page) : IQuery<Result<PagedResult<ModerationReview>, Error>>;

/// <param name="Reason">A <see cref="ReviewHideReason"/> name.</param>
public sealed record HideReviewCommand(Id ReviewId, string Reason) : ICommand<Result<ModerationReview, Error>>;

public sealed record RestoreReviewCommand(Id ReviewId) : ICommand<Result<ModerationReview, Error>>;

public sealed class ListReviewsForModerationQueryValidator : AbstractValidator<ListReviewsForModerationQuery>
{
    public ListReviewsForModerationQueryValidator()
    {
        RuleFor(query => query.Visibility)
            .Must(value => value is null || value is "hidden" or "visible")
            .WithMessage("Visibility is 'hidden' or 'visible'.");
        RuleFor(query => query.Direction)
            .Must(value => value is null || Enumeration.GetAll<ReviewDirection>().Any(direction => direction.Name == value))
            .WithMessage("Unknown review direction.");
        RuleFor(query => query.Rating).InclusiveBetween(Rating.Minimum, Rating.Maximum).When(query => query.Rating is not null);
        RuleFor(query => query.Search).MaximumLength(200);
    }
}

public sealed class HideReviewCommandValidator : AbstractValidator<HideReviewCommand>
{
    public HideReviewCommandValidator()
    {
        RuleFor(command => command.Reason)
            .Must(value => Enumeration.GetAll<ReviewHideReason>().Any(reason => reason.Name == value))
            .WithMessage("A review is hidden for one of the policy reasons.");
    }
}

public sealed class ReviewModerationHandlers(
    IReviewRepository reviews,
    IReviewModerationReader reader,
    AdminActionRecorder audit,
    IClock clock,
    IUnitOfWork unitOfWork) :
    IRequestHandler<ListReviewsForModerationQuery, Result<PagedResult<ModerationReview>, Error>>,
    IRequestHandler<HideReviewCommand, Result<ModerationReview, Error>>,
    IRequestHandler<RestoreReviewCommand, Result<ModerationReview, Error>>
{
    public async Task<Result<PagedResult<ModerationReview>, Error>> Handle(
        ListReviewsForModerationQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var filter = new ReviewModerationFilter(
            request.Visibility switch { "hidden" => true, "visible" => false, _ => null },
            request.Direction is null ? null : Enumeration.FromName<ReviewDirection>(request.Direction),
            request.Rating,
            string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim());

        return await reader.ListAsync(filter, request.Page, clock.UtcNow, cancellationToken);
    }

    public Task<Result<ModerationReview, Error>> Handle(HideReviewCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = Enumeration.FromName<ReviewHideReason>(request.Reason);
        return ActAsync(
            request.ReviewId,
            review => review.Hide(reason),
            AuditAction.ReviewHidden,
            previousValue: _ => null,
            newValue: _ => reason.Name,
            cancellationToken);
    }

    public Task<Result<ModerationReview, Error>> Handle(RestoreReviewCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ActAsync(
            request.ReviewId,
            review => review.Unhide(),
            AuditAction.ReviewRestored,
            previousValue: reason => reason,
            newValue: _ => null,
            cancellationToken);
    }

    /// <summary>
    /// One decision: load, act, audit, save. The audit entry names the BOOKING the review is about — a reference, never
    /// a person — and carries the policy reason as a code the console words: the reason a review was hidden moves from
    /// the new value (hide) to the previous one (restore).
    /// </summary>
    private async Task<Result<ModerationReview, Error>> ActAsync(
        Id reviewId,
        Func<Review, UnitResult<Error>> act,
        AuditAction action,
        Func<string?, string?> previousValue,
        Func<string?, string?> newValue,
        CancellationToken cancellationToken)
    {
        var review = await reviews.GetByIdAsync(reviewId, cancellationToken);
        if (review is null)
            return ReviewErrors.NotFound;

        // The booking's reference from the read side, not the Booking aggregate: loading a booking can settle a lapse
        // on it (item 234), and moderating a review must never save a change to a booking as a side effect.
        var described = await reader.GetAsync(review.Id, clock.UtcNow, cancellationToken);
        var reasonBefore = review.HiddenReason?.Name;
        var outcome = act(review);
        if (outcome.IsFailure)
            return outcome.Error;

        audit.Record(
            action,
            AuditEntityType.Review,
            review.Id,
            described?.BookingReference ?? review.BookingId.Value.ToString("N")[..8],
            previousValue(reasonBefore),
            newValue(review.HiddenReason?.Name));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var item = await reader.GetAsync(review.Id, clock.UtcNow, cancellationToken);
        return item is null ? ReviewErrors.NotFound : item;
    }
}
