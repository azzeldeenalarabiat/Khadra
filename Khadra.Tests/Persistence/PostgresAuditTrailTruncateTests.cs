using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Persistence;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Pre-launch item 1 (owner, 2026-10-08): the audit trail refuses TRUNCATE as well as UPDATE and DELETE, and the
/// migration that adds the guard is safe on a database that already holds a trail — it alters and rewrites no entry.
/// </summary>
/// <remarks>
/// Each scenario gets a FRESH scratch database named after the one <c>KHADRA_TEST_POSTGRES</c> names, because each
/// needs the schema at a particular migration. They are left behind, never dropped, as in
/// <c>PostgresFinancialDocumentsMigrationTests</c>.
/// </remarks>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresAuditTrailTruncateTests
{
    private const string Previous = "20261008110814_SignInThrottle";
    private const string ThisMigration = "20261008111400_AuditTrailRefusesTruncate";

    private static readonly DateTimeOffset Now = Build.Now;

    // Every column of every row, in one deterministic string: what "nothing was rewritten" is checked against.
    private const string Fingerprint = """
        SELECT coalesce(string_agg(to_jsonb(entry)::text, '|' ORDER BY entry.id), '')
        FROM audit_entries entry
        """;

    [PostgresFact]
    public async Task Applying_it_to_a_database_with_a_trail_keeps_every_entry_exactly_as_it_was()
    {
        var database = await FreshDatabaseAsync("auditkeep");
        await using (var context = new KhadraDbContext(database.Options))
            await MigrateToAsync(context, Previous);

        // Written as the API before this migration wrote them: in SQL, because today's model has columns a database at
        // the previous migration does not.
        await using (var connection = await OpenAsync(database))
        {
            await ExecuteAsync(connection, $"""
                INSERT INTO audit_entries
                    (id, occurred_at, actor_user_id, actor_name, actor_role, action, entity_type, entity_id, subject_label,
                     previous_value, new_value, reason, correlation_id)
                VALUES
                    ('{Guid.NewGuid()}', '{Now.AddDays(-3):O}', '{Guid.NewGuid()}', 'Rania Haddad', 'Admin', 'DealerApproved',
                     'Dealer', '{Guid.NewGuid()}', 'Aqaba Coast Cars', NULL, NULL, 'Licence verified', NULL),
                    ('{Guid.NewGuid()}', '{Now.AddDays(-2):O}', '{Guid.NewGuid()}', 'رانيا حداد', 'Admin', 'CustomerSuspended',
                     'Customer', '{Guid.NewGuid()}', 'KH-2026-000123', 'Active', 'Suspended', NULL, NULL),
                    ('{Guid.NewGuid()}', '{Now.AddDays(-1):O}', NULL, 'System', NULL, 'AdminInvited',
                     'AdminUser', '{Guid.NewGuid()}', 'admin@example.com', NULL, NULL, NULL, NULL)
                """);
        }

        string before;
        await using (var connection = await OpenAsync(database))
            before = await ScalarAsync<string>(connection, Fingerprint);

        await using (var context = new KhadraDbContext(database.Options))
            await MigrateToAsync(context, ThisMigration);

        await using (var check = await OpenAsync(database))
        {
            Assert.Equal(before, await ScalarAsync<string>(check, Fingerprint));
            Assert.Equal(3L, await ScalarAsync<long>(check, "SELECT count(*) FROM audit_entries"));
            Assert.Equal(
                ["audit_entries_append_only", "audit_entries_no_truncate"],
                await NamesAsync(check, "SELECT tgname FROM pg_trigger WHERE NOT tgisinternal AND tgrelid = 'audit_entries'::regclass ORDER BY tgname"));
        }

        // And every later migration on the table — Wave 6's Arabic subject column (item 176) — adds without rewriting:
        // each entry's every original column is as it was, and the new one is null.
        await using (var context = new KhadraDbContext(database.Options))
            await context.Database.MigrateAsync();

        await using var latest = await OpenAsync(database);
        Assert.Equal(before, await ScalarAsync<string>(latest, FingerprintWithout("subject_label_ar")));
        Assert.Equal(0L, await ScalarAsync<long>(latest, "SELECT count(*) FROM audit_entries WHERE subject_label_ar IS NOT NULL"));
    }

    [PostgresFact]
    public async Task The_trail_refuses_truncate_update_and_delete_and_still_takes_new_entries()
    {
        var database = await FreshDatabaseAsync("audittrunc");
        await using (var context = new KhadraDbContext(database.Options))
        {
            // Every migration, this one and the later ones: the guard must hold on the schema the API runs on.
            await context.Database.MigrateAsync();
            context.AuditEntries.Add(AuditEntry.BySystem(AuditAction.AdminInvited, AuditEntityType.AdminUser, Id.New(), "admin@example.com", Now));
            await context.SaveChangesAsync();
        }

        await using (var connection = await OpenAsync(database))
        {
            await RefusedAsync(connection, "TRUNCATE audit_entries", "TRUNCATE");
            // A CASCADE or a list naming it is refused by the same statement trigger.
            await RefusedAsync(connection, "TRUNCATE audit_entries CASCADE", "TRUNCATE");
            await RefusedAsync(connection, "UPDATE audit_entries SET reason = 'rewritten'", "UPDATE");
            await RefusedAsync(connection, "DELETE FROM audit_entries", "DELETE");
            Assert.Equal(1L, await ScalarAsync<long>(connection, "SELECT count(*) FROM audit_entries"));
        }

        // Appending is the one thing the trail is for, and it still works.
        await using (var context = new KhadraDbContext(database.Options))
        {
            context.AuditEntries.Add(AuditEntry.BySystem(AuditAction.AdminInvited, AuditEntityType.AdminUser, Id.New(), "second@example.com", Now));
            await context.SaveChangesAsync();
        }
        await using var check = await OpenAsync(database);
        Assert.Equal(2L, await ScalarAsync<long>(check, "SELECT count(*) FROM audit_entries"));
    }

    /// <summary>The rollback removes only the statement guard; the row guard from 2026-09-03 stays.</summary>
    [PostgresFact]
    public async Task Rolling_back_removes_only_the_truncate_guard()
    {
        var database = await FreshDatabaseAsync("auditdown");
        await using (var context = new KhadraDbContext(database.Options))
        {
            await MigrateToAsync(context, ThisMigration);
            await MigrateToAsync(context, Previous);
        }

        await using var check = await OpenAsync(database);
        Assert.Equal(
            ["audit_entries_append_only"],
            await NamesAsync(check, "SELECT tgname FROM pg_trigger WHERE NOT tgisinternal AND tgrelid = 'audit_entries'::regclass"));
    }

    private sealed record Scratch(DbContextOptions<KhadraDbContext> Options, string ConnectionString);

    // The same fingerprint over every column but the ones a later migration added, which read null on an old row.
    private static string FingerprintWithout(string column) => $"""
        SELECT coalesce(string_agg((to_jsonb(entry) - '{column}')::text, '|' ORDER BY entry.id), '')
        FROM audit_entries entry
        """;

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static Task MigrateToAsync(KhadraDbContext context, string target) =>
        context.GetService<IMigrator>().MigrateAsync(target);

    private static async Task RefusedAsync(NpgsqlConnection connection, string sql, string operation)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("P0001", refusal.SqlState);
        Assert.Equal($"audit_entries is append-only: {operation} is not permitted", refusal.MessageText);
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<string>> NamesAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
    }

    private static async Task<NpgsqlConnection> OpenAsync(Scratch database)
    {
        var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

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
