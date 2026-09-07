using Khadra.Application.Common;
using Khadra.Application.Notifications;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The signed-in person's own notifications.
///
/// Every route answers for whoever is calling and is scoped by their id in the repository; there is
/// deliberately no route addressable by user id. A notification is the only place on this platform
/// where one member of staff could otherwise learn what a colleague was told, and nothing in the spec
/// asks for that.
///
/// Not restricted by role: an owner, an employee and an administrator all have a bell. What each of
/// them is told about differs because of what raises the rows, not because of what this can read.
/// </summary>
[Authorize]
[Route("api/v1/notifications")]
public sealed class NotificationsController(ICurrentActor actor) : ApiControllerBase
{
    /// <summary>Newest first, paged. The unread count comes with the page so the bell cannot disagree with the list.</summary>
    [HttpGet]
    [ProducesResponseType<NotificationFeed>(StatusCodes.Status200OK)]
    public async Task<ActionResult> List(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new GetMyNotificationsQuery(actor.UserId!.Value, page ?? 1, pageSize ?? 25),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>Just the badge. Small enough to ask for on every navigation, which is what the rail does.</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<ActionResult> UnreadCount(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new GetMyUnreadNotificationCountQuery(actor.UserId!.Value),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// Marks one read. Answers 404 for a notification that is not yours, so one cannot be probed for
    /// by guessing an id.
    /// </summary>
    [HttpPost("{notificationId:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> MarkRead(Guid notificationId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new MarkNotificationReadCommand(actor.UserId!.Value, Id.From(notificationId)),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>Clears the badge. Answers how many rows changed, so nothing has to be re-fetched to know.</summary>
    [HttpPost("read-all")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<ActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new MarkAllNotificationsReadCommand(actor.UserId!.Value),
            cancellationToken);
        return FromResult(result);
    }
}
