using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Security;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The per-account sign-in ceiling (pre-launch item 51) on the engine that runs it: the migration's table, the upsert
/// as PostgreSQL parses it, and the reason it is one statement — failures arriving together are each counted, so a
/// guesser cannot slip extra attempts past the ceiling by sending them at once.
/// </summary>
/// <remarks>Opt-in: set <c>KHADRA_TEST_POSTGRES</c> to a scratch database; it is reused, so each test uses its own name.</remarks>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresSignInThrottleTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = Build.Now;
    private DbContextOptions<KhadraDbContext>? _options;

    public async Task InitializeAsync()
    {
        var connectionString = PostgresTestDatabase.ConnectionString;
        if (connectionString is null) return;

        await EnsureDatabaseExistsAsync(connectionString);
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var context = new KhadraDbContext(_options);
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private KhadraDbContext NewContext() => new(_options
        ?? throw new InvalidOperationException($"{PostgresTestDatabase.VariableName} is not set."));

    private static SignInThrottle Throttle(KhadraDbContext context) =>
        new(context, Options.Create(new SignInThrottleOptions()));

    private static EmailAddress UniqueName() =>
        EmailAddress.Create($"{PostgresTestDatabase.Unique("throttle")}@example.com").Value;

    [PostgresFact]
    public async Task Failures_sent_together_are_each_counted_and_the_ceiling_still_blocks()
    {
        var subject = UniqueName();

        // Twelve at once, each on its own connection: a read-then-write would lose some of them to each other.
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var context = NewContext();
            await Throttle(context).RecordFailureAsync(subject, Now);
        }));

        await using var check = NewContext();
        var hash = SignInThrottle.Hash(subject);
        var row = await check.SignInThrottles.SingleAsync(entry => entry.SubjectHash == hash);
        Assert.Equal(12, row.Failures);
        Assert.Equal(Now.AddMinutes(15), row.BlockedUntil);
        Assert.Equal(TimeSpan.FromMinutes(15), await Throttle(check).BlockedForAsync(subject, Now));
    }

    [PostgresFact]
    public async Task A_window_restarts_and_a_reset_forgets_on_PostgreSQL_too()
    {
        var subject = UniqueName();
        var hash = SignInThrottle.Hash(subject);
        await using var context = NewContext();
        var throttle = Throttle(context);

        for (var attempt = 0; attempt < 7; attempt++)
            await throttle.RecordFailureAsync(subject, Now);
        await throttle.RecordFailureAsync(subject, Now.AddMinutes(15));

        var restarted = await context.SignInThrottles.AsNoTracking().SingleAsync(entry => entry.SubjectHash == hash);
        Assert.Equal(1, restarted.Failures);
        Assert.Null(restarted.BlockedUntil);

        await throttle.ResetAsync(subject);
        Assert.False(await context.SignInThrottles.AnyAsync(entry => entry.SubjectHash == hash));
    }

    [PostgresFact]
    public async Task The_table_refuses_a_row_with_no_failures()
    {
        await using var context = NewContext();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var insert = new NpgsqlCommand(
            "INSERT INTO sign_in_throttles (subject_hash, window_started_at, failures) VALUES (@hash, now(), 0)", connection);
        insert.Parameters.AddWithValue("hash", SignInThrottle.Hash(UniqueName()));

        var refusal = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_sign_in_throttles_failures", refusal.ConstraintName);
    }

    private static async Task EnsureDatabaseExistsAsync(string connectionString)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        var maintenance = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();
        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        exists.Parameters.AddWithValue("name", target.Database!);
        if (await exists.ExecuteScalarAsync() is not null) return;
        await using var create = new NpgsqlCommand(
            $"CREATE DATABASE \"{target.Database!.Replace("\"", "\"\"", StringComparison.Ordinal)}\"", connection);
        await create.ExecuteNonQueryAsync();
    }
}
