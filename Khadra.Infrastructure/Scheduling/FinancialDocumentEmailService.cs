using Khadra.Application.FinancialDocuments.Email;
using Khadra.Infrastructure.Configuration;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Scheduling;

/// <summary>
/// Emails issued receipts to their customers with their PDFs (payments Phase 7): a batch a minute, on a timer of its
/// own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a step in the settlement pass.</b> That pass is the platform's deadline clock — expiries, no-shows, refund
/// sweeps — and every send can hold the transport's whole timeout budget: a mail server that accepts the connection
/// and never greets would turn one batch into minutes, and every booking deadline would slip by as much for as long
/// as mail was down. Rendering is local work; mail is a remote dependency with a known way of stalling. A receipt
/// follows its PDF within a minute, which nobody is waiting on to the second.
/// </para>
/// <para>
/// <b>One scope per email</b>, as issuing and rendering do: an email whose save failed leaves nothing tracked behind
/// it for the next. Claims are exclusive and leased, so two processes overlapping during a deploy take different
/// emails, and a process that dies lets its emails go when their leases run out. A pass slow enough to outlive a lease
/// finds the row claimed again by the other process — its claim count moved — and leaves it to that process. Every
/// failure is logged and swallowed: a pass that throws must not stop the service until somebody restarts the API.
/// </para>
/// </remarks>
internal sealed partial class FinancialDocumentEmailService(
    IServiceScopeFactory scopes,
    IOptions<SchedulingOptions> options,
    IOptions<FinancialDocumentEmailOptions> emails,
    IFinancialDocumentEmailSettings transport,
    ILogger<FinancialDocumentEmailService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.EmailFinancialDocuments)
        {
            LogDisabled(logger);
            return;
        }

        // Production on Brevo (owner, 2026-09-29; pre-launch item 202): the service does not run, so nothing is claimed
        // and nothing sent, while the API and everything else carry on. Receipts are still issued and owed their emails,
        // which wait in the queue — the work queue lists them once they have waited too long.
        if (transport.DeliveryDisabledReason is { } disabled)
        {
            LogDeliveryDisabled(logger, disabled);
            return;
        }

        var interval = TimeSpan.FromSeconds(settings.FinancialDocumentEmailIntervalSeconds);
        LogStarted(logger, interval);
        // Said once at boot, so whoever reads the log knows where TEST receipts can go and what a crash can cost.
        if (transport.TransportDeliversMail && !transport.TransportCapturesMail)
            LogTestRecipients(logger, transport.TransportName, emails.Value.TestRecipients.Count);
        if (transport.TransportName == EmailOptions.BrevoProvider)
            LogBrevoWithoutKey(logger);

        using var timer = new PeriodicTimer(interval);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var claimed = await RunAsync(new ClaimFinancialDocumentEmailsQuery(), cancellationToken);
        if (claimed is null)
            return;

        foreach (var delivery in claimed)
        {
            if (cancellationToken.IsCancellationRequested)
                return;
            await RunAsync(new EmailFinancialDocumentCommand(delivery.DeliveryId, delivery.Claims), cancellationToken);
        }
    }

    private async Task<TResponse?> RunAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down. Not a failure.
        }
#pragma warning disable CA1031 // A step that fails must not take the service, or the next email, down with it.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogStepFailed(logger, request.GetType().Name, exception);
        }

        return default;
    }

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

    [LoggerMessage(2710, LogLevel.Warning,
        "Emailing financial documents is DISABLED. Receipts are issued and stay in the customer's account, and their emails wait in the queue.")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(2711, LogLevel.Information, "Financial document emails worked every {Interval}.")]
    private static partial void LogStarted(ILogger logger, TimeSpan interval);

    [LoggerMessage(2712, LogLevel.Error, "The {Step} step of the document emails failed. The next email and the next pass still run.")]
    private static partial void LogStepFailed(ILogger logger, string step, Exception exception);

    [LoggerMessage(2713, LogLevel.Information,
        "TEST receipts are emailed through {Transport} only to the {Count} address(es) on FinancialDocuments:Email:TestRecipients; every other TEST receipt's email is skipped.")]
    private static partial void LogTestRecipients(ILogger logger, string transport, int count);

    [LoggerMessage(2714, LogLevel.Warning,
        "Receipts emailed through Brevo carry no idempotency key: a process stopped mid-send can email one twice. Accepted for local and Staging only, never for Production (pre-launch item 202).")]
    private static partial void LogBrevoWithoutKey(ILogger logger);

    [LoggerMessage(2715, LogLevel.Warning, "FINANCIAL-DOCUMENT EMAILS ARE NOT SENT. {Reason}")]
    private static partial void LogDeliveryDisabled(ILogger logger, string reason);
}
