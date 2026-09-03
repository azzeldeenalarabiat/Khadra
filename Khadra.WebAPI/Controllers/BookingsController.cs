using System.ComponentModel.DataAnnotations;
using Khadra.Application.Bookings.DecideBooking;
using Khadra.Application.Bookings.Dtos;
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
        [FromQuery] Guid? vehicleId,
        [FromQuery] string? tab,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new ListMyBookingsQuery(actor.UserId!.Value, actor.Role!, status, PageRequest.From(page, pageSize), tab, vehicleId),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>How many bookings sit behind each tab of the caller's list.</summary>
    [HttpGet("tab-counts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> TabCounts(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new GetMyBookingTabCountsQuery(actor.UserId!.Value, actor.Role!),
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

    // ── The dealer's decisions (spec 4.2, 5.4). Owner or ACTIVE employee; approve and reject also
    // require the business to be able to trade, which the policy checks in the pipeline. ──

    public sealed record ApproveRequest([MaxLength(500)] string? Note);

    public sealed record RejectRequest(
        [Required, MaxLength(40)] string ReasonCode,
        [Required, MaxLength(500)] string Details);

    public sealed record HandoverRequest(
        [Range(0, int.MaxValue)] int? OdometerKm,
        [Range(0, 1)] decimal? FuelLevel,
        [MaxLength(1000)] string? Notes,
        [Range(0, double.MaxValue)] decimal? CashCollected);

    /// <summary>Approves a request. The optional note reaches the customer through the booking's history.</summary>
    [Authorize(Policy = SecurityPolicies.ApprovedDealerStaff)]
    [HttpPost("{bookingId:guid}/approve")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Approve(Guid bookingId, [FromBody] ApproveRequest request, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new ApproveBookingCommand(actor.UserId!.Value, Id.From(bookingId), request.Note),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>Rejects a request. Always free for the customer; the reason is shown to them.</summary>
    [Authorize(Policy = SecurityPolicies.ApprovedDealerStaff)]
    [HttpPost("{bookingId:guid}/reject")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Reject(Guid bookingId, [FromBody] RejectRequest request, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new RejectBookingCommand(actor.UserId!.Value, Id.From(bookingId), request.ReasonCode, request.Details),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>Records the handover to the customer. Not gated on trading: an approved rental is honoured.</summary>
    [Authorize(Policy = SecurityPolicies.DealerStaff)]
    [HttpPost("{bookingId:guid}/pickup")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> RecordPickup(Guid bookingId, [FromBody] HandoverRequest request, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new RecordPickupCommand(actor.UserId!.Value, Id.From(bookingId), request.OdometerKm, request.FuelLevel, request.Notes, request.CashCollected),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>Records the car coming back. The settlement window starts from this moment.</summary>
    [Authorize(Policy = SecurityPolicies.DealerStaff)]
    [HttpPost("{bookingId:guid}/return")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> RecordReturn(Guid bookingId, [FromBody] HandoverRequest request, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new RecordReturnCommand(actor.UserId!.Value, Id.From(bookingId), request.OdometerKm, request.FuelLevel, request.Notes, request.CashCollected),
            cancellationToken);
        return FromResult(result);
    }
}
