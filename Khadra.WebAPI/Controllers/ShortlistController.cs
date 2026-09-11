using Khadra.Application.Common;
using Khadra.Application.Shortlist;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The cars the signed-in customer has saved to look at again.
/// </summary>
/// <remarks>
/// <para>
/// Keyed on the ACTOR in every route. There is no customer id in any path here and there must never
/// be one: a route taking one would be a way to read, or edit, somebody else's list, and no check
/// inside it could undo the fact that the id was accepted at all.
/// </para>
/// <para>
/// Account-only, deliberately. The catalogue itself is anonymous — a tourist comparing prices before
/// flying to Jordan has no reason to sign up first — but a saved list that lived on the device would
/// not be a shortlist: it would vanish with the phone, show nothing on a second one, and become a
/// merge problem the day a real one arrived. The heart on an anonymous card goes through the ordinary
/// sign-in redirect with its destination.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/customers/me/shortlist")]
[Authorize(Policy = SecurityPolicies.Customer)]
public sealed class ShortlistController(ICurrentActor actor) : ApiControllerBase
{
    /// <summary>The caller's saved cars, newest save first.</summary>
    /// <remarks>
    /// A car the caller can no longer book comes back NAMED — make, model, year and its gallery —
    /// with no listing beside it, and no reason. The name is safe because nothing reaches a shortlist
    /// that the public catalogue did not return first: <c>SaveVehicleCommand</c> refuses any id
    /// <c>ICatalogueReader.GetAsync</c> answers null to. The REASON is what stays private, because
    /// naming it would distinguish a hidden car from a deleted one from a suspended gallery's, which
    /// the catalogue is deliberately shaped never to do.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> List(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new ListMyShortlistQuery(actor.UserId!.Value), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// Which of a named set of cars the caller has saved.
    /// </summary>
    /// <remarks>
    /// So a catalogue page can draw a heart per card without loading the list. It answers only about
    /// the ids the caller NAMED, which is what stops it being a way to read a shortlist through a
    /// screen that was never shown one.
    ///
    /// A GET with repeated <c>vehicleId</c> parameters rather than a POST: it changes nothing, and a
    /// page of twenty ids is well inside a URL.
    /// </remarks>
    [HttpGet("membership")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> Membership(
        [FromQuery] Guid[] vehicleId,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new WhichAreSavedQuery(
                actor.UserId!.Value,
                [.. (vehicleId ?? []).Select(Id.From)]),
            cancellationToken);

        return FromResult(result, saved => Ok(saved.Select(id => id.Value)));
    }

    /// <summary>
    /// Saves a car. Safe to repeat: saving what is already saved changes nothing and succeeds.
    /// </summary>
    /// <remarks>
    /// Answers 404 for a car this customer may not see — a draft, a hidden one, one in maintenance,
    /// a suspended gallery's, a deleted one and a genuinely unknown id alike. Distinguishing them
    /// would turn this into an enumeration oracle over unpublished inventory for anybody with an
    /// account.
    /// </remarks>
    [HttpPut("{vehicleId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Save(Guid vehicleId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new SaveVehicleCommand(actor.UserId!.Value, Id.From(vehicleId)), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// Removes a car. Safe to repeat, and does NOT require the car to still be visible.
    /// </summary>
    /// <remarks>
    /// That last part matters: an entry for a listing that has since been withdrawn is precisely the
    /// one a customer most wants gone, and a visibility check here would trap it on their list.
    /// </remarks>
    [HttpDelete("{vehicleId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Forget(Guid vehicleId, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new ForgetVehicleCommand(actor.UserId!.Value, Id.From(vehicleId)), cancellationToken);
        return FromResult(result);
    }
}
