using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.Common.Behaviors;

public sealed partial class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    ICurrentActor currentActor)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await next(cancellationToken);
        }
        finally
        {
            stopwatch.Stop();
            LogHandled(logger, requestName, stopwatch.ElapsedMilliseconds, currentActor.CorrelationId);
        }
    }

    [LoggerMessage(1000, LogLevel.Information, "Handled {RequestName} in {ElapsedMs} ms (correlation {CorrelationId})")]
    private static partial void LogHandled(ILogger logger, string requestName, long elapsedMs, string correlationId);
}
