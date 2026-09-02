using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.Bff.Security;

internal sealed partial class BffExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<BffExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, code) = exception switch
        {
            AntiforgeryValidationException => (StatusCodes.Status400BadRequest, "Request validation failed.", "bff.antiforgery"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "The session is unavailable or expired.", "bff.session_expired"),
            HttpRequestException => (StatusCodes.Status502BadGateway, "The API is unreachable.", "bff.api_unreachable"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "server.error")
        };

        if (status >= 500)
            LogUnhandled(logger, httpContext.TraceIdentifier, exception);

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Type = $"https://httpstatuses.com/{status}",
                Extensions = { ["code"] = code, ["traceId"] = httpContext.TraceIdentifier }
            }
        });
    }

    [LoggerMessage(3000, LogLevel.Error, "Unhandled BFF exception. TraceId: {TraceId}")]
    private static partial void LogUnhandled(ILogger logger, string traceId, Exception exception);
}
