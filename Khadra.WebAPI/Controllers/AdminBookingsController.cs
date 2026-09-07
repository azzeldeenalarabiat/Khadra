using System.ComponentModel.DataAnnotations;
using Khadra.Application.Bookings.AdminBookings;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// Every booking on the platform, and the three interventions an administrator can make in one.
///
/// Its own controller rather than a branch inside <c>BookingsController</c>: that controller's
/// actions are authenticated-only and scoped to whoever is asking, and one forgotten attribute on a
/// new action there would hand the whole platform's book to any signed-in customer. Admin-only at the
/// class level means a route added later is restricted by default rather than by remembering.
///
/// Nothing here moves money. Penalties are ASSESSED against the terms each booking froze; money moves
/// only when an Admin resolves a dispute ticket.
/// </summary>
[Authorize(Policy = SecurityPolicies.Admin)]
[Route("api/v1/admin/bookings")]
public sealed class AdminBookingsController : ApiControllerBase
{
    public sealed record CancelRequest([Required, MaxLength(1000)] string Reason);

    /// <summary>The platform's bookings, newest first, filtered the way an admin working them would.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<BookingListItem>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? tab,
        [FromQuery] Guid? dealerId,
        [FromQuery] Guid? customerId,
        [FromQuery] string? reference,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new ListAllBookingsQuery(
                status,
                tab,
                dealerId,
                customerId,
                reference,
                PageRequest.From(page, pageSize)),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>How many bookings sit behind each tab, under the same dealer/customer scope.</summary>
    [HttpGet("tab-counts")]
    [ProducesResponseType<IReadOnlyDictionary<string, int>>(StatusCodes.Status200OK)]
    public async Task<ActionResult> TabCounts(
        [FromQuery] Guid? dealerId,
        [FromQuery] Guid? customerId,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetAllBookingTabCountsQuery(dealerId, customerId), cancellationToken);
        return FromResult(result);
    }

    /// <summary>One booking in full: the frozen price and terms, the handovers, the whole history.</summary>
    [HttpGet("{bookingId:guid}")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Get(Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetAnyBookingQuery(Id.From(bookingId)), cancellationToken);
        return FromResult(result);
    }

    /// <summary>Cancels the booking on the platform's behalf. Assesses no penalty against either party.</summary>
    [HttpPost("{bookingId:guid}/cancel")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Cancel(Guid bookingId, [FromBody] CancelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new CancelBookingAsAdminCommand(Id.From(bookingId), request.Reason),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// Ends a booking whose own deadline has passed. Refused while the frozen window still has time
    /// in it, so this cannot be used to end a booking early.
    /// </summary>
    [HttpPost("{bookingId:guid}/expire")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Expire(Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new ExpireBookingAsAdminCommand(Id.From(bookingId)), cancellationToken);
        return FromResult(result);
    }

    /// <summary>Records that the customer never collected the car, once the no-show window has run out.</summary>
    [HttpPost("{bookingId:guid}/no-show")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> MarkNoShow(Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new MarkBookingNoShowAsAdminCommand(Id.From(bookingId)), cancellationToken);
        return FromResult(result);
    }
}
