using System.Data.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.WebAPI;

/// <summary>
/// Says, on every start, whether the platform can take a deposit — and refuses to start at all if
/// this database's payments and this process's provider are different kinds of money.
/// </summary>
/// <remarks>
/// <para>
/// The twin of <see cref="MailStartupCheck"/> for the reporting half: a platform that silently
/// cannot take payments looks exactly like one that can, right up to the first customer who tries,
/// and the line at boot is what stops that being a surprise. That half is never fatal.
/// </para>
/// <para>
/// <b>The GUARD half is fatal, and it enforces one rule in both directions:</b> sandbox payments and
/// real payments never share a database, and a database holding sandbox payments is served only by
/// the sandbox. Stated that way rather than as "the sandbox must not see real rows", because the
/// other direction is the one that bites on launch day — sandbox-confirmed bookings, reading
/// <c>Confirmed</c> with a deposit marked paid and a gallery already notified, still sitting there
/// when the real adapter is switched on. Nobody could then tell which rentals had money behind them,
/// which is the exact confusion the owner said must never be possible.
/// </para>
/// <para>
/// It is deliberately a different question from the one <c>Program.cs</c> asks. That one asks the
/// ENVIRONMENT — is this Production — which is a property of the process and travels with a variable
/// somebody can forget, copy or override. This one asks the DATA, which does not move when a
/// connection string does. It is what catches a staging API pointed at the production database, and
/// what catches a production API pointed at a database somebody once played with, and it is modelled
/// on <c>AdminBootstrapper</c>'s "only on a database that has never held an administrator" — a shape
/// this project already trusts.
/// </para>
/// <para>
/// <b>What is still open</b> is two processes starting at once against one empty database, one
/// sandbox and one real: both pass, and afterwards both write. Only the database itself can close
/// that, with an insert trigger on <c>payments</c> in the shape of the one <c>audit_entries</c>
/// already has. It needs a real adapter to be reachable at all, so it is written when that adapter is
/// — pre-launch item 123.
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

        await RefuseAMismatchedDatabaseAsync(services, provider, logger, cancellationToken);

        if (provider.IsConfigured)
            LogReady(logger, detail);
        else
            LogNotReady(logger, detail);
    }

    /// <summary>
    /// Refuses to start when this database's payments are a different kind of money from this
    /// process's provider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Payment.Provider</c> is the durable marker, and the only one: it is written when the
    /// attempt is opened, never changes, and is carried into every receipt and refund. So "what kind
    /// of money has this database handled" is a question the data answers on its own, with no flag
    /// anybody has to remember to set and no second marker that could disagree with the first.
    /// </para>
    /// <para>
    /// <b>An EMPTY payments table passes either way, and has to.</b> A developer's database is empty,
    /// and so is a real one the day before launch. That is precisely why the environment guard in
    /// <c>Program.cs</c> exists as well: neither guard is sufficient alone, and this one is blind on
    /// exactly the day the other one matters most.
    /// </para>
    /// <para>
    /// <b>Severity of a FAILURE TO ASK follows <see cref="IPaymentProvider.IsConfigured"/>, while a
    /// violation FOUND is always fatal.</b> A provider that can confirm a booking does not get to
    /// assume the answer it wants, so it refuses when it cannot read the table. A platform with no
    /// provider opens no attempts and can create no confusion, so an unreadable table is left to the
    /// request that needs it — every smoke test in the suite boots that way against a database nobody
    /// has, and a database blip must not stop the platform serving the other ninety-nine per cent of
    /// the product.
    /// </para>
    /// </remarks>
    private static async Task RefuseAMismatchedDatabaseAsync(
        IServiceProvider services,
        IPaymentProvider provider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var sandbox = PaymentProviders.IsSandbox(provider.Name);

        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<KhadraDbContext>();

        List<string> offending;
        try
        {
            // Whichever kind this process is NOT. Five names is enough to diagnose it; the count is
            // not the point, the existence is.
            offending = await context.Payments
                .AsNoTracking()
                .Where(payment => sandbox
                    ? payment.Provider != PaymentProviders.Sandbox
                    : payment.Provider == PaymentProviders.Sandbox)
                .Select(payment => payment.Provider)
                .Distinct()
                .Take(5)
                .ToListAsync(cancellationToken);
        }
        catch (DbException unreadable)
        {
            if (!provider.IsConfigured)
            {
                // Nothing this process can do moves money, so the platform still starts. The first
                // request that needs the database will say so far more usefully than this would.
                LogPaymentsTableUnreadable(logger, unreadable.Message);
                return;
            }

            // Could not ask, and this process CAN confirm a booking. A guard about money does not get
            // to assume the answer it wants — but it says which question went unanswered, because
            // "relation payments does not exist" on its own sends somebody hunting the wrong bug.
            throw new InvalidOperationException(
                $"Payments:Provider is '{provider.Name}', which can complete a checkout, and this "
                + "process could not read the payments table to check that this database has only "
                + "ever handled the same kind of money. It does not run on a database it cannot "
                + "verify. Migrate the database, check the connection string, or set "
                + "Payments__Provider to 'None'.",
                unreadable);
        }

        if (offending.Count == 0)
        {
            if (sandbox) LogSandboxDatabaseClean(logger);
            return;
        }

        throw new InvalidOperationException(sandbox
            ? $"Payments:Provider is '{PaymentProviders.Sandbox}', but this database already holds "
              + $"payments from: {string.Join(", ", offending)}. A database that has handled a real "
              + "provider is never served by one that confirms bookings without money — a sandbox "
              + "capture would be indistinguishable from those rows to everyone reading them "
              + "afterwards. This most likely means a test or staging process is pointed at the "
              + "wrong connection string. Set Payments__Provider to 'None', or point this process "
              + "at a database that has never taken a payment."
            : $"Payments:Provider is '{provider.Name}', but this database holds "
              + $"{PaymentProviders.Sandbox} payments — bookings confirmed with no money behind "
              + "them. Serving it as anything else would leave those rows looking exactly like paid "
              + "ones, on every screen and in every export, with nothing to tell them apart. A "
              + "database used for sandbox payments stays on the sandbox for good. Set "
              + $"Payments__Provider to '{PaymentProviders.Sandbox}', or point this process at a "
              + "database that has never taken a sandbox payment.");
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "SANDBOX PAYMENTS ARE ENABLED. No money moves, and every payment row is stamped " +
                  "SANDBOX. This database holds no payments from any other provider, which is the " +
                  "only condition under which this is allowed to run.")]
    private static partial void LogSandboxDatabaseClean(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Could not read the payments table to check what kind of money this database has " +
                  "handled. Starting anyway, because no provider is configured and nothing this " +
                  "process does can move money. {Reason}")]
    private static partial void LogPaymentsTableUnreadable(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Payments ready. {Detail}")]
    private static partial void LogReady(ILogger logger, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PAYMENTS ARE NOT ACCEPTED. {Detail}")]
    private static partial void LogNotReady(ILogger logger, string detail);
}
