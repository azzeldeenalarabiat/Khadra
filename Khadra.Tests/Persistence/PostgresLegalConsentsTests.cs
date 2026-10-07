using Khadra.Domain.Common;
using Khadra.Domain.Legal;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The consents' migration on a real PostgreSQL (Wave 4, W4-8), opt-in like every scratch-database proof: the
/// constraints under the names the code relies on, the append-only triggers, the one statement of "pending" as
/// PostgreSQL runs it, and a rollback that refuses once anybody has consented.
/// </summary>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresLegalConsentsTests
{
    private const string Previous = "20261007001707_BookingDisputeWindowEnd";

    private static LegalDocumentVersion Version(LegalDocumentKind kind, string label, DateTimeOffset at) =>
        LegalDocumentVersion.Publish(kind, label, "# Text\n", "# نص\n", Id.New(), at, null).Value;

    private static async Task SaveAsync(Scratch database, params object[] rows)
    {
        await using var context = new KhadraDbContext(database.Options);
        foreach (var row in rows)
        {
            if (row is LegalDocumentVersion version)
                new LegalDocumentVersionRepository(context).Add(version);
            else
                new LegalConsentRepository(context).Add((LegalConsent)row);
        }

        await context.SaveChangesAsync();
    }

    [PostgresFact]
    public async Task The_table_its_names_and_its_triggers_are_as_the_code_expects()
    {
        var database = await FreshDatabaseAsync("consents");
        var terms = Version(LegalDocumentKind.Terms, "2026-10", Build.Now);
        var userId = Id.New();
        var consent = LegalConsent.Accept(userId, terms.Id, ConsentChannel.Website, Language.Arabic, Build.Now);
        await SaveAsync(database, terms, consent);

        await using var connection = await OpenAsync(database);

        // Append-only, whoever asks: the application's guard and the database's triggers both refuse.
        await RefusedAsync(connection, $"UPDATE legal_consents SET language = 'en' WHERE id = '{consent.Id.Value}'", "is append-only");
        await RefusedAsync(connection, $"DELETE FROM legal_consents WHERE id = '{consent.Id.Value}'", "is append-only");
        await RefusedAsync(connection, "TRUNCATE legal_consents", "is append-only");

        // Only what a build knows, and only a version that exists.
        var insert = $"INSERT INTO legal_consents (id, user_id, document_version_id, occurred_at, channel, language, action) " +
                     $"VALUES (gen_random_uuid(), '{userId.Value}', '{terms.Id.Value}', now(), ";
        await CheckRefusedAsync(connection, "ck_legal_consents_channel", insert + "'Fax', 'en', 'Accepted')");
        await CheckRefusedAsync(connection, "ck_legal_consents_language", insert + "'Website', 'fr', 'Accepted')");
        await CheckRefusedAsync(connection, "ck_legal_consents_action", insert + "'Website', 'en', 'Withdrawn')");
        await using (var orphan = new NpgsqlCommand(
            "INSERT INTO legal_consents (id, user_id, document_version_id, occurred_at, channel, language, action) " +
            $"VALUES (gen_random_uuid(), '{userId.Value}', gen_random_uuid(), now(), 'Website', 'en', 'Accepted')", connection))
        {
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => orphan.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, refusal.SqlState);
        }

        var indexes = await NamesAsync(connection, "SELECT indexname FROM pg_indexes WHERE tablename = 'legal_consents' ORDER BY indexname");
        Assert.Contains("ix_legal_consents_user_id_document_version_id", indexes);
        Assert.Contains("ix_legal_consents_user_id_occurred_at", indexes);
    }

    /// <summary>The gate asks this on every request it judges: it must translate, and answer the same as on SQLite.</summary>
    [PostgresFact]
    public async Task Pending_and_the_record_read_the_same_on_postgres()
    {
        var database = await FreshDatabaseAsync("pending");
        var userId = Id.New();
        var september = Version(LegalDocumentKind.Terms, "2026-09", Build.Now.AddDays(-30));
        var october = Version(LegalDocumentKind.Terms, "2026-10", Build.Now.AddDays(-1));
        var privacy = Version(LegalDocumentKind.Privacy, "1.0", Build.Now.AddDays(-2));
        await SaveAsync(
            database,
            september,
            october,
            privacy,
            LegalConsent.Accept(userId, september.Id, ConsentChannel.Website, Language.English, Build.Now.AddDays(-29)),
            LegalConsent.Accept(userId, privacy.Id, ConsentChannel.Console, Language.Arabic, Build.Now.AddHours(-1)));

        await using var context = new KhadraDbContext(database.Options);
        var reader = new LegalConsentReader(context);
        var pending = await reader.PendingAsync(userId, Build.Now);
        var record = await reader.AcceptedAsync(userId);
        var already = await reader.AlreadyAcceptedAsync(userId, [september.Id, october.Id]);

        Assert.Equal(october.Id, Assert.Single(pending).VersionId);
        Assert.Equal(["1.0", "2026-09"], record.Select(row => row.VersionLabel));
        Assert.Equal([september.Id], already);
    }

    [PostgresFact]
    public async Task The_rollback_runs_while_nobody_has_consented_and_refuses_once_anyone_has()
    {
        var empty = await FreshDatabaseAsync("consentsempty");
        await using (var context = new KhadraDbContext(empty.Options))
            await context.GetService<IMigrator>().MigrateAsync(Previous);
        await using (var connection = await OpenAsync(empty))
        await using (var gone = new NpgsqlCommand("SELECT to_regclass('legal_consents') IS NULL", connection))
            Assert.True((bool)(await gone.ExecuteScalarAsync())!);

        var consented = await FreshDatabaseAsync("consentsgiven");
        var terms = Version(LegalDocumentKind.Terms, "2026-10", Build.Now);
        await SaveAsync(consented, terms, LegalConsent.Accept(Id.New(), terms.Id, ConsentChannel.Website, Language.English, Build.Now));

        await using (var context = new KhadraDbContext(consented.Options))
        {
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync(Previous));
            Assert.Contains("fixed forward", refusal.MessageText, StringComparison.Ordinal);
        }
    }

    // ── Helpers, as the other scratch-database proofs have them ──────────────────────────────────

    private static async Task RefusedAsync(NpgsqlConnection connection, string sql, string because)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Contains(because, refusal.MessageText, StringComparison.Ordinal);
    }

    private static async Task CheckRefusedAsync(NpgsqlConnection connection, string constraint, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal(constraint, refusal.ConstraintName);
    }

    private static async Task<List<string>> NamesAsync(NpgsqlConnection connection, string sql)
    {
        var names = new List<string>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
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

    private sealed record Scratch(DbContextOptions<KhadraDbContext> Options, string ConnectionString);

    /// <summary>A database nothing else has used, named after the configured scratch one, migrated to the latest.</summary>
    private static async Task<Scratch> FreshDatabaseAsync(string scenario)
    {
        var configured = PostgresTestDatabase.ConnectionString
            ?? throw new InvalidOperationException($"{PostgresTestDatabase.VariableName} is not set.");
        var fresh = new NpgsqlConnectionStringBuilder(configured);
        var name = $"{fresh.Database}_{scenario}_{Guid.NewGuid():N}";
        fresh.Database = name[..Math.Min(63, $"{fresh.Database}_{scenario}_".Length + 8)];

        var maintenance = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" };
        await using (var connection = new NpgsqlConnection(maintenance.ConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand(
                $"CREATE DATABASE \"{fresh.Database!.Replace("\"", "\"\"", StringComparison.Ordinal)}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseNpgsql(fresh.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new KhadraDbContext(options);
        await context.Database.MigrateAsync();
        return new Scratch(options, fresh.ConnectionString);
    }
}
