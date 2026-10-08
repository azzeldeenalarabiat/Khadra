using System.Security.Cryptography;
using System.Text;
using Khadra.Application.IdentityAccess.Login;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Security;

/// <summary>
/// Failed sign-ins per account name, in the database (pre-launch item 51), so the count holds across restarts and
/// across instances, and is the same count whichever address the attempts come from.
/// </summary>
/// <remarks>
/// Every write is its own statement, outside the sign-in's unit of work: a failure has to be counted although the
/// sign-in that failed saves nothing, and the count must not wait for, or be rolled back with, anything else.
/// </remarks>
internal sealed class SignInThrottle(KhadraDbContext context, IOptions<SignInThrottleOptions> options) : ISignInThrottle
{
    // One statement, so two failures arriving together are counted as two. Inside the window the count goes up; once
    // the window has passed it starts again at one; reaching the ceiling sets the block. Every SET expression reads
    // the row as it was before this statement, on PostgreSQL and SQLite alike.
    private const string RecordFailureSql = """
        INSERT INTO sign_in_throttles (subject_hash, window_started_at, failures, blocked_until)
        VALUES ({0}, {1}, 1, CASE WHEN 1 >= {3} THEN {4} ELSE NULL END)
        ON CONFLICT (subject_hash) DO UPDATE SET
            failures = CASE WHEN sign_in_throttles.window_started_at <= {2} THEN 1 ELSE sign_in_throttles.failures + 1 END,
            window_started_at = CASE WHEN sign_in_throttles.window_started_at <= {2} THEN {1} ELSE sign_in_throttles.window_started_at END,
            blocked_until = CASE
                WHEN (CASE WHEN sign_in_throttles.window_started_at <= {2} THEN 1 ELSE sign_in_throttles.failures + 1 END) >= {3}
                    THEN {4}
                ELSE sign_in_throttles.blocked_until
            END
        """;

    private SignInThrottleOptions Policy => options.Value;

    public async Task<TimeSpan?> BlockedForAsync(EmailAddress subject, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var hash = Hash(subject);

        var blockedUntil = await context.SignInThrottles
            .AsNoTracking()
            .Where(row => row.SubjectHash == hash)
            .Select(row => row.BlockedUntil)
            .FirstOrDefaultAsync(cancellationToken);

        return blockedUntil is { } until && until > now ? until - now : null;
    }

    public async Task RecordFailureAsync(EmailAddress subject, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var windowStart = now.AddMinutes(-Policy.WindowMinutes);

        await context.Database.ExecuteSqlRawAsync(
            RecordFailureSql,
            [Hash(subject), Instant(now), Instant(windowStart), Policy.MaxFailures, Instant(now.AddMinutes(Policy.BlockMinutes))],
            cancellationToken);

        // The sweep: rows whose window and block are both over say nothing any more. Run on the failure path, which is
        // also the only path that adds rows, so the table never outgrows the attempts of the last window.
        await context.SignInThrottles
            .Where(row => row.WindowStartedAt <= windowStart && (row.BlockedUntil == null || row.BlockedUntil <= now))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task ResetAsync(EmailAddress subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var hash = Hash(subject);

        await context.SignInThrottles
            .Where(row => row.SubjectHash == hash)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>A purpose-prefixed SHA-256 of the normalised address, as 64 lower-case hex characters.</summary>
    internal static string Hash(EmailAddress subject) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("khadra-sign-in:" + subject.Value)));

    /// <summary>An instant as EF stores it on this provider: timestamptz in UTC on PostgreSQL, UTC ticks on SQLite.</summary>
    private object Instant(DateTimeOffset value) =>
        string.Equals(context.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal)
            ? value.UtcTicks
            : value.ToUniversalTime();
}
