using Khadra.Application.Notifications.Delivery;
using Khadra.Infrastructure.Configuration;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Scheduling;

/// <summary>
/// Works the notification outbox: pushes and reminder emails, a batch every few seconds.
/// </summary>
/// <remarks>
/// <para>
/// A timer of its own rather than a step in the settlement pass, because the two answer to different
/// clocks: settlement is about deadlines measured in minutes, and a push about an approval the customer
/// is waiting on should leave within seconds of the commit.
/// </para>
/// <para>
/// <b>Safe to run twice.</b> Claims are exclusive and leased (see
/// <c>INotificationDeliveryRepository.ClaimDueAsync</c>), so the old and new process overlapping during
/// a zero-downtime deploy each take different rows, and a process that dies mid-batch releases its rows
/// when the lease runs out. A pass drains up to one batch; a backlog drains over successive ticks.
/// </para>
/// </remarks>
internal sealed partial class NotificationDispatchService(
    IServiceScopeFactory scopes,
    IOptions<SchedulingOptions> options,
    ILogger<NotificationDispatchService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.DispatchNotifications)
        {
            LogDisabled(logger);
            return;
        }

        var interval = TimeSpan.FromSeconds(settings.NotificationDispatchIntervalSeconds);
        LogStarted(logger, interval);

        using var timer = new PeriodicTimer(interval);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();
            await mediator.Send(new DeliverNotificationsCommand(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down. Not a failure.
        }
#pragma warning disable CA1031 // A pass that fails must not take the service down with it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogPassFailed(logger, exception);
        }
    }

    [LoggerMessage(2320, LogLevel.Warning,
        "Notification dispatch is DISABLED. Push notifications and reminder emails will not be sent.")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(2321, LogLevel.Information, "Notification dispatch running every {Interval}.")]
    private static partial void LogStarted(ILogger logger, TimeSpan interval);

    [LoggerMessage(2322, LogLevel.Error, "A notification dispatch pass failed. The next pass will retry.")]
    private static partial void LogPassFailed(ILogger logger, Exception exception);

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
