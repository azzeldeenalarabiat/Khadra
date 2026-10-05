using Khadra.Domain.Common;
using Khadra.Domain.Legal;
using Khadra.Domain.Legal.Repositories;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The legal texts' migration on a real PostgreSQL (Wave 2 G1), opt-in like every scratch-database proof: the
/// constraints and indexes under the names the code relies on, the append-only triggers, the hash recomputed by the
/// database itself, and a rollback that refuses once anything is published.
/// </summary>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresLegalDocumentsTests
{
    private const string Previous = "20260929195432_OfficePayables";

    private static LegalDocumentVersion Version(string label, DateTimeOffset at) =>
        LegalDocumentVersion.Publish(
            LegalDocumentKind.Terms,
            label,
            "# Terms\n\nWelcome to **Khadra**.\n",
            "# الشروط\n\nمرحبًا بك في **خضرا**.\n",
            Id.New(),
            at,
            null).Value;

    [PostgresFact]
    public async Task The_table_its_names_and_its_triggers_are_as_the_code_expects()
    {
        var database = await FreshDatabaseAsync("legal");
        var published = Version("2026-10", Build.Now);
        await using (var context = new KhadraDbContext(database.Options))
        {
            new LegalDocumentVersionRepository(context).Add(published);
            await IssuanceHarness.UnitOfWork(context).SaveChangesAsync();
        }

        await using var connection = await OpenAsync(database);
        Assert.Equal(
            [
                "ck_legal_document_versions_bodies",
                "ck_legal_document_versions_effective_from",
                "ck_legal_document_versions_hashes",
                "ck_legal_document_versions_kind",
                "ck_legal_document_versions_version_label",
            ],
            (await NamesAsync(connection, "SELECT conname FROM pg_constraint WHERE conrelid = 'legal_document_versions'::regclass AND contype = 'c'"))
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                "pk_legal_document_versions",
                ILegalDocumentVersionRepository.KindEffectiveFromIndex,
                ILegalDocumentVersionRepository.KindLabelIndex,
            ],
            (await NamesAsync(connection, "SELECT indexname FROM pg_indexes WHERE tablename = 'legal_document_versions'"))
                .Order(StringComparer.Ordinal));

        // The hash is what PostgreSQL itself computes over the stored text: what sha256sum computes over the file.
        await using (var hashes = new NpgsqlCommand(@"
SELECT encode(sha256(convert_to(body_en, 'UTF8')), 'hex') = body_en_sha256::text
   AND encode(sha256(convert_to(body_ar, 'UTF8')), 'hex') = body_ar_sha256::text
  FROM legal_document_versions", connection))
        {
            Assert.True((bool)(await hashes.ExecuteScalarAsync())!);
        }

        await RefusedAsync(connection, "UPDATE legal_document_versions SET body_en = 'Rewritten.'", "is append-only");
        await RefusedAsync(connection, "DELETE FROM legal_document_versions", "is append-only");
        await RefusedAsync(connection, "TRUNCATE legal_document_versions", "is append-only");
        await CheckRefusedAsync(connection, "ck_legal_document_versions_effective_from", @"
INSERT INTO legal_document_versions (id, kind, version_label, effective_from, published_at, published_by_admin_id,
                                     body_en, body_ar, body_en_sha256, body_ar_sha256)
VALUES (gen_random_uuid(), 'Privacy', '1', now() - interval '1 day', now(), gen_random_uuid(), 'a', 'b',
        repeat('0', 64), repeat('0', 64))");
        await CheckRefusedAsync(connection, "ck_legal_document_versions_kind", @"
INSERT INTO legal_document_versions (id, kind, version_label, effective_from, published_at, published_by_admin_id,
                                     body_en, body_ar, body_en_sha256, body_ar_sha256)
VALUES (gen_random_uuid(), 'Cookies', '1', now(), now(), gen_random_uuid(), 'a', 'b', repeat('0', 64), repeat('0', 64))");
        await CheckRefusedAsync(connection, "ck_legal_document_versions_version_label", @"
INSERT INTO legal_document_versions (id, kind, version_label, effective_from, published_at, published_by_admin_id,
                                     body_en, body_ar, body_en_sha256, body_ar_sha256)
VALUES (gen_random_uuid(), 'Privacy', ' padded ', now(), now(), gen_random_uuid(), 'a', 'b', repeat('0', 64), repeat('0', 64))");
    }

    /// <summary>The unit of work names the index a lost race hit, which the publish handler turns into its answer.</summary>
    [PostgresFact]
    public async Task A_second_version_under_one_label_is_refused_by_name()
    {
        var database = await FreshDatabaseAsync("legallabel");
        await using (var context = new KhadraDbContext(database.Options))
        {
            new LegalDocumentVersionRepository(context).Add(Version("2026-10", Build.Now));
            await IssuanceHarness.UnitOfWork(context).SaveChangesAsync();
        }

        await using (var context = new KhadraDbContext(database.Options))
        {
            new LegalDocumentVersionRepository(context).Add(Version("2026-10", Build.Now.AddDays(1)));
            var conflict = await Assert.ThrowsAsync<UniqueConstraintConflictException>(() => IssuanceHarness.UnitOfWork(context).SaveChangesAsync());
            Assert.Equal(ILegalDocumentVersionRepository.KindLabelIndex, conflict.ConstraintName);
        }

        await using (var context = new KhadraDbContext(database.Options))
        {
            new LegalDocumentVersionRepository(context).Add(Version("2026-11", Build.Now));
            var conflict = await Assert.ThrowsAsync<UniqueConstraintConflictException>(() => IssuanceHarness.UnitOfWork(context).SaveChangesAsync());
            Assert.Equal(ILegalDocumentVersionRepository.KindEffectiveFromIndex, conflict.ConstraintName);
        }
    }

    [PostgresFact]
    public async Task The_rollback_runs_while_nothing_is_published_and_refuses_once_anything_is()
    {
        var empty = await FreshDatabaseAsync("legalempty");
        await using (var context = new KhadraDbContext(empty.Options))
            await context.GetService<IMigrator>().MigrateAsync(Previous);
        await using (var connection = await OpenAsync(empty))
        await using (var gone = new NpgsqlCommand("SELECT to_regclass('legal_document_versions') IS NULL", connection))
            Assert.True((bool)(await gone.ExecuteScalarAsync())!);

        var published = await FreshDatabaseAsync("legalpublished");
        await using (var context = new KhadraDbContext(published.Options))
        {
            new LegalDocumentVersionRepository(context).Add(Version("2026-10", Build.Now));
            await context.SaveChangesAsync();
        }

        await using (var context = new KhadraDbContext(published.Options))
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
