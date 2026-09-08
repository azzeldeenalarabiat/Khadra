using Khadra.Application.Bookings.SettleBookings;
using Khadra.Application.Payments.SettlePayments;
using Khadra.Infrastructure.Configuration;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Scheduling;

/// <summary>
/// The timer that closes pre-launch checklist item 4.
/// </summary>
/// <remarks>
/// Four booking rules were enforced correctly wherever anyone asked and nobody asked on a schedule.
/// This asks. It owns no rules of its own — every decision belongs to the aggregate, and this only
/// decides HOW OFTEN to look.
///
/// A scope per pass, because the handler's dependencies are scoped and a background service is a
/// singleton. Every exception is swallowed and logged: a hosted service that throws out of
/// ExecuteAsync stops for the lifetime of the process, and a settlement pass failing once is not a
/// reason to stop settling bookings until somebody restarts the API.
///
/// Since 2026-09-08 it drives the payment sweep as well, which closes checkout attempts a provider
/// stopped talking about and sends the refunds the platform owes. Same reasoning, same scope, same
/// swallow-and-log: neither job owns a rule, and both only decide how often to look.
///
/// One instance is assumed. Two processes running this concurrently is not a correctness problem —
/// the transitions are idempotent and the concurrency token makes the loser retry — but it is wasted
/// work, and a leader election belongs with the deployment story rather than here. Recorded on the
/// pre-launch checklist.
/// </remarks>
internal sealed partial class BookingSettlementService(
    IServiceScopeFactory scopes,
    IOptions<SchedulingOptions> options,
    ILogger<BookingSettlementService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.SettleBookings)
        {
            LogDisabled(logger);
            return;
        }

        var interval = TimeSpan.FromSeconds(settings.SettlementIntervalSeconds);
        LogStarted(logger, interval);

        // A first pass on startup, before the first tick. A process that has been down over a
        // deadline should not wait another whole interval to notice.
        await RunOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await SafeWaitAsync(timer, stoppingToken))
            await RunOnceAsync(stoppingToken);
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<ISender>();
            await mediator.Send(new SettleDueBookingsCommand(), cancellationToken);

            // Payments after bookings, and in the same pass rather than on a timer of their own.
            // The order matters: expiring an unpaid booking is what makes its open checkout pointless,
            // and sweeping in that order closes the attempt on the same tick rather than the next.
            // A second timer would buy nothing and give two schedules to reason about.
            await mediator.Send(new SettlePaymentsCommand(), cancellationToken);
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

    [LoggerMessage(
        2200,
        LogLevel.Warning,
        "Booking settlement is DISABLED. Requests, approvals, no-shows and returns will keep their old status past their deadlines.")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(2201, LogLevel.Information, "Booking settlement running every {Interval}.")]
    private static partial void LogStarted(ILogger logger, TimeSpan interval);

    [LoggerMessage(2202, LogLevel.Error, "A booking settlement pass failed. The next pass will retry.")]
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
