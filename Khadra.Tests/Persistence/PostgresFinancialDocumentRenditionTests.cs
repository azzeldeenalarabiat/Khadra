using Khadra.Application.FinancialDocuments.Rendering;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The PDF renditions of issued documents on PostgreSQL itself (payments Phase 6, migration
/// <c>20260928222747_FinancialDocumentRenditions</c>): the table, its keys and its append-only triggers; the
/// rendering pass's queries; a second process losing the race to record the same PDF — forced, not hoped
/// for — and removing its own copy; and the rollback, which refuses to run over a real document's PDFs. Opt-in:
/// set <c>KHADRA_TEST_POSTGRES</c> to a scratch database. Each scenario gets a fresh database of its own.
/// </summary>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresFinancialDocumentRenditionTests
{
    private const string Previous = "20260926230609_FinancialDocuments";
    private const string ThisMigration = "20260928222747_FinancialDocumentRenditions";

    private static readonly DateTimeOffset Now = Build.Now;

    [PostgresFact]
    public async Task The_table_its_keys_and_its_triggers_are_created_and_refuse_every_change()
    {
        var database = await FreshDatabaseAsync("renditions");
        var harness = new IssuanceHarness(database.Options);
        await harness.PaidAsync(PaymentProviders.Sandbox, unique: "31313");
        await harness.PassAsync();
        Assert.Equal(4, (await harness.RenderPassAsync()).Count(outcome => outcome.RenditionId is not null));

        await using var connection = await OpenAsync(database);
        var constraints = await NamesAsync(connection, @"
SELECT c.conname FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid WHERE t.relname = 'financial_document_renditions'");
        Assert.All(
            [
                "pk_financial_document_renditions",
                "ck_financial_document_renditions_size_bytes",
                "ck_financial_document_renditions_template_version",
                "fk_financial_document_renditions_financial_documents_document_",
            ],
            name => Assert.Contains(name, constraints));
        var indexes = await NamesAsync(connection, "SELECT indexname FROM pg_indexes WHERE tablename = 'financial_document_renditions'");
        Assert.Contains("ix_financial_document_renditions_document_id_language_format_t", indexes);
        Assert.Contains("ix_financial_document_renditions_storage_key", indexes);
        // The key restricts: a document cannot be removed from under its PDF (it cannot be removed at all).
        await using (var rule = new NpgsqlCommand(@"
SELECT confdeltype FROM pg_constraint WHERE conname = 'fk_financial_document_renditions_financial_documents_document_'", connection))
        {
            Assert.Equal('r', (char)(await rule.ExecuteScalarAsync())!);
        }

        Assert.Equal(
            ["financial_document_renditions_append_only", "financial_document_renditions_no_truncate"],
            (await NamesAsync(connection, @"
SELECT tgname FROM pg_trigger WHERE NOT tgisinternal AND tgrelid = 'financial_document_renditions'::regclass")).Order(StringComparer.Ordinal));
        await RefusedAsync(connection, "UPDATE financial_document_renditions SET size_bytes = size_bytes", "UPDATE");
        await RefusedAsync(connection, "DELETE FROM financial_document_renditions", "DELETE");
        await RefusedAsync(connection, "TRUNCATE financial_document_renditions", "TRUNCATE");
        await using var count = new NpgsqlCommand("SELECT count(*) FROM financial_document_renditions", connection);
        Assert.Equal(4L, (long)(await count.ExecuteScalarAsync())!);
    }

    [PostgresFact]
    public async Task A_second_process_that_loses_the_race_removes_its_own_copy()
    {
        var harness = new IssuanceHarness((await FreshDatabaseAsync("race")).Options);
        await harness.PaidAsync(PaymentProviders.Sandbox, unique: "41414");
        await harness.PassAsync();
        var receipt = (await harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.PaymentReceipt);
        var command = new RenderFinancialDocumentCommand(receipt.Id, Language.English);

        // The loser has looked (no PDF yet) and drawn; as its bytes reach storage, the winner draws, stores and
        // RECORDS the same PDF. The loser's own record then meets the unique index.
        RenditionOutcome? winner = null;
        harness.Storage.BeforeWrite = async _ =>
        {
            harness.Storage.BeforeWrite = null;
            await using var first = harness.NewContext();
            winner = await harness.RenderHandler(first).Handle(command, CancellationToken.None);
        };
        RenditionOutcome loser;
        await using (var second = harness.NewContext())
            loser = await harness.RenderHandler(second).Handle(command, CancellationToken.None);

        Assert.NotNull(winner!.RenditionId);
        Assert.Equal("lost_race", loser.Skipped);
        var recorded = Assert.Single(await harness.RenditionsAsync());
        Assert.Equal(winner.RenditionId, recorded.Id);
        // Only the winner's bytes remain: the loser's copy pointed at nothing, and went.
        Assert.Equal(recorded.StorageKey, Assert.Single(harness.Storage.Files.Keys));
        Assert.Single(harness.Storage.Deleted);
    }

    [PostgresFact]
    public async Task The_rollback_refuses_to_run_over_a_real_documents_pdf()
    {
        var database = await FreshDatabaseAsync("realpdf");
        await using (var context = new KhadraDbContext(database.Options))
        {
            await MigrateToAsync(context, ThisMigration);
            var document = Receipt("PAY-2026-000001", "Stripe");
            context.FinancialDocuments.Add(document);
            context.FinancialDocumentRenditions.Add(Rendition(document));
            await context.SaveChangesAsync();
        }

        await using (var context = new KhadraDbContext(database.Options))
        {
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(context, Previous));
            Assert.Contains("fixed forward", refusal.MessageText, StringComparison.Ordinal);
        }

        await using var connection = await OpenAsync(database);
        await using var count = new NpgsqlCommand("SELECT count(*) FROM financial_document_renditions", connection);
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
    }

    [PostgresFact]
    public async Task The_rollback_runs_over_test_documents_pdfs()
    {
        var database = await FreshDatabaseAsync("testpdf");
        await using (var context = new KhadraDbContext(database.Options))
        {
            await MigrateToAsync(context, ThisMigration);
            var document = Receipt("TEST-PAY-2026-000001", PaymentProviders.Sandbox);
            context.FinancialDocuments.Add(document);
            context.FinancialDocumentRenditions.Add(Rendition(document));
            await context.SaveChangesAsync();
        }

        await using (var context = new KhadraDbContext(database.Options))
            await MigrateToAsync(context, Previous);

        await using var connection = await OpenAsync(database);
        await using var exists = new NpgsqlCommand("SELECT to_regclass('financial_document_renditions') IS NULL", connection);
        Assert.True((bool)(await exists.ExecuteScalarAsync())!);
        // The documents themselves are untouched by this rollback.
        await using var documents = new NpgsqlCommand("SELECT count(*) FROM financial_documents", connection);
        Assert.Equal(1L, (long)(await documents.ExecuteScalarAsync())!);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private static FinancialDocument Receipt(string number, string provider)
    {
        var paymentId = Id.New();
        var draft = new FinancialDocumentDraft(
            FinancialDocumentType.PaymentReceipt,
            paymentId,
            Version: 1,
            PreviousVersionId: null,
            RelatedDocumentId: null,
            BookingId: Id.New(),
            BookingReference: "KH-PDFPROOF",
            CustomerId: Id.New(),
            DealerId: Id.New(),
            PaymentId: paymentId,
            RefundId: null,
            FinancialDocumentCause.PaymentCaptured,
            OccurredAt: Now,
            CoversThrough: null,
            CheckpointFingerprint: null,
            HeadlineAmount: Money.Jod(18m),
            Provider: provider,
            CalculatorVersion: 1,
            SnapshotSchemaVersion: 1,
            Snapshot: "{\"schemaVersion\":1}");
        return FinancialDocument.Issue(draft, number, Now);
    }

    private static FinancialDocumentRendition Rendition(FinancialDocument document) =>
        FinancialDocumentRendition.Record(
            document,
            Language.English,
            RenditionFormat.Pdf,
            1,
            "QuestPDF 2026.9.1",
            FinancialDocumentRendition.NewStorageKey(document.Id, Language.English, RenditionFormat.Pdf, 1),
            new string('b', 64),
            2048,
            Now);

    private static Task MigrateToAsync(KhadraDbContext context, string target) =>
        context.GetService<IMigrator>().MigrateAsync(target);

    private static async Task RefusedAsync(NpgsqlConnection connection, string sql, string operation)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Contains("is append-only", refusal.MessageText, StringComparison.Ordinal);
        Assert.Contains("financial_document_renditions", refusal.MessageText, StringComparison.Ordinal);
        Assert.Contains(operation, refusal.MessageText, StringComparison.Ordinal);
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

    /// <summary>A fresh scratch database: the options for a context over it, and its connection string.</summary>
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
