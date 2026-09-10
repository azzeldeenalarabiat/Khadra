using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Khadra.WebAPI.Security;

// Last line of defence: anything a handler did not turn into a Result becomes a sanitised ProblemDetails.
internal sealed partial class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, code) = exception switch
        {
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "The request is malformed.", "request.malformed"),
            ConcurrencyConflictException => (StatusCodes.Status409Conflict, "The record was changed by another request. Reload and try again.", "concurrency.conflict"),
            DbUpdateException => (StatusCodes.Status409Conflict, "The request conflicts with existing data.", "data.conflict"),
            // Not a server error: an upload ticket is spent once, so a second write at the same key is
            // refused rather than allowed to replace evidence somebody has already read. For a client
            // retrying after a lost response this is the good news -- the bytes are stored, and the
            // next step is the confirmation call, not another upload.
            DocumentAlreadyExistsException => (StatusCodes.Status409Conflict, "That upload is already stored.", "upload.already_stored"),
            DomainException => (StatusCodes.Status400BadRequest, "The request violates a business rule.", "domain.invalid"),
            OperationCanceledException => (StatusCodes.Status499ClientClosedRequest, "The request was cancelled.", "request.cancelled"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "server.error")
        };

        if (status >= 500)
            LogUnhandled(logger, httpContext.TraceIdentifier, exception);
        else
            LogRejected(logger, status, code, httpContext.TraceIdentifier);

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
                Extensions = { ["code"] = code }
            }
        });
    }

    [LoggerMessage(2000, LogLevel.Error, "Unhandled API exception. TraceId: {TraceId}")]
    private static partial void LogUnhandled(ILogger logger, string traceId, Exception exception);

    [LoggerMessage(2001, LogLevel.Warning, "Request rejected with {StatusCode} ({Code}). TraceId: {TraceId}")]
    private static partial void LogRejected(ILogger logger, int statusCode, string code, string traceId);
}
