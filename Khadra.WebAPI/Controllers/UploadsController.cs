using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// Step two of the spec 4.3 upload flow: the target a signed ticket points at.
///
/// This is the local stand-in for an S3 presigned PUT, and it is deliberately shaped like one. The
/// TICKET is the authorisation: it names the exact key and content type, it expires, and it was
/// issued only to a dealer who owns the car. A session is required as well, so a leaked ticket alone
/// is not enough — the belt-and-braces an object store cannot give us, kept while we can have it.
/// </summary>
[Route("api/v1/uploads")]
public sealed class UploadsController(
    IUploadTicketService tickets,
    IDocumentStorage storage,
    IDocumentPolicySettings policy,
    IClock clock) : ApiControllerBase
{
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [HttpPut("{token}")]
    [RequestSizeLimit(16 * 1024 * 1024)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult> Upload(string token, CancellationToken cancellationToken)
    {
        // An invalid or expired ticket reads as not found rather than forbidden: a 403 would tell a
        // caller probing tokens that they had guessed a real one.
        if (!tickets.TryRedeem(token, clock.UtcNow, out var upload))
            return NotFound();

        // The declared type must match what the ticket was issued for. Otherwise a ticket for a JPEG
        // becomes a way to store anything at all.
        var declared = Request.ContentType?.Split(';')[0].Trim();
        if (!string.Equals(declared, upload.ContentType, StringComparison.OrdinalIgnoreCase))
            return BadRequest();

        if (Request.ContentLength is null || Request.ContentLength <= 0)
            return BadRequest();
        if (Request.ContentLength > policy.MaximumSizeBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);

        // At the key the ticket committed to, not one of the storage layer's choosing: the
        // confirmation step looks the file up by exactly that key.
        await storage.SaveAtAsync(upload.StorageKey, upload.ContentType, Request.Body, cancellationToken);

        return NoContent();
    }
}

/// <summary>
/// Serves car photos.
///
/// Anonymous and cacheable, unlike every other file this platform stores. Spec 7 draws the line
/// explicitly — "never public URLs like car images" — so identity documents go through expiring
/// signed links while marketing photos are simply public. The path is restricted to the vehicles
/// scope so this endpoint can never be talked into serving a passport.
/// </summary>
[Route("api/v1/vehicle-images")]
public sealed class VehicleImagesController(IDocumentStorage storage) : ApiControllerBase
{
    [AllowAnonymous]
    [HttpGet("vehicles/{vehicleId:guid}/{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Get(Guid vehicleId, string fileName, CancellationToken cancellationToken)
    {
        var key = $"vehicles/{vehicleId}/{fileName}";
        var content = await storage.OpenAsync(key, cancellationToken);
        if (content is null)
            return NotFound();

        // Immutable: the key contains a fresh guid per upload, so a photo at a given URL never changes.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(content, ContentTypeFor(fileName));
    }

    private static string ContentTypeFor(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
}
