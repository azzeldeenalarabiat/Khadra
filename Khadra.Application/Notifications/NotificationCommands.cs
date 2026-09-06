using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.Notifications;
using Khadra.Domain.Notifications.Repositories;
using MediatR;

namespace Khadra.Application.Notifications;

// A person's own notifications: read them, and mark them seen.
//
// Every command and query carries the CALLER's id and is scoped by it in the repository. There is no
// "notifications for user X" use case, and there should not be: this is the only table where one
// member of staff could otherwise learn what a colleague was told.

/// <summary>One row as a screen needs it. No sentence — the client composes from the parts.</summary>
public sealed record NotificationItem(
    Guid NotificationId,
    string Kind,
    Guid? SubjectId,
    string? SubjectReference,
    string ActorName,
    bool IsMine,
    DateTimeOffset OccurredAt,
    DateTimeOffset? ReadAt)
{
    public bool IsRead => ReadAt is not null;

    public static NotificationItem From(Notification notification, Id viewer)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return new NotificationItem(
            notification.Id.Value,
            notification.Kind.Name,
            notification.SubjectId?.Value,
            notification.SubjectReference,
            notification.ActorName,
            // "You approved KR-1042" reads wrong when someone else did it, and wrong the other way
            // round too. The server knows which it is; the screen should not have to guess from a
            // name that two people might share.
            notification.ActorUserId is { } actor && actor == viewer,
            notification.OccurredAt,
            notification.ReadAt);
    }
}

public sealed record NotificationFeed(
    IReadOnlyList<NotificationItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int UnreadCount);

public sealed record GetMyNotificationsQuery(Id UserId, int Page, int PageSize)
    : IQuery<Result<NotificationFeed, Error>>;

public sealed record GetMyUnreadNotificationCountQuery(Id UserId) : IQuery<Result<int, Error>>;

public sealed record MarkNotificationReadCommand(Id UserId, Id NotificationId)
    : ICommand<UnitResult<Error>>;

public sealed record MarkAllNotificationsReadCommand(Id UserId) : ICommand<Result<int, Error>>;

public sealed class GetMyNotificationsQueryValidator : AbstractValidator<GetMyNotificationsQuery>
{
    public GetMyNotificationsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThan(0);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
    }
}

public static class NotificationErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "notification.not_found",
        "That notification is not yours, or no longer exists.");
}

public sealed class NotificationHandlers(
    INotificationRepository notifications,
    IClock clock,
    IUnitOfWork unitOfWork) :
    IRequestHandler<GetMyNotificationsQuery, Result<NotificationFeed, Error>>,
    IRequestHandler<GetMyUnreadNotificationCountQuery, Result<int, Error>>,
    IRequestHandler<MarkNotificationReadCommand, UnitResult<Error>>,
    IRequestHandler<MarkAllNotificationsReadCommand, Result<int, Error>>
{
    public async Task<Result<NotificationFeed, Error>> Handle(
        GetMyNotificationsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Sequential on purpose: these share the scoped DbContext, and running them concurrently is
        // the mistake the reporting rule in CLAUDE.md exists to stop.
        var skip = (request.Page - 1) * request.PageSize;
        var rows = await notifications.ListForAsync(request.UserId, skip, request.PageSize, cancellationToken);
        var total = await notifications.CountForAsync(request.UserId, cancellationToken);
        var unread = await notifications.CountUnreadForAsync(request.UserId, cancellationToken);

        var items = rows.Select(row => NotificationItem.From(row, request.UserId)).ToList();
        return new NotificationFeed(items, request.Page, request.PageSize, total, unread);
    }

    public async Task<Result<int, Error>> Handle(
        GetMyUnreadNotificationCountQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await notifications.CountUnreadForAsync(request.UserId, cancellationToken);
    }

    public async Task<UnitResult<Error>> Handle(
        MarkNotificationReadCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var notification = await notifications.GetAsync(request.NotificationId, cancellationToken);
        // Not-yours and does-not-exist answer identically. Anything else turns this endpoint into a
        // way to test whether a given id belongs to somebody.
        if (notification is null || !notification.BelongsTo(request.UserId))
            return UnitResult.Failure(NotificationErrors.NotFound);

        notification.MarkRead(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UnitResult.Success<Error>();
    }

    public async Task<Result<int, Error>> Handle(
        MarkAllNotificationsReadCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var changed = await notifications.MarkAllReadAsync(request.UserId, clock.UtcNow, cancellationToken);
        if (changed > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);
        return changed;
    }
}
