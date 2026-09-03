using Khadra.Application.Bookings.ReadBookings;
using Khadra.Application.Common;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// A party's own bookings, read-only.
///
/// Customers see what they booked; dealer staff see what was booked from them. There is no POST
/// here on purpose: creating a booking means choosing how payment, overlap and delivery are decided,
/// and those decisions belong to the Booking module, not to a slice whose job is to let a dispute be
/// opened FROM a booking (spec 3.3).
/// </summary>
[ApiController]
[Route("api/v1/bookings")]
[Authorize]
public sealed class BookingsController(ICurrentActor actor) : ApiControllerBase
{
    /// <summary>Lists the signed-in person's bookings, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> List(
        [FromQuery] string? status,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new ListMyBookingsQuery(actor.UserId!.Value, actor.Role!, status, PageRequest.From(page, pageSize)),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>One booking in full. Answers 404 to anyone who is not a party to it.</summary>
    [HttpGet("{bookingId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Get(Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new GetBookingQuery(actor.UserId!.Value, Id.From(bookingId)),
            cancellationToken);
        return FromResult(result);
    }
}
