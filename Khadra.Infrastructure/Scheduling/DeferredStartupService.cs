using System.Data.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.AdminUsers;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Payments;
using Khadra.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Khadra.Infrastructure.Scheduling;

/// <summary>What the boot left for later, because the database did not answer then (pre-launch item 221).</summary>
public sealed class DeferredStartupWork
{
    private int _adminBootstrap;

    /// <summary>The first-administrator bootstrap could not ask the database at boot, and must still run.</summary>
    public bool AdminBootstrapPending => Volatile.Read(ref _adminBootstrap) == 1;

    public void DeferAdminBootstrap() => Volatile.Write(ref _adminBootstrap, 1);

    internal void AdminBootstrapSettled() => Volatile.Write(ref _adminBootstrap, 0);
}

/// <summary>
/// Asks again, in the background, what the boot could not ask because the database did not answer — so a temporary
/// outage no longer stops the API, and nothing the boot guards is skipped (pre-launch item 221; owner, 2026-10-01).
/// </summary>
/// <remarks>
/// <para>
/// <b>The payments guard.</b> Until <see cref="PaymentDatabaseCheck"/> reads the database cleanly, every payment
/// operation is held (<see cref="VerifiedPaymentProvider"/>). A clean read opens the latch; finding the other kind of
/// money refuses for good and stops the host with exit code 1 — the same outcome as a refusal at boot, the moment the
/// question can be answered, and before any payment could be taken. It runs whatever the provider: with none, a
/// database holding sandbox money still has to be refused, and an unreadable boot used to skip that question for good.
/// </para>
/// <para>
/// <b>The first administrator.</b> <c>AdminBootstrapper</c> is the one thing no user action can retry, so a bootstrap
/// the boot could not run is run here until it completes. A refusal it finds now — the address or phone already held
/// by another account — stops the host as it would have at boot.
/// </para>
/// <para>
/// The first attempt comes a moment after start and the wait doubles to half a minute, so payments reopen within
/// about thirty seconds of the database coming back. A stop request ends the wait at once.
/// </para>
/// <para>
/// <b>What it says.</b> A database that does not answer is reported once at Warning and then at Debug, so an outage
/// does not bury the log. A database that ANSWERS and says no — a missing table, a refused login, no such database —
/// is reported at Warning on every attempt, with its SQLSTATE, because no amount of waiting fixes that. And a process
/// with no provider is never told its payments are held: it takes none either way.
/// </para>
/// </remarks>
internal sealed partial class DeferredStartupService(
    IServiceScopeFactory scopes,
    PaymentVerification verification,
    DeferredStartupWork work,
    IHostApplicationLifetime lifetime,
    ILogger<DeferredStartupService> logger) : BackgroundService
{
    internal static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(30);

    private int _paymentAttempts;
    private int _bootstrapAttempts;

    private bool HasWork => !verification.IsSettled || work.AdminBootstrapPending;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Never hold up the host's start on a connection attempt.
        await Task.Yield();

        var wait = FirstWait;
        while (HasWork && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RunOnceAsync(stoppingToken);
            wait = TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, LongestWait.Ticks));
        }
    }

    /// <summary>One attempt at everything still pending.</summary>
    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (!verification.IsSettled)
            await VerifyPaymentsAsync(cancellationToken);
        if (work.AdminBootstrapPending && !lifetime.ApplicationStopping.IsCancellationRequested)
            await BootstrapAdministratorAsync(cancellationToken);
    }

    private async Task VerifyPaymentsAsync(CancellationToken cancellationToken)
    {
        PaymentDatabaseVerdict verdict;
        string providerName;
        bool canConfirm;
        try
        {
            using var scope = scopes.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<IPaymentProvider>();
            providerName = provider.Name;
            canConfirm = PaymentDatabaseCheck.CanConfirmBookings(provider);
            verdict = await PaymentDatabaseCheck.RunAsync(
                scope.ServiceProvider.GetRequiredService<KhadraDbContext>(), provider, cancellationToken);
        }
        catch (Exception unexpected) when (unexpected is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Payments stay held. A failure nobody expected must not open the latch, nor stop the host on a guess —
            // a cancellation this service did not ask for included.
            LogVerificationFailed(logger, unexpected);
            return;
        }

        switch (verdict)
        {
            case PaymentDatabaseVerdict.Clean when canConfirm:
                verification.MarkVerified();
                LogPaymentsVerified(logger, providerName);
                if (PaymentProviders.IsSandbox(providerName))
                    LogSandboxEnabled(logger);
                break;
            case PaymentDatabaseVerdict.Clean:
                verification.MarkVerified();
                LogGuardSatisfiedWithoutProvider(logger);
                break;
            case PaymentDatabaseVerdict.Mismatched mismatched:
                verification.MarkRefused();
                LogPaymentsRefused(logger, mismatched.Message);
                Stop();
                break;
            case PaymentDatabaseVerdict.Unreadable unreadable:
                ReportPaymentsUnreadable(++_paymentAttempts, unreadable.Cause, canConfirm);
                break;
        }
    }

    private void ReportPaymentsUnreadable(int attempt, Exception exception, bool canConfirm)
    {
        if (DatabaseUnreadable.CauseOf(exception) is DbException { IsTransient: false } answered)
            LogPaymentsAnsweredNo(logger, answered.SqlState ?? "none", attempt, answered.Message);
        else if (attempt > 1)
            LogPaymentsStillUnreadableAgain(logger, attempt, DatabaseUnreadable.Reason(exception));
        else if (canConfirm)
            LogPaymentsStillHeld(logger, DatabaseUnreadable.Reason(exception));
        else
            LogGuardStillWaitingWithoutProvider(logger, DatabaseUnreadable.Reason(exception));
    }

    private async Task BootstrapAdministratorAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().EnsureAsync(cancellationToken);
            work.AdminBootstrapSettled();
            LogBootstrapRan(logger);
        }
        catch (Exception unreadable) when (DatabaseUnreadable.IsCauseOf(unreadable))
        {
            ++_bootstrapAttempts;
            if (DatabaseUnreadable.CauseOf(unreadable) is DbException { IsTransient: false } answered)
                LogBootstrapAnsweredNo(logger, answered.SqlState ?? "none", _bootstrapAttempts, answered.Message);
            else if (_bootstrapAttempts == 1)
                LogBootstrapStillWaiting(logger, DatabaseUnreadable.Reason(unreadable));
            else
                LogBootstrapStillWaitingAgain(logger, _bootstrapAttempts, DatabaseUnreadable.Reason(unreadable));
        }
        catch (OperationCanceledException unasked) when (!cancellationToken.IsCancellationRequested)
        {
            // Nothing said no, so this is not a refusal: asked again, like a database that did not answer.
            LogBootstrapStillWaitingAgain(logger, ++_bootstrapAttempts, unasked.Message);
        }
        catch (Exception refused) when (refused is not OperationCanceledException)
        {
            // What the boot would have refused — the address or phone already held by another account.
            work.AdminBootstrapSettled();
            LogBootstrapRefused(logger, refused.Message, refused);
            Stop();
        }
    }

    private void Stop()
    {
        Environment.ExitCode = 1;
        lifetime.StopApplication();
    }

    [LoggerMessage(2900, LogLevel.Information,
        "The payments guard is satisfied now: this database holds no payment of another kind of money than "
        + "{Provider}'s. Whatever this provider can take, it takes from now on.")]
    private static partial void LogPaymentsVerified(ILogger logger, string provider);

    [LoggerMessage(2901, LogLevel.Critical,
        "PAYMENTS REFUSED, and the API is stopping: {Reason}")]
    private static partial void LogPaymentsRefused(ILogger logger, string reason);

    [LoggerMessage(2902, LogLevel.Warning,
        "PAYMENTS ARE STILL HELD: the payments table still cannot be read, so nothing is accepted yet. "
        + "Asking again shortly. {Reason}")]
    private static partial void LogPaymentsStillHeld(ILogger logger, string reason);

    [LoggerMessage(2903, LogLevel.Debug,
        "The payments guard still cannot read the payments table after {Attempts} attempts. {Reason}")]
    private static partial void LogPaymentsStillUnreadableAgain(ILogger logger, int attempts, string reason);

    [LoggerMessage(2909, LogLevel.Warning,
        "The payments guard still cannot read the payments table. No payment provider is configured, so nothing is "
        + "accepted either way; it asks again shortly, and stops the API if the database holds sandbox payments. {Reason}")]
    private static partial void LogGuardStillWaitingWithoutProvider(ILogger logger, string reason);

    [LoggerMessage(2910, LogLevel.Warning,
        "The database refused the payments guard's question ({SqlState}, attempt {Attempts}) — waiting will not fix "
        + "that, so this is said on every attempt. Where a provider is configured, payments stay held. {Reason}")]
    private static partial void LogPaymentsAnsweredNo(ILogger logger, string sqlState, int attempts, string reason);

    [LoggerMessage(2912, LogLevel.Warning, PaymentDatabaseCheck.SandboxEnabled)]
    private static partial void LogSandboxEnabled(ILogger logger);

    [LoggerMessage(2913, LogLevel.Information,
        "The payments guard has now read this database: it holds no sandbox payment. No payment provider is "
        + "configured, so nothing is accepted, as before.")]
    private static partial void LogGuardSatisfiedWithoutProvider(ILogger logger);

    [LoggerMessage(2904, LogLevel.Error,
        "Checking the payments table failed unexpectedly. Payments stay held; asking again shortly.")]
    private static partial void LogVerificationFailed(ILogger logger, Exception exception);

    [LoggerMessage(2905, LogLevel.Information, "The first-administrator bootstrap has now run.")]
    private static partial void LogBootstrapRan(ILogger logger);

    [LoggerMessage(2906, LogLevel.Warning,
        "The first-administrator bootstrap is still waiting for the database. Asking again shortly. {Reason}")]
    private static partial void LogBootstrapStillWaiting(ILogger logger, string reason);

    [LoggerMessage(2907, LogLevel.Debug, "The first-administrator bootstrap still waiting after {Attempts} attempts. {Reason}")]
    private static partial void LogBootstrapStillWaitingAgain(ILogger logger, int attempts, string reason);

    [LoggerMessage(2911, LogLevel.Warning,
        "The database refused the first-administrator bootstrap's question ({SqlState}, attempt {Attempts}) — waiting "
        + "will not fix that, so this is said on every attempt. {Reason}")]
    private static partial void LogBootstrapAnsweredNo(ILogger logger, string sqlState, int attempts, string reason);

    [LoggerMessage(2908, LogLevel.Critical,
        "The first-administrator bootstrap was refused, and the API is stopping: {Reason}")]
    private static partial void LogBootstrapRefused(ILogger logger, string reason, Exception exception);
}
