using Khadra.Application.Common.Ports;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Payments;

/// <summary>
/// The payments guard's one question — has this database ever handled the other kind of money? — asked at boot by
/// <c>PaymentsStartupCheck</c> and again by <c>DeferredStartupService</c> until it can be answered (pre-launch
/// item 221). One copy of the query and of its refusals, so the two askers cannot drift apart.
/// </summary>
/// <remarks>
/// <c>Payment.Provider</c> is the durable marker, and the only one: written when the attempt is opened, never
/// changed, carried into every receipt and refund. So the data answers on its own, with no flag anybody has to
/// remember to set. An EMPTY table passes either way, and has to: a developer's database is empty, and so is a real
/// one the day before launch — which is why the environment guard in <c>Program.cs</c> exists as well.
/// </remarks>
public static class PaymentDatabaseCheck
{
    /// <summary>
    /// What a SANDBOX process says once the guard has read its database cleanly — at boot, or later when the database
    /// did not answer then — so the warning is printed by whichever asker opened payments, and only once.
    /// </summary>
    public const string SandboxEnabled =
        "SANDBOX PAYMENTS ARE ENABLED. No money moves, and every payment row is stamped " +
        "SANDBOX. This database holds no payments from any other provider, which is the " +
        "only condition under which this is allowed to run.";

    /// <summary>
    /// Whether this provider can confirm a booking: the one configuration chose, not the wrapper holding it while
    /// the question is unanswered.
    /// </summary>
    public static bool CanConfirmBookings(IPaymentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider is VerifiedPaymentProvider held ? held.Inner.IsConfigured : provider.IsConfigured;
    }

    public static async Task<PaymentDatabaseVerdict> RunAsync(
        KhadraDbContext context,
        IPaymentProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(provider);
        var sandbox = PaymentProviders.IsSandbox(provider.Name);

        List<string> offending;
        try
        {
            // Whichever kind this process is NOT. Five names is enough to diagnose it; the count is not the point,
            // the existence is. The first five in order, so a refusal names the same five on every boot — a limit
            // with no order is whichever five the database meets first.
            offending = await context.Payments
                .AsNoTracking()
                .Where(payment => sandbox
                    ? payment.Provider != PaymentProviders.Sandbox
                    : payment.Provider == PaymentProviders.Sandbox)
                .Select(payment => payment.Provider)
                .Distinct()
                .OrderBy(name => name)
                .Take(5)
                .ToListAsync(cancellationToken);
        }
        catch (Exception unreadable) when (DatabaseUnreadable.IsCauseOf(unreadable))
        {
            return new PaymentDatabaseVerdict.Unreadable(unreadable);
        }

        return offending.Count == 0
            ? PaymentDatabaseVerdict.Clean.Instance
            : new PaymentDatabaseVerdict.Mismatched(offending, Refusal(sandbox, provider.Name, offending));
    }

    private static string Refusal(bool sandbox, string providerName, IReadOnlyList<string> offending) => sandbox
        ? $"Payments:Provider is '{PaymentProviders.Sandbox}', but this database already holds "
          + $"payments from: {string.Join(", ", offending)}. A database that has handled a real "
          + "provider is never served by one that confirms bookings without money — a sandbox "
          + "capture would be indistinguishable from those rows to everyone reading them "
          + "afterwards. This most likely means a test or staging process is pointed at the "
          + "wrong connection string. Set Payments__Provider to 'None', or point this process "
          + "at a database that has never taken a payment."
        : $"Payments:Provider is '{providerName}', but this database holds "
          + $"{PaymentProviders.Sandbox} payments — bookings confirmed with no money behind "
          + "them. Serving it as anything else would leave those rows looking exactly like paid "
          + "ones, on every screen and in every export, with nothing to tell them apart. A "
          + "database used for sandbox payments stays on the sandbox for good. Set "
          + $"Payments__Provider to '{PaymentProviders.Sandbox}', or point this process at a "
          + "database that has never taken a sandbox payment.";
}

/// <summary>What <see cref="PaymentDatabaseCheck"/> found.</summary>
public abstract record PaymentDatabaseVerdict
{
    private PaymentDatabaseVerdict()
    {
    }

    /// <summary>Only this process's kind of money, or none at all.</summary>
    public sealed record Clean : PaymentDatabaseVerdict
    {
        public static readonly Clean Instance = new();
    }

    /// <summary>The other kind of money: refused, always.</summary>
    public sealed record Mismatched(IReadOnlyList<string> Offending, string Message) : PaymentDatabaseVerdict;

    /// <summary>The question could not be asked: the database refused, did not answer, or has no such table.</summary>
    public sealed record Unreadable(Exception Cause) : PaymentDatabaseVerdict;
}
