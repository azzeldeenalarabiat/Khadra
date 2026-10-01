using System.Diagnostics;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Payments;
using Khadra.Infrastructure.Persistence;

namespace Khadra.WebAPI;

/// <summary>
/// Says, on every start, whether the platform can take a deposit — and refuses to start at all if this database's
/// payments and this process's provider are different kinds of money.
/// </summary>
/// <remarks>
/// <para>
/// The twin of <see cref="MailStartupCheck"/> for the reporting half: a platform that silently cannot take payments
/// looks exactly like one that can, right up to the first customer who tries, and the line at boot is what stops
/// that being a surprise. That half is never fatal.
/// </para>
/// <para>
/// <b>The GUARD half is fatal, and it enforces one rule in both directions:</b> sandbox payments and real payments
/// never share a database, and a database holding sandbox payments is served only by the sandbox. Stated that way
/// rather than as "the sandbox must not see real rows", because the other direction is the one that bites on launch
/// day — sandbox-confirmed bookings, reading <c>Confirmed</c> with a deposit marked paid and a gallery already
/// notified, still sitting there when the real adapter is switched on. Nobody could then tell which rentals had money
/// behind them, which is the exact confusion the owner said must never be possible.
/// </para>
/// <para>
/// It is deliberately a different question from the one <c>Program.cs</c> asks. That one asks the ENVIRONMENT — is
/// this Production — which is a property of the process and travels with a variable somebody can forget, copy or
/// override. This one asks the DATA, which does not move when a connection string does. It is what catches a staging
/// API pointed at the production database, and what catches a production API pointed at a database somebody once
/// played with, and it is modelled on <c>AdminBootstrapper</c>'s "only on a database that has never held an
/// administrator" — a shape this project already trusts. The question itself is <see cref="PaymentDatabaseCheck"/>,
/// shared with the background service that asks it again.
/// </para>
/// <para>
/// <b>A database that does not answer no longer stops the API</b> (pre-launch item 221; owner, 2026-10-01). A
/// violation FOUND is still always fatal. But when the table cannot be read at all — the database refused the
/// connection, timed out, or has no such table — a provider that can confirm a booking does not get to assume the
/// answer it wants, and it does not crash the platform either: the API starts with every payment operation HELD
/// (<see cref="PaymentVerification"/>, <see cref="VerifiedPaymentProvider"/>), and <c>DeferredStartupService</c> asks
/// again until the database answers — opening payments on a clean read, stopping the host on a mismatch, before any
/// payment could be taken. A platform with no provider opens no attempts and simply starts, as before.
/// </para>
/// <para>
/// <b>What is still open</b> is two processes starting at once against one empty database, one sandbox and one
/// real: both pass, and afterwards both write. Only the database itself can close that, with an insert trigger on
/// <c>payments</c> in the shape of the one <c>audit_entries</c> already has. It needs a real adapter to be reachable
/// at all, so it is written when that adapter is — pre-launch item 123.
/// </para>
/// </remarks>
internal static partial class PaymentsStartupCheck
{
    public static async Task ReportAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var provider = services.GetRequiredService<IPaymentProvider>();
        var detail = await services.GetRequiredService<IPaymentProviderProbe>().DescribeAsync(cancellationToken);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Khadra.Payments");
        var canConfirm = PaymentDatabaseCheck.CanConfirmBookings(provider);

        PaymentDatabaseVerdict verdict;
        using (var scope = services.CreateScope())
        {
            verdict = await PaymentDatabaseCheck.RunAsync(
                scope.ServiceProvider.GetRequiredService<KhadraDbContext>(), provider, cancellationToken);
        }

        switch (verdict)
        {
            case PaymentDatabaseVerdict.Mismatched mismatched:
                services.GetService<PaymentVerification>()?.MarkRefused();
                throw new InvalidOperationException(mismatched.Message);

            case PaymentDatabaseVerdict.Unreadable unreadable when canConfirm:
                // Held, not refused: the probe's own sentence says "both checked", which would be false here.
                LogPaymentsHeld(logger, provider.Name, DatabaseUnreadable.Reason(unreadable.Cause));
                return;

            case PaymentDatabaseVerdict.Unreadable unreadable:
                // Nothing this process can do moves money, so the platform starts as it always did — and the question
                // is still asked, in the background: a database holding sandbox money must refuse this process too.
                LogPaymentsTableUnreadable(logger, DatabaseUnreadable.Reason(unreadable.Cause));
                LogNotReady(logger, detail);
                return;

            case PaymentDatabaseVerdict.Clean:
                services.GetService<PaymentVerification>()?.MarkVerified();
                if (PaymentProviders.IsSandbox(provider.Name))
                    LogSandboxDatabaseClean(logger);
                break;

            default:
                // A closed set: a verdict nobody has decided must never open payments by falling through.
                throw new UnreachableException($"The payments guard returned an unknown verdict, {verdict.GetType().Name}.");
        }

        if (canConfirm)
            LogReady(logger, detail);
        else
            LogNotReady(logger, detail);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = PaymentDatabaseCheck.SandboxEnabled)]
    private static partial void LogSandboxDatabaseClean(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Could not read the payments table to check what kind of money this database has " +
                  "handled. Starting anyway, because no provider is configured and nothing this " +
                  "process does can move money. It asks again in the background, and stops if the " +
                  "database holds sandbox payments. {Reason}")]
    private static partial void LogPaymentsTableUnreadable(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "PAYMENTS ARE HELD. Payments:Provider is '{Provider}', which can complete a checkout, and this " +
                  "process could not read the payments table to check that this database has only ever handled the " +
                  "same kind of money. Nothing is accepted — no checkout, no provider event, no refund sent — until " +
                  "it can: it asks again in the background, accepts payments once the answer is clean, and stops if " +
                  "it is the other kind of money. Everything else is served meanwhile. {Reason}")]
    private static partial void LogPaymentsHeld(ILogger logger, string provider, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Payments ready. {Detail}")]
    private static partial void LogReady(ILogger logger, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PAYMENTS ARE NOT ACCEPTED. {Detail}")]
    private static partial void LogNotReady(ILogger logger, string detail);
}
