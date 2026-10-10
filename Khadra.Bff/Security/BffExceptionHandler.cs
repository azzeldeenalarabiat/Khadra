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
            // Not a 401: the session is kept, and a 401 is what sends the website and the console to sign-in (item 246).
            BffSessionRefreshUnavailableException => (StatusCodes.Status503ServiceUnavailable, "The session could not be refreshed just now. Try again shortly.", "bff.session_refresh_unavailable"),
            HttpRequestException => (StatusCodes.Status502BadGateway, "The API is unreachable.", "bff.api_unreachable"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "server.error")
        };

        if (exception is BffSessionRefreshUnavailableException unavailable)
        {
            // A passing failure, not a defect: a warning naming the cause, never the "unhandled" error line.
            LogRefreshUnavailable(
                logger,
                unavailable.Status is { } answered ? ((int)answered).ToString(System.Globalization.CultureInfo.InvariantCulture) : "no answer",
                unavailable.Code ?? "-",
                unavailable.InnerException?.GetType().Name ?? "-",
                httpContext.TraceIdentifier);
            AuthApiResult.WriteRetryAfter(httpContext.Response, unavailable.RetryAfter);
        }
        else if (status >= 500)
        {
            LogUnhandled(logger, httpContext.TraceIdentifier, exception);
        }

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

    [LoggerMessage(3002, LogLevel.Warning,
        "A session refresh could not complete (status {Status}, code {Code}, exception {ExceptionType}); the session was kept and the browser told to retry with 503. TraceId: {TraceId}")]
    private static partial void LogRefreshUnavailable(ILogger logger, string status, string code, string exceptionType, string traceId);
}
