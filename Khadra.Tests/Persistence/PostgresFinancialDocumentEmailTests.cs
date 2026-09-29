using Khadra.Application.FinancialDocuments.Email;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.FinancialDocuments.Repositories;
using Khadra.Domain.Payments;
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
/// The emails of issued receipts on PostgreSQL itself (payments Phase 7, migration
/// <c>20260929164711_FinancialDocumentDeliveries</c>): the tables, their keys and the attempts' append-only triggers; a
/// claim that passes over rows another process holds — forced, not hoped for; one queued email per document, held by
/// the database; a whole pass, from issue to a sent email with its PDFs, through the PostgreSQL query shapes; and the
/// rollback, which refuses to run over a real document's emails. Opt-in: set <c>KHADRA_TEST_POSTGRES</c> to a scratch
/// database. Each scenario gets a fresh database of its own.
/// </summary>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresFinancialDocumentEmailTests
{
    private const string Previous = "20260929020747_FinancialDocumentRenditionKind";

    [PostgresFact]
    public async Task The_tables_keys_and_triggers_are_created_and_the_history_refuses_every_change()
    {
        var database = await FreshDatabaseAsync("emails");
        var harness = new IssuanceHarness(database.Options);
        await harness.PaidAsync(PaymentProviders.Sandbox, unique: "51515");
        await harness.PassAsync();
        await harness.RenderPassAsync();
        Assert.Equal("Sent", Assert.Single(await harness.EmailPassAsync()).State);

        await using var connection = await OpenAsync(database);
        var indexes = await NamesAsync(connection, "SELECT indexname FROM pg_indexes WHERE tablename = 'financial_document_deliveries'");
        Assert.Contains("ix_financial_document_deliveries_one_queued", indexes);
        Assert.Contains("ix_financial_document_deliveries_queued_due", indexes);
        Assert.Contains("ix_financial_document_deliveries_failed", indexes);
        // Every constraint named for what it holds — the two PDFs' by their language.
        Assert.Equal(
            [
                "ck_financial_document_deliveries_completed",
                "ck_financial_document_deliveries_counts",
                "ck_financial_document_deliveries_waiting",
                "ck_financial_document_delivery_attempts_number",
                "fk_financial_document_deliveries_document",
                "fk_financial_document_delivery_attempts_arabic_rendition",
                "fk_financial_document_delivery_attempts_delivery",
                "fk_financial_document_delivery_attempts_english_rendition",
            ],
            (await NamesAsync(connection, @"
SELECT conname FROM pg_constraint
 WHERE conrelid IN ('financial_document_deliveries'::regclass, 'financial_document_delivery_attempts'::regclass)
   AND contype IN ('c', 'f')")).Order(StringComparer.Ordinal));
        // A sent email never says it is waiting for its PDF.
        await using (var waiting = new NpgsqlCommand(
            "UPDATE financial_document_deliveries SET waiting_reason = 'PdfNotReady', waiting_since = now()", connection))
        {
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => waiting.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
            Assert.Equal("ck_financial_document_deliveries_waiting", refusal.ConstraintName);
        }
        Assert.Equal(
            ["financial_document_delivery_attempts_append_only", "financial_document_delivery_attempts_no_truncate"],
            (await NamesAsync(connection, @"
SELECT tgname FROM pg_trigger WHERE NOT tgisinternal AND tgrelid = 'financial_document_delivery_attempts'::regclass")).Order(StringComparer.Ordinal));
        // The header is the outbox row: it moves, so nothing guards it.
        Assert.Empty(await NamesAsync(connection, @"
SELECT tgname FROM pg_trigger WHERE NOT tgisinternal AND tgrelid = 'financial_document_deliveries'::regclass"));

        await RefusedAsync(connection, "UPDATE financial_document_delivery_attempts SET error = 'rewritten'", "UPDATE");
        await RefusedAsync(connection, "DELETE FROM financial_document_delivery_attempts", "DELETE");
        await RefusedAsync(connection, "TRUNCATE financial_document_delivery_attempts", "TRUNCATE");
    }

    [PostgresFact]
    public async Task A_claim_passes_over_the_emails_another_process_holds_and_never_takes_one_twice()
    {
        var database = await FreshDatabaseAsync("claim");
        var harness = new IssuanceHarness(database.Options);
        await harness.PaidAsync(PaymentProviders.Sandbox, unique: "61616");
        await harness.PaidAsync(PaymentProviders.Sandbox, unique: "62626");
        await harness.PassAsync();
        Assert.Equal(2, (await harness.DeliveriesAsync()).Count);

        // The first process claims one email and has not committed: its row is locked.
        await using var first = harness.NewContext();
        await using var transaction = await first.Database.BeginTransactionAsync();
        var held = Assert.Single(await new FinancialDocumentDeliveryRepository(first).ClaimDueAsync(harness.Now, TimeSpan.FromMinutes(5), batchSize: 1));
        Assert.Equal(1, held.Claims);

        // The second takes the OTHER one — without waiting, and without the first's.
        await using (var second = harness.NewContext())
        {
            var taken = await new FinancialDocumentDeliveryRepository(second).ClaimDueAsync(harness.Now, TimeSpan.FromMinutes(5), batchSize: 10);
            var other = Assert.Single(taken);
            Assert.NotEqual(held.DeliveryId, other.DeliveryId);
            Assert.Equal(1, other.Claims);
        }

        await transaction.CommitAsync();
        var deliveries = await harness.DeliveriesAsync();
        Assert.All(deliveries, delivery => Assert.Equal(1, delivery.Claims));
        Assert.All(deliveries, delivery => Assert.Equal(harness.Now.AddMinutes(5), delivery.NextAttemptAt));
    }

    [PostgresFact]
    public async Task A_claim_taken_over_mid_step_is_left_to_the_process_that_took_it_and_sent_once()
    {
        var database = await FreshDatabaseAsync("takeover");
        var harness = new IssuanceHarness(database.Options);
        await harness.PaidAsync(PaymentProviders.Sandbox, unique: "65656");
        await harness.PassAsync();
        await harness.RenderPassAsync();
        var mine = Assert.Single(await harness.ClaimEmailsAsync());

        // Forced, not hoped for: while this process reads the PDFs its lease runs out and another claims the email.
        ClaimedFinancialDocumentDelivery? theirs = null;
        harness.Storage.BeforeOpen = async _ =>
        {
            harness.Storage.BeforeOpen = null;
            harness.Now = harness.Now.Add(harness.EmailSettings.Lease).AddSeconds(1);
            theirs = Assert.Single(await harness.ClaimEmailsAsync());
        };

        // Its attempt is refused by the claim count it no longer holds; the other process sends it, once.
        Assert.Same(FinancialDocumentEmailOutcome.Untouched, await harness.EmailAsync(mine));
        Assert.Equal((0, 2), (Assert.Single(await harness.DeliveriesAsync()).SendAttempts, theirs!.Claims));
        Assert.Equal("Sent", (await harness.EmailAsync(theirs)).State);
        Assert.Single(harness.Mail.Messages);
        Assert.Same(FinancialDocumentEmailOutcome.Untouched, await harness.EmailAsync(mine));
    }

    [PostgresFact]
    public async Task The_database_holds_one_queued_email_per_document()
    {
        var database = await FreshDatabaseAsync("onequeued");
        var harness = new IssuanceHarness(database.Options);
        await harness.PaidAsync(PaymentProviders.Sandbox, unique: "71717");
        await harness.PassAsync();
        var receipt = (await harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.PaymentReceipt);

        await using var context = harness.NewContext();
        context.FinancialDocumentDeliveries.Add(FinancialDocumentDelivery.Queue(receipt, Id.New(), harness.Now));
        var refusal = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(refusal.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("ix_financial_document_deliveries_one_queued", postgres.ConstraintName);
    }

    [PostgresFact]
    public async Task The_administrators_readings_run_on_postgresql()
    {
        var database = await FreshDatabaseAsync("readings");
        var harness = new IssuanceHarness(database.Options);
        await harness.PaidAsync(PaymentProviders.Sandbox, unique: "81818");
        await harness.PassAsync();
        var receipt = (await harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.PaymentReceipt);

        // Stale: waiting past the threshold for a PDF nobody has drawn.
        harness.Now = harness.Now.Add(harness.EmailSettings.StaleAfter).AddMinutes(1);
        await using (var context = harness.NewContext())
        {
            var summary = await new FinancialDocumentReader(context).EmailsNotSentSummaryAsync(harness.Now.Subtract(harness.EmailSettings.StaleAfter));
            Assert.Equal([receipt.Number], summary.Numbers);
        }

        await harness.RenderPassAsync();
        await harness.EmailPassAsync();
        await using (var context = harness.NewContext())
        {
            var reader = new FinancialDocumentReader(context);
            var email = Assert.Single(await reader.DeliveriesOfAsync(receipt.Id));
            Assert.Equal(FinancialDocumentDeliveryState.Sent, email.State);
            Assert.NotNull(Assert.Single(email.Attempts).EnglishPdfSha256);
            Assert.Equal(0, (await reader.EmailsNotSentSummaryAsync(harness.Now.Subtract(harness.EmailSettings.StaleAfter))).Count);
        }
    }

    [PostgresFact]
    public async Task The_rollback_refuses_to_run_over_a_real_documents_emails_and_runs_over_test_ones()
    {
        var real = await FreshDatabaseAsync("realemail");
        await using (var context = new KhadraDbContext(real.Options))
        {
            var document = Receipt("PAY-2026-000001", "Stripe");
            context.FinancialDocuments.Add(document);
            context.FinancialDocumentDeliveries.Add(FinancialDocumentDelivery.Queue(document, null, Build.Now));
            await context.SaveChangesAsync();
        }

        await using (var context = new KhadraDbContext(real.Options))
        {
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync(Previous));
            Assert.Contains("fixed forward", refusal.MessageText, StringComparison.Ordinal);
        }

        var test = await FreshDatabaseAsync("testemail");
        await using (var context = new KhadraDbContext(test.Options))
        {
            var document = Receipt("TEST-PAY-2026-000001", PaymentProviders.Sandbox);
            context.FinancialDocuments.Add(document);
            context.FinancialDocumentDeliveries.Add(FinancialDocumentDelivery.Queue(document, null, Build.Now));
            await context.SaveChangesAsync();
            await context.GetService<IMigrator>().MigrateAsync(Previous);
        }

        await using var connection = await OpenAsync(test);
        await using var gone = new NpgsqlCommand("SELECT to_regclass('financial_document_deliveries') IS NULL AND to_regclass('financial_document_delivery_attempts') IS NULL", connection);
        Assert.True((bool)(await gone.ExecuteScalarAsync())!);
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
            BookingReference: "KH-EMLPROOF",
            CustomerId: Id.New(),
            DealerId: Id.New(),
            PaymentId: paymentId,
            RefundId: null,
            FinancialDocumentCause.PaymentCaptured,
            OccurredAt: Build.Now,
            CoversThrough: null,
            CheckpointFingerprint: null,
            HeadlineAmount: Money.Jod(18m),
            Provider: provider,
            CalculatorVersion: 1,
            SnapshotSchemaVersion: 1,
            Snapshot: "{\"schemaVersion\":1}");
        return FinancialDocument.Issue(draft, number, Build.Now);
    }

    private static async Task RefusedAsync(NpgsqlConnection connection, string sql, string operation)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Contains("is append-only", refusal.MessageText, StringComparison.Ordinal);
        Assert.Contains("financial_document_delivery_attempts", refusal.MessageText, StringComparison.Ordinal);
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
