using Khadra.Application.Common.Ports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// Serves a private document to a caller holding a valid short-lived link (spec 7).
///
/// Two independent checks, deliberately. The signature proves the link was minted by this platform
/// for this exact file and has not expired; the inherited authentication requirement proves there is
/// still a live session behind the request. A signature alone would turn a leaked URL into a working
/// one for as long as it lasted, and a session alone would let any signed-in user enumerate keys.
/// </summary>
[Route("api/v1/documents")]
public sealed class DocumentsController(
    IDocumentStorage storage,
    IDocumentLinkSigner signer,
    Application.Common.IClock clock) : ApiControllerBase
{
    [HttpGet("{token}")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Download(
        string token,
        [FromQuery] long expires,
        [FromQuery] string signature,
        CancellationToken cancellationToken)
    {
        if (!signer.TryDecodeToken(token, out var storageKey))
            return NotFound();

        // A bad or stale signature is reported as "not found", not "forbidden": a 403 would confirm
        // that the document exists to someone holding nothing but a guessed key.
        if (!signer.IsValid(storageKey, expires, signature, clock.UtcNow))
            return NotFound();

        var content = await storage.OpenAsync(storageKey, cancellationToken);
        if (content is null)
            return NotFound();

        // No-store: an identity document must not linger in a shared proxy or the browser cache.
        Response.Headers.CacheControl = "no-store, private";
        return File(content, ContentTypeFor(storageKey));
    }

    private static string ContentTypeFor(string storageKey) =>
        Path.GetExtension(storageKey).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => "image/jpeg"
        };
}
