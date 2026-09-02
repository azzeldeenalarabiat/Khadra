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

        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
