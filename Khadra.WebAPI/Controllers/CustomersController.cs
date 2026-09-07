using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Application.IdentityAccess.UpdateProfile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// A customer's own account details.
/// </summary>
/// <remarks>
/// Scoped to the ACTOR, never to an id in the path — the same shape as
/// <see cref="CustomerDocumentsController"/>, and for the same reason: there is no route through
/// which one customer can read or edit another's.
///
/// Rate limited despite being authenticated. The uniqueness check on a phone number answers
/// <c>auth.phone_taken</c>, which is an enumeration oracle in the same family as the ones recorded in
/// pre-launch checklist items 9, 31, 37 and 44: a signed-in caller could otherwise walk a number
/// range and learn which are registered. The limit is what makes that expensive.
/// </remarks>
[ApiController]
[Authorize(Policy = SecurityPolicies.Customer)]
[Route("api/v1/customers/me")]
public sealed class CustomersController(ICurrentActor actor) : ApiControllerBase
{
    /// <param name="FullName">Shown to a gallery on the bookings this account makes.</param>
    /// <param name="Phone">
    /// Normalised by the server: a local <c>07…</c> comes back as <c>+9627…</c>. A client renders the
    /// value it is given back rather than what was typed, or the two disagree.
    /// </param>
    public sealed record UpdateMyProfileRequest(
        [param: Required, StringLength(150)] string FullName,
        [param: Required, StringLength(32)] string Phone);

    /// <summary>
    /// Corrects the caller's name and phone number.
    /// </summary>
    /// <remarks>
    /// Email is not editable here. It is the sign-in identifier and the address a password reset goes
    /// to, so moving it on a live session alone would hand the account to anyone holding an unlocked
    /// phone; it needs its own verified flow, which is pre-launch checklist item 44.
    ///
    /// The JWT's name claim goes stale until the next refresh, which is harmless because nothing
    /// reads identity from the token — a client shows the <see cref="UserDto"/> this returns.
    /// </remarks>
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [HttpPut("profile")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> UpdateProfile(
        [FromBody] UpdateMyProfileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await Mediator.Send(
            new UpdateMyProfileCommand(actor.UserId!.Value, request.FullName, request.Phone),
            cancellationToken);
        return FromResult(result);
    }
}
