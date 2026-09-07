using System.ComponentModel.DataAnnotations;
using Khadra.Application.Bookings.CancelBooking;
using Khadra.Application.Bookings.CreateBooking;
using Khadra.Application.Bookings.DecideBooking;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadBookings;
using Khadra.Application.Common;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// A party's own bookings: what a customer booked, what was booked from a dealership, and the
/// actions each party may take on one.
/// </summary>
/// <remarks>
/// There was no POST here for a long time, on the grounds that creating a booking meant deciding how
/// payment, overlap and delivery were settled. Those decisions have since been made -- the owner
/// reordered the flow to "reserve now, pay after approval" on 2026-09-07 -- so a customer can now
/// ask for a car without a card, and this is where they do it.
/// </remarks>
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

    /// <summary>What a customer sends to ask a gallery for a car.</summary>
    /// <remarks>
    /// No prices. Every figure on the resulting booking is computed and frozen server-side, because
    /// a client that could name a total could name a cheaper one. The delivery location is a pair or
    /// neither, and is only allowed with the Delivery method.
    /// </remarks>
    public sealed record CreateBookingRequest(
        [Required] Guid VehicleId,
        [Required] DateTimeOffset PickupAt,
        [Required] DateTimeOffset ReturnAt,
        [Required, MaxLength(20)] string PickupMethod,
        [Range(-90, 90)] double? Latitude,
        [Range(-180, 180)] double? Longitude);

    /// <summary>
    /// Asks a gallery for a car. Creates the booking in Requested; nothing is paid here.
    /// </summary>
    /// <remarks>
    /// The deposit falls due only if the gallery approves, and the booking holds the car until their
    /// answer window closes. 409 means somebody else took it -- either the guard saw a live hold, or
    /// the database's exclusion constraint refused the write in a race the guard could not see.
    /// </remarks>
    [Authorize(Policy = SecurityPolicies.Customer)]
    [HttpPost]
    [ProducesResponseType<BookingDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Create([FromBody] CreateBookingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await Mediator.Send(
            new CreateBookingCommand(
                actor.UserId!.Value,
                Id.From(request.VehicleId),
                request.PickupAt,
                request.ReturnAt,
                request.PickupMethod,
                request.Latitude,
                request.Longitude),
            cancellationToken);

        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { bookingId = result.Value.BookingId }, result.Value)
            : Failure(result.Error);
    }

    /// <param name="ReasonCode">
    /// One of the codes published on <c>GET /api/v1/app-config</c>. A closed list rather than free
    /// text: a required text box produces "asdf", and neither the gallery nor the owner can count it.
    /// </param>
    /// <param name="Details">Optional, and the customer's own words in their own language.</param>
    public sealed record CancelBookingRequest(
        [Required, MaxLength(40)] string ReasonCode,
        [MaxLength(500)] string? Details);

    /// <summary>
    /// The customer ends their own booking (spec 5.5).
    /// </summary>
    /// <remarks>
    /// Nothing is charged here. A cancellation ASSESSES a penalty and stops (spec 3.3) — money moves
    /// only through an admin resolving a dispute — and before the deposit clears there is nothing to
    /// assess at all. The figure a customer is shown before they confirm is
    /// <c>BookingDto.Cancellation</c>, computed by the server from this booking's own frozen terms.
    ///
    /// Retrying is safe: a second call on a booking this customer already cancelled answers 200 with
    /// the booking rather than a conflict, because a phone that lost the first response would
    /// otherwise tell them their cancellation failed when it succeeded.
    ///
    /// It can also answer with an EXPIRED booking rather than a cancelled one. If the window closed
    /// while the customer was deciding, the clock already ended the booking and the platform released
    /// the car; recording a cancellation over that would say "you cancelled this" where the truth is
    /// "the time ran out".
    /// </remarks>
    [Authorize(Policy = SecurityPolicies.Customer)]
    [HttpPost("{bookingId:guid}/cancel")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Cancel(
        Guid bookingId,
        [FromBody] CancelBookingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await Mediator.Send(
            new CancelMyBookingCommand(actor.UserId!.Value, Id.From(bookingId), request.ReasonCode, request.Details),
            cancellationToken);
        return FromResult(result);
    }

    public sealed record ReportNonDeliveryRequest([Required, MaxLength(1000)] string Details);

    /// <summary>
    /// The customer reports that the gallery never handed the car over (spec 5.5).
    /// </summary>
    /// <remarks>
    /// Refused before the rental was due to start. Without that guard a customer facing a
    /// cancellation penalty could file this days ahead instead, and the record would say the GALLERY
    /// failed to deliver — with a 25-50% assessment against them — leaving the gallery to open a
    /// dispute to clear a claim made without them. <c>MarkNoShow</c>, the mirror-image claim, has
    /// always been guarded this way.
    ///
    /// NOT REACHABLE TODAY. It needs a Confirmed booking, and Confirmed needs a cleared deposit,
    /// which needs the Payments context. The app gates the screen on the booking's own status, so no
    /// customer is shown a button that cannot work.
    /// </remarks>
    [Authorize(Policy = SecurityPolicies.Customer)]
    [HttpPost("{bookingId:guid}/report-non-delivery")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> ReportNonDelivery(
        Guid bookingId,
        [FromBody] ReportNonDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await Mediator.Send(
            new ReportNonDeliveryCommand(actor.UserId!.Value, Id.From(bookingId), request.Details),
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
