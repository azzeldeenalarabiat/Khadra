using Khadra.Application.AdminDashboard.Dtos;
using Khadra.Application.AdminDashboard.GetAdminWorkload;
using Khadra.Application.AdminDashboard.GetAttentionQueue;
using Khadra.Application.AdminDashboard.GetBookingCounts;
using Khadra.Application.AdminDashboard.GetBookingTrend;
using Khadra.Application.AdminDashboard.GetCustomerCounts;
using Khadra.Application.AdminDashboard.GetDealerCounts;
using Khadra.Application.AdminDashboard.GetDisputeCounts;
using Khadra.Application.AdminDashboard.GetRecentActivity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

// One endpoint per panel, and a screen calls only the ones it draws.
//
// This was a single /dashboard returning the whole landing screen, defended as "one screen, one
// refresh". Two things were wrong with that. The navigation rail reads two of these numbers on EVERY
// admin screen, so opening the dealer queue paid for the attention queue, the fourteen-day trend and
// the audit feed to render two integers -- measured at 5,305 bytes for 40 bytes of badge. And because
// it was one root resource fetched once, those badges were frozen at the first paint of the session:
// approving a dealer left the rail still asking for it.
//
// The old comment warned that several requests would be worse for the browser and the database.
// Measured through this BFF, four parallel requests cost 34ms against 21ms for one composite -- and
// each of those four did the FULL composition, four times the work a real panel does. The reason is
// in the split itself: the composite's readers had to run sequentially because they share one scoped
// DbContext, while a request per panel gets a scope per panel and they genuinely run at once.
//
// The Admin policy stays at the CLASS level. Program.cs sets a fallback policy that only requires an
// authenticated user, so an action here without an explicit policy would be readable by any dealer
// employee holding a valid bearer token. Platform-wide counts, the dispute queue and the audit feed
// are none of their business, and putting it on the class means a future action cannot forget it.
[Authorize(Policy = SecurityPolicies.Admin)]
[Route("api/v1/admin")]
public sealed class AdminDashboardController : ApiControllerBase
{
    /// <summary>
    /// What is sitting with the platform: applications awaiting review, and live disputes.
    ///
    /// The one request every admin screen makes, so it is deliberately the cheapest -- two single-row
    /// aggregates. Named for what it counts rather than the badge that draws it.
    /// </summary>
    [HttpGet("workload")]
    [ProducesResponseType<AdminWorkloadDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> Workload(CancellationToken cancellationToken) =>
        FromResult(await Mediator.Send(new GetAdminWorkloadQuery(), cancellationToken));

    /// <summary>Rental offices by where they stand in the licence check.</summary>
    [HttpGet("dashboard/dealer-counts")]
    [ProducesResponseType<DealerCountsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> DealerCounts(CancellationToken cancellationToken) =>
        FromResult(await Mediator.Send(new GetDealerCountsQuery(), cancellationToken));

    /// <summary>Bookings in total, today, live, and awaiting a dealer's answer.</summary>
    [HttpGet("dashboard/booking-counts")]
    [ProducesResponseType<BookingCountsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> BookingCounts(CancellationToken cancellationToken) =>
        FromResult(await Mediator.Send(new GetBookingCountsQuery(), cancellationToken));

    /// <summary>Renters by verification state.</summary>
    [HttpGet("dashboard/customer-counts")]
    [ProducesResponseType<CustomerCountsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> CustomerCounts(CancellationToken cancellationToken) =>
        FromResult(await Mediator.Send(new GetCustomerCountsQuery(), cancellationToken));

    /// <summary>Tickets open, under review, overdue, and settled inside the reporting window.</summary>
    [HttpGet("dashboard/dispute-counts")]
    [ProducesResponseType<DisputeCountsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> DisputeCounts(CancellationToken cancellationToken) =>
        FromResult(await Mediator.Send(new GetDisputeCountsQuery(), cancellationToken));

    /// <summary>The work queue, ordered by the deadline each record froze.</summary>
    [HttpGet("dashboard/attention-queue")]
    [ProducesResponseType<AttentionQueueDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> AttentionQueue(CancellationToken cancellationToken) =>
        FromResult(await Mediator.Send(new GetAttentionQueueQuery(), cancellationToken));

    /// <summary>Daily bookings over the reporting window, in the platform's local calendar.</summary>
    [HttpGet("dashboard/booking-trend")]
    [ProducesResponseType<BookingTrendDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> BookingTrend(CancellationToken cancellationToken) =>
        FromResult(await Mediator.Send(new GetBookingTrendQuery(), cancellationToken));

    /// <summary>The last few privileged actions. Not the audit log: no filters, no paging.</summary>
    [HttpGet("dashboard/activity")]
    [ProducesResponseType<ActivityFeedDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Activity(CancellationToken cancellationToken) =>
        FromResult(await Mediator.Send(new GetRecentActivityQuery(), cancellationToken));
}
