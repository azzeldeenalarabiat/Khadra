using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.RenterDocuments;
using Khadra.Application.Common;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The renter's identity paperwork, for the gallery handing them a car (spec 5.1, spec 7).
/// </summary>
/// <remarks>
/// <para>
/// Spec 5.1 makes the dealer the party who checks a renter's licence, and until this existed they
/// could not: <c>CustomerDocument</c> had written the rule down and no endpoint implemented it
/// (pre-launch item 63).
/// </para>
/// <para>
/// <b>Every route is keyed on the BOOKING.</b> There is no customer id and no bare document id to
/// send, so a dealer session cannot be turned into a lookup service over the customer base: the
/// renter is read off the booking, and a document that is not theirs is not found. Nothing the
/// caller sends is trusted to establish a relationship — the actor comes from the validated token,
/// the dealership from that actor's membership, and the customer from the booking.
/// </para>
/// <para>
/// <b>No signed link, and no storage key.</b> The rest of the platform mints a short-lived HMAC link
/// where the grant is stable for a session. A gallery's right here is not: it lasts exactly as long
/// as <c>Booking.IsLive</c>, so the bytes are streamed by an endpoint that re-checks the whole
/// relationship on every request. Nothing reaches the browser but a booking id and a document id it
/// was given. See <c>OpenRenterDocumentQuery</c> and items 14 and 63 before changing that.
/// </para>
/// <para>
/// <b>Answers:</b> 401 unauthenticated · bodiless 403 for a role that is not dealer staff ·
/// 404 <c>booking.not_found</c> for a booking that is not this dealership's, so another gallery's ids
/// cannot be probed · 409 <c>booking.renter_documents_not_available</c> once the booking stops being
/// live · 404 <c>documents.not_found</c> for a document id that is not this renter's, or whose file
/// has gone · 200 with an empty list when the renter has uploaded nothing, which is a true answer
/// about a booking that does exist and so is never a 404.
/// </para>
/// </remarks>
[Authorize(Policy = SecurityPolicies.DealerStaff)]
[Route("api/v1/bookings/{bookingId:guid}/renter-documents")]
// Its own bucket. NOT RateLimitPolicies.Auth, which the sibling document endpoint carries: for a GET
// no credential subject is ever stashed, so that policy degrades to ten requests per minute per
// address — and a Jordanian carrier, or one gallery's office NAT, is one address, while a handover
// screen loads three or four documents at once. This one is address-keyed too (the limiter runs
// before authentication) but twelve times as generous, and it queues rather than refusing.
[EnableRateLimiting(RateLimitPolicies.PrivateDocuments)]
public sealed class RenterDocumentsController(ICurrentActor actor) : ApiControllerBase
{
    /// <summary>What this booking's renter has on file, and what they are still missing.</summary>
    /// <remarks>Describes the documents. Carries no URL, no storage key and no review note.</remarks>
    [HttpGet]
    [ProducesResponseType<RenterDocumentsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> List(Guid bookingId, CancellationToken cancellationToken)
    {
        // Not only the bytes. This body says which identity papers a named private individual holds
        // and when they filed them, which is spec 7 data in its own right and must not settle into a
        // shared proxy or survive the gallery signing out. Set before the result, so it is on the
        // refusals too — a cached 409 would keep saying "your window has closed" after a booking was
        // reinstated.
        Response.Headers.CacheControl = "no-store, private";

        var result = await Mediator.Send(
            new ViewRenterDocumentsQuery(actor.UserId!.Value, Id.From(bookingId)),
            cancellationToken);
        return FromResult(result);
    }

    /// <summary>The bytes of one of them, streamed, private and never cached.</summary>
    /// <remarks>
    /// The authorization runs again here in full, rather than being inherited from the listing: the
    /// two calls are minutes apart on a handover screen, and the booking can stop being live in
    /// between.
    /// </remarks>
    [HttpGet("{documentId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Open(Guid bookingId, Guid documentId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new OpenRenterDocumentQuery(actor.UserId!.Value, Id.From(bookingId), Id.From(documentId)),
            cancellationToken);

        return FromResult(result, opened => PrivateDocument(opened.Content, opened.ContentType));
    }
}
