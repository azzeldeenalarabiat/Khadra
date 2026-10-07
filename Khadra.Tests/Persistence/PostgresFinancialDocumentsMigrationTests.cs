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
/// The payments Phase 5 migration (<c>20260926230609_FinancialDocuments</c>), proved on the engine that
/// runs it before it touched any real database (owner, 2026-09-27). Each scenario gets a FRESH scratch
/// database — named after the one <c>KHADRA_TEST_POSTGRES</c> names, with a suffix — because each needs
/// the schema at a particular migration, and a database nothing else uses is the only way to be sure what
/// it holds. They are left behind, never dropped: dropping databases from a test is one keystroke away
/// from dropping the wrong one.
/// </summary>
/// <remarks>
/// What is proved, one scenario each: every refund that existed before the migration gets exactly the
/// split the application rule gives it; every constraint, index and trigger is created; the append-only
/// triggers refuse UPDATE, DELETE and TRUNCATE; the migration is atomic — a failure part-way leaves no
/// trace of it; and the rollback refuses to run over a real document, and runs over test ones.
/// </remarks>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresFinancialDocumentsMigrationTests
{
    private const string Previous = "20260924162513_PaymentFeeRefundable";
    private const string ThisMigration = "20260926230609_FinancialDocuments";

    private static readonly DateTimeOffset Now = Build.Now;

    // ── The backfill, the schema and the triggers ────────────────────────────────────────────────

    [PostgresFact]
    public async Task Every_existing_refund_gets_the_split_the_application_rule_gives_it()
    {
        var database = await FreshDatabaseAsync("backfill");
        await using (var context = new KhadraDbContext(database.Options))
            await MigrateToAsync(context, Previous);

        // Refunds recorded BEFORE the split was stored: every reason, with and without a fee,
        // refundable or not, and a capture in another currency. The aggregate computes each split by
        // the application rule in memory; the database, still at the previous migration, never sees it.
        var recorded = RefundSplitCases.All()
            .Select(@case => RefundSplitCases.Record(@case.Reason, @case.Fee, @case.Refundable, Now))
            .ToList();
        var otherCurrency = RefundSplitCases.Opened(RefundSplitCases.Fee, refundable: true, Now);
        Assert.True(otherCurrency.Orphan(Money.Create(50m, "USD"), Now, "booking.not_awaiting_payment", Now).IsSuccess);
        var payments = recorded.Select(pair => pair.Payment).Append(otherCurrency).ToList();
        var expected = payments.SelectMany(payment => payment.Refunds).ToList();
        await InsertAsLegacyRowsAsync(database, payments);

        await using (var context = new KhadraDbContext(database.Options))
            await MigrateToAsync(context, ThisMigration);

        var actual = new Dictionary<Guid, (decimal Booking, decimal Fee)>();
        await using (var connection = await OpenAsync(database))
        await using (var read = new NpgsqlCommand("SELECT id, booking_part, fee_part FROM payment_refunds", connection))
        await using (var reader = await read.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                actual[reader.GetGuid(0)] = (reader.GetDecimal(1), reader.GetDecimal(2));
        }

        Assert.Equal(expected.Count, actual.Count);
        Assert.All(expected, refund =>
        {
            var (booking, fee) = actual[refund.Id.Value];
            Assert.Equal(refund.FeePart.Amount, fee);
            Assert.Equal(refund.BookingPart.Amount, booking);
        });
    }

    [PostgresFact]
    public async Task Every_constraint_index_and_trigger_is_created()
    {
        var database = await FreshDatabaseAsync("schema");
        await using (var context = new KhadraDbContext(database.Options))
            await MigrateToAsync(context, ThisMigration);

        await using var connection = await OpenAsync(database);
        var constraints = await NamesAsync(connection, @"
SELECT c.conname FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid
WHERE t.relname IN ('financial_documents', 'financial_document_voids', 'financial_document_series',
                    'financial_document_issuance_holds', 'payment_refunds')");
        string[] expectedConstraints =
        [
            "pk_financial_documents", "pk_financial_document_voids", "pk_financial_document_series",
            "pk_financial_document_issuance_holds",
            "ck_financial_documents_version", "ck_financial_documents_previous", "ck_financial_documents_subject",
            "ck_financial_documents_statement_coverage", "ck_financial_document_series_last_number",
            "ck_payment_refunds_split",
            "fk_financial_documents_financial_documents_previous_version_id",
            "fk_financial_documents_financial_documents_related_document_id",
            "fk_financial_document_voids_financial_documents_document_id",
        ];
        Assert.All(expectedConstraints, name => Assert.Contains(name, constraints));

        var indexes = await NamesAsync(connection, @"
SELECT indexname FROM pg_indexes
WHERE tablename IN ('financial_documents', 'financial_document_issuance_holds')");
        string[] expectedIndexes =
        [
            "ix_financial_documents_document_number", "ix_financial_documents_document_type_subject_id_version",
            "ix_financial_documents_customer_id_issued_at_id", "ix_financial_documents_booking_id_issued_at_id",
            "ix_financial_documents_issued_at_id", "ix_financial_documents_payment_id",
            "ix_financial_documents_previous_version_id", "ix_financial_documents_related_document_id",
            "ix_financial_document_issuance_holds_document_type_subject_id",
            "ix_financial_document_issuance_holds_next_attempt_at",
        ];
        Assert.All(expectedIndexes, name => Assert.Contains(name, indexes));

        var triggers = await NamesAsync(connection, @"
SELECT tgname FROM pg_trigger
WHERE NOT tgisinternal AND tgrelid IN ('financial_documents'::regclass, 'financial_document_voids'::regclass)");
        Assert.Equal(
            ["financial_document_voids_append_only", "financial_document_voids_no_truncate",
             "financial_documents_append_only", "financial_documents_no_truncate"],
            triggers.Order(StringComparer.Ordinal));

        // Every new table starts empty.
        foreach (var table in new[] { "financial_documents", "financial_document_voids", "financial_document_series", "financial_document_issuance_holds" })
        {
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
            Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
        }
    }

    [PostgresFact]
    public async Task The_database_refuses_to_change_or_erase_an_issued_document_or_a_void()
    {
        var database = await FreshDatabaseAsync("appendonly");
        await using (var context = new KhadraDbContext(database.Options))
        {
            await MigrateToAsync(context, ThisMigration);
            var document = Receipt("TEST-PAY-2026-000001", PaymentProviders.Sandbox);
            context.FinancialDocuments.Add(document);
            context.FinancialDocumentVoids.Add(FinancialDocumentVoid.Record(document.Id, Id.New(), "Issued from the wrong capture.", Now).Value);
            await context.SaveChangesAsync();
        }

        await using var connection = await OpenAsync(database);
        await RefusedAsync(connection, "UPDATE financial_documents SET document_number = document_number", "financial_documents", "UPDATE");
        await RefusedAsync(connection, "DELETE FROM financial_documents", "financial_documents", "DELETE");
        await RefusedAsync(connection, "UPDATE financial_document_voids SET reason = reason", "financial_document_voids", "UPDATE");
        await RefusedAsync(connection, "DELETE FROM financial_document_voids", "financial_document_voids", "DELETE");
        await RefusedAsync(connection, "TRUNCATE financial_document_voids", "financial_document_voids", "TRUNCATE");
        // Truncating the documents alone is refused earlier, by the void's foreign key; together with
        // the voids, the key is satisfied and the statement trigger is what refuses it.
        await RefusedAsync(connection, "TRUNCATE financial_documents, financial_document_voids", "financial_document", "TRUNCATE");

        await using var count = new NpgsqlCommand("SELECT (SELECT count(*) FROM financial_documents) + (SELECT count(*) FROM financial_document_voids)", connection);
        Assert.Equal(2L, (long)(await count.ExecuteScalarAsync())!);
    }

    // ── Atomic ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A failure part-way — here, a table in the way of the fourth statement's CREATE, AFTER the refund
    /// split was added and filled — must leave no trace: no split columns, no new tables, no history row.
    /// </summary>
    [PostgresFact]
    public async Task A_failure_part_way_leaves_no_trace_of_the_migration()
    {
        var database = await FreshDatabaseAsync("atomic");
        await using (var context = new KhadraDbContext(database.Options))
            await MigrateToAsync(context, Previous);
        var (payment, _) = RefundSplitCases.Record(RefundReason.FreeCancellation, RefundSplitCases.Fee, refundable: true, Now);
        await InsertAsLegacyRowsAsync(database, [payment]);

        await using (var connection = await OpenAsync(database))
        await using (var blocker = new NpgsqlCommand("CREATE TABLE financial_document_series (blocker integer)", connection))
            await blocker.ExecuteNonQueryAsync();

        await using (var context = new KhadraDbContext(database.Options))
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(context, ThisMigration));
            Assert.Equal(PostgresErrorCodes.DuplicateTable, failure.SqlState);
        }

        await using var check = await OpenAsync(database);
        Assert.Empty(await NamesAsync(check,
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'payment_refunds' AND column_name IN ('booking_part', 'fee_part')"));
        Assert.Empty(await NamesAsync(check,
            "SELECT table_name FROM information_schema.tables WHERE table_name IN ('financial_documents', 'financial_document_voids', 'financial_document_issuance_holds')"));
        Assert.Empty(await NamesAsync(check,
            $"SELECT migration_id FROM \"__EFMigrationsHistory\" WHERE migration_id = '{ThisMigration}'"));
        await using var refunds = new NpgsqlCommand("SELECT count(*) FROM payment_refunds", check);
        Assert.Equal(1L, (long)(await refunds.ExecuteScalarAsync())!);
    }

    // ── The rollback ─────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task The_rollback_refuses_to_run_over_a_real_document()
    {
        var database = await FreshDatabaseAsync("guard");
        await using (var context = new KhadraDbContext(database.Options))
        {
            await MigrateToAsync(context, ThisMigration);
            context.FinancialDocuments.Add(Receipt("PAY-2026-000001", "TestProvider"));
            await context.SaveChangesAsync();
        }

        await using (var context = new KhadraDbContext(database.Options))
        {
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(context, Previous));
            Assert.Contains("holds real documents", refusal.MessageText, StringComparison.Ordinal);
        }

        await using var check = await OpenAsync(database);
        await using var documents = new NpgsqlCommand("SELECT count(*) FROM financial_documents", check);
        Assert.Equal(1L, (long)(await documents.ExecuteScalarAsync())!);
        Assert.Single(await NamesAsync(check,
            $"SELECT migration_id FROM \"__EFMigrationsHistory\" WHERE migration_id = '{ThisMigration}'"));
    }

    [PostgresFact]
    public async Task The_rollback_runs_over_test_documents_only()
    {
        var database = await FreshDatabaseAsync("guardok");
        await using (var context = new KhadraDbContext(database.Options))
        {
            await MigrateToAsync(context, ThisMigration);
            context.FinancialDocuments.Add(Receipt("TEST-PAY-2026-000001", PaymentProviders.Sandbox));
            await context.SaveChangesAsync();
        }

        await using (var context = new KhadraDbContext(database.Options))
            await MigrateToAsync(context, Previous);

        await using var check = await OpenAsync(database);
        Assert.Empty(await NamesAsync(check,
            "SELECT table_name FROM information_schema.tables WHERE table_name LIKE 'financial_document%'"));
        Assert.Empty(await NamesAsync(check,
            "SELECT column_name FROM information_schema.columns WHERE table_name = 'payment_refunds' AND column_name IN ('booking_part', 'fee_part')"));
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
            BookingReference: "KH-PROOF001",
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

    /// <summary>
    /// Writes payments with their refunds as a database at the PREVIOUS migration holds them, both by hand with the
    /// columns each table had then: a refund with no split, and a payment with no capture reference.
    /// </summary>
    /// <remarks>
    /// The payment went through EF until Wave 4, on the ground that its table "does not change". It did: B1 gave
    /// <c>payments</c> its capture reference, and today's model writes a column this schema has never heard of.
    /// </remarks>
    private static async Task InsertAsLegacyRowsAsync(Scratch database, IReadOnlyList<Payment> payments)
    {
        var refunds = payments.SelectMany(payment => payment.Refunds).ToList();

        await using var connection = await OpenAsync(database);
        foreach (var payment in payments)
        {
            await using var insert = new NpgsqlCommand(@"
INSERT INTO payments (id, booking_id, customer_id, amount, currency, purpose, processing_fee, fee_refundable, status,
                      provider, provider_reference, checkout_url, expires_at, created_at, captured_at, amount_captured,
                      captured_currency, applied_at, orphaned_at, orphan_reason, failed_at, failure_code)
VALUES (@id, @booking, @customer, @amount, @currency, @purpose, @fee, @refundable, @status, @provider, @reference,
        @checkout, @expires, @created, @captured, @capturedAmount, @capturedCurrency, @applied, @orphaned,
        @orphanReason, @failed, @failureCode)", connection);
            insert.Parameters.AddWithValue("id", payment.Id.Value);
            insert.Parameters.AddWithValue("booking", payment.BookingId.Value);
            insert.Parameters.AddWithValue("customer", payment.CustomerId.Value);
            insert.Parameters.AddWithValue("amount", payment.Amount.Amount);
            insert.Parameters.AddWithValue("currency", payment.Amount.CurrencyCode);
            insert.Parameters.AddWithValue("purpose", payment.Purpose.Name);
            insert.Parameters.AddWithValue("fee", payment.ProcessingFee.Amount);
            insert.Parameters.AddWithValue("refundable", payment.FeeRefundable);
            insert.Parameters.AddWithValue("status", payment.Status.Name);
            insert.Parameters.AddWithValue("provider", payment.Provider);
            insert.Parameters.AddWithValue("reference", (object?)payment.ProviderReference ?? DBNull.Value);
            insert.Parameters.AddWithValue("checkout", (object?)payment.CheckoutUrl ?? DBNull.Value);
            insert.Parameters.AddWithValue("expires", payment.ExpiresAt);
            insert.Parameters.AddWithValue("created", payment.CreatedAt);
            insert.Parameters.AddWithValue("captured", (object?)payment.CapturedAt ?? DBNull.Value);
            insert.Parameters.AddWithValue("capturedAmount", (object?)payment.AmountCaptured?.Amount ?? DBNull.Value);
            insert.Parameters.AddWithValue("capturedCurrency", (object?)payment.AmountCaptured?.CurrencyCode ?? DBNull.Value);
            insert.Parameters.AddWithValue("applied", (object?)payment.AppliedAt ?? DBNull.Value);
            insert.Parameters.AddWithValue("orphaned", (object?)payment.OrphanedAt ?? DBNull.Value);
            insert.Parameters.AddWithValue("orphanReason", (object?)payment.OrphanReason ?? DBNull.Value);
            insert.Parameters.AddWithValue("failed", (object?)payment.FailedAt ?? DBNull.Value);
            insert.Parameters.AddWithValue("failureCode", (object?)payment.FailureCode ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync();
        }

        foreach (var refund in refunds)
        {
            await using var insert = new NpgsqlCommand(@"
INSERT INTO payment_refunds (id, payment_id, amount, currency, reason, dispute_ticket_id, status,
                             provider_reference, failure_code, requested_at, sent_at, settled_at, failed_at)
VALUES (@id, @payment, @amount, @currency, @reason, @ticket, @status, NULL, NULL, @requested, NULL, NULL, NULL)", connection);
            insert.Parameters.AddWithValue("id", refund.Id.Value);
            insert.Parameters.AddWithValue("payment", refund.PaymentId.Value);
            insert.Parameters.AddWithValue("amount", refund.Amount.Amount);
            insert.Parameters.AddWithValue("currency", refund.Amount.CurrencyCode);
            insert.Parameters.AddWithValue("reason", refund.Reason.Name);
            insert.Parameters.AddWithValue("ticket", (object?)refund.DisputeTicketId?.Value ?? DBNull.Value);
            insert.Parameters.AddWithValue("status", refund.Status.Name);
            insert.Parameters.AddWithValue("requested", refund.RequestedAt);
            await insert.ExecuteNonQueryAsync();
        }
    }

    private static Task MigrateToAsync(KhadraDbContext context, string target) =>
        context.GetService<IMigrator>().MigrateAsync(target);

    private static async Task RefusedAsync(NpgsqlConnection connection, string sql, string table, string operation)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Contains("is append-only", refusal.MessageText, StringComparison.Ordinal);
        Assert.Contains(table, refusal.MessageText, StringComparison.Ordinal);
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

    /// <summary>A database nothing else has used, named after the configured scratch one.</summary>
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
