using Khadra.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Security;

/// <summary>
/// Turns a denied authorization into the same RFC 9457 ProblemDetails every other failure produces.
///
/// ASP.NET's default is an empty 403, which for a dealer gate is close to useless: "you are not a
/// dealer", "you have not applied yet" and "your application is still pending" all look identical,
/// and the last two have very different answers. When a requirement fails with a domain Error this
/// preserves its stable code; anything else falls through to the framework's behaviour untouched.
/// </summary>
internal sealed class ProblemDetailsAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        var error = authorizeResult.AuthorizationFailure?.FailureReasons
            .OfType<DomainAuthorizationFailureReason>()
            .Select(reason => reason.Error)
            .FirstOrDefault();

        // Only a genuine forbid carries a domain reason. A challenge (no token at all) stays a 401
        // so the client knows to authenticate rather than to give up.
        if (authorizeResult.Forbidden && error is not null)
        {
            await WriteAsync(context, error);
            return;
        }

        await _fallback.HandleAsync(next, context, policy, authorizeResult);
    }

    private static async Task WriteAsync(HttpContext context, Error error)
    {
        var status = error.Kind == ErrorKind.NotFound
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status403Forbidden;

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = error.Message,
            Type = $"https://httpstatuses.com/{status}",
            Instance = context.Request.Path,
            Extensions =
            {
                ["code"] = error.Code,
                ["traceId"] = context.TraceIdentifier
            }
        }, context.RequestAborted);
    }
}
