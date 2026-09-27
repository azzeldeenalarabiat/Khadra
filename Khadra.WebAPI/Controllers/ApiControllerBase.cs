using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Domain.Common;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _mediator;

    protected ISender Mediator => _mediator ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    protected ClientInfo Client => new(
        HttpContext.Connection.RemoteIpAddress?.ToString(),
        Request.Headers.UserAgent.ToString());

    // Maps a Result-returning handler to HTTP. Success is 200 by default; pass `onSuccess` for 201/202/204.
    protected ActionResult FromResult<T>(Result<T, Error> result, Func<T, ActionResult>? onSuccess = null) =>
        result.IsSuccess
            ? onSuccess?.Invoke(result.Value) ?? Ok(result.Value)
            : Failure(result.Error);

    protected ActionResult FromResult(UnitResult<Error> result, Func<ActionResult>? onSuccess = null) =>
        result.IsSuccess
            ? onSuccess?.Invoke() ?? NoContent()
            : Failure(result.Error);

    /// <summary>
    /// Puts the bytes of a PRIVATE document on the wire, the one way this platform does it.
    /// </summary>
    /// <remarks>
    /// Shared rather than repeated, because there are now two ways to EARN a private file — a signed
    /// link the platform minted, and a live booking relationship re-checked per request — and only
    /// one way it should ever be delivered. Two copies of these headers would drift, and the way they
    /// would drift is one of them losing <c>no-store</c> and an identity document settling into a
    /// shared proxy.
    ///
    /// No <c>fileDownloadName</c>: a name would have to be invented (keys are generated guids) or
    /// taken from what the customer typed, and neither is something to hand a caller.
    /// </remarks>
    protected FileStreamResult PrivateDocument(Stream content, string contentType)
    {
        // An identity document must not linger in a shared proxy or the browser's disk cache.
        KeepOutOfCaches();
        // Belt and braces on a body the caller did not name: nothing here is ever a page.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(content, contentType);
    }

    /// <summary>
    /// Keeps this answer out of every cache — a shared proxy's and the browser's own disk — for a body that
    /// IS somebody's private record: an identity document's bytes, the list of a renter's documents, or a
    /// customer's financial documents, which carry their name and their money (payments Phase 5b; owner,
    /// 2026-09-27). One place, so they can never drift apart. Called before the handler runs, so refusals
    /// carry it too.
    /// </summary>
    protected void KeepOutOfCaches() => Response.Headers.CacheControl = "no-store, private";

    // RFC 9457 ProblemDetails with a stable machine `code`; validation details go under `errors`.
    protected ObjectResult Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            // A mail relay that refused the message is not the caller getting the request wrong.
            ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = error.Message,
            Type = $"https://httpstatuses.com/{status}",
            Instance = Request.Path,
            Extensions =
            {
                ["code"] = error.Code,
                ["traceId"] = HttpContext.TraceIdentifier
            }
        };
        if (error.Details is not null)
            problem.Extensions["errors"] = error.Details;
        // Top-level, beside `code`: a refusal that carries the figure that changed (booking.refund_changed).
        // The reserved names are never overwritten by a payload.
        if (error.Extensions is not null)
        {
            foreach (var (key, value) in error.Extensions)
            {
                if (key is not ("code" or "traceId" or "errors"))
                    problem.Extensions[key] = value;
            }
        }

        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
