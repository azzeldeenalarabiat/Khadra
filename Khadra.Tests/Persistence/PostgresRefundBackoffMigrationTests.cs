using Khadra.Infrastructure.Persistence;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The one row-writing step of the Wave 4 payments migration, on the engine that runs it (the advisor's D8 review).
/// </summary>
/// <remarks>
/// A refund that already reads Failed when the migration runs has been refused at least once, so it starts at a
/// refusal count of ONE — a floor, since the old sweep re-sent it every minute and the true count is unknown — and is
/// due on the first tick. Every other refund starts at zero. Run on a fresh database at the previous migration, with
/// the rows written as that schema holds them; opt-in like every proof here (<c>KHADRA_TEST_POSTGRES</c>).
/// </remarks>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresRefundBackoffMigrationTests
{
    private const string Previous = "20261005062940_LegalDocumentVersions";
    private const string ThisMigration = "20261006235151_PaymentCaptureIncidentsAndRefundBackoff";

    private static readonly DateTimeOffset Now = Build.Now;

    [PostgresFact]
    public async Task A_refund_already_refused_starts_at_one_refusal_and_is_due_at_once()
    {
        var database = await FreshDatabaseAsync("refusals");
        await using (var context = new KhadraDbContext(database.Options))
            await context.GetService<IMigrator>().MigrateAsync(Previous);

        var paymentId = Guid.NewGuid();
        var refused = Guid.NewGuid();
        var recorded = Guid.NewGuid();
        var settled = Guid.NewGuid();
        await using (var connection = await OpenAsync(database.ConnectionString))
        {
            await ExecuteAsync(connection, @"
INSERT INTO payments (id, booking_id, customer_id, amount, currency, purpose, processing_fee, fee_refundable, status,
                      provider, provider_reference, expires_at, created_at, captured_at, amount_captured,
                      captured_currency, orphaned_at, orphan_reason)
VALUES (@id, gen_random_uuid(), gen_random_uuid(), 54, 'JOD', 'Deposit', 0, true, 'Orphaned',
        'TestProvider', 'sess_migration', @now, @now, @now, 54, 'JOD', @now, 'BookingExpired')",
                ("id", paymentId), ("now", Now));
            foreach (var (id, status) in new[] { (refused, "Failed"), (recorded, "Requested"), (settled, "Settled") })
            {
                await ExecuteAsync(connection, @"
INSERT INTO payment_refunds (id, payment_id, amount, currency, reason, status, requested_at, booking_part, fee_part,
                             failed_at, failure_code)
VALUES (@id, @payment, 18, 'JOD', 'OrphanedCapture', @status, @now, 18, 0,
        CASE WHEN @status = 'Failed' THEN @now END, CASE WHEN @status = 'Failed' THEN 'card_closed' END)",
                    ("id", id), ("payment", paymentId), ("status", status), ("now", Now));
            }
        }

        await using (var context = new KhadraDbContext(database.Options))
            await context.GetService<IMigrator>().MigrateAsync(ThisMigration);

        await using var read = await OpenAsync(database.ConnectionString);
        await using var query = new NpgsqlCommand(
            "SELECT id, refusal_count, next_attempt_at FROM payment_refunds WHERE payment_id = @payment", read);
        query.Parameters.AddWithValue("payment", paymentId);
        var rows = new Dictionary<Guid, (int Refusals, bool Scheduled)>();
        await using (var reader = await query.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                rows[reader.GetGuid(0)] = (reader.GetInt32(1), !reader.IsDBNull(2));
        }

        Assert.Equal((1, false), rows[refused]);
        Assert.Equal((0, false), rows[recorded]);
        Assert.Equal((0, false), rows[settled]);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private sealed record Scratch(DbContextOptions<KhadraDbContext> Options, string ConnectionString);

    /// <summary>A database nothing else has used, named after the configured scratch one. It is never dropped.</summary>
    private static async Task<Scratch> FreshDatabaseAsync(string scenario)
    {
        var configured = PostgresTestDatabase.ConnectionString
            ?? throw new InvalidOperationException($"{PostgresTestDatabase.VariableName} is not set.");
        var fresh = new NpgsqlConnectionStringBuilder(configured);
        fresh.Database = $"{fresh.Database}_{scenario}_{Guid.NewGuid():N}"[..Math.Min(63, $"{fresh.Database}_{scenario}_".Length + 8)];

        var maintenance = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" };
        await using (var connection = new NpgsqlConnection(maintenance.ConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand(
                $"CREATE DATABASE \"{fresh.Database!.Replace("\"", "\"\"", StringComparison.Ordinal)}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        return new Scratch(
            new DbContextOptionsBuilder<KhadraDbContext>()
                .UseNpgsql(fresh.ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options,
            fresh.ConnectionString);
    }
}
