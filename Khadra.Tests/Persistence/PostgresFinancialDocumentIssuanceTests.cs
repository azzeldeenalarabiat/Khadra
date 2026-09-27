using Khadra.Application.Payments;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Issuing financial documents on PostgreSQL itself (payments Phase 5): the detection and reading queries
/// translate, the number counter holds its row lock so two issuers never take one number and a rolled-back
/// number comes back, and the <c>json</c> column returns the snapshot exactly as it was written. Opt-in: set
/// <c>KHADRA_TEST_POSTGRES</c> to a scratch database. Each test gets a fresh database of its own, so every
/// number it asserts is its own.
/// </summary>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresFinancialDocumentIssuanceTests
{
    [PostgresFact]
    public async Task A_pass_finds_issues_and_reads_everything_on_postgresql()
    {
        var harness = new IssuanceHarness((await FreshDatabaseAsync("issue")).Options);
        var (refunded, refundedPayment) = await harness.PaidAsync(PaymentProviders.Sandbox, inFull: true, fee: 4.5m, unique: "11111");
        var (collected, _) = await harness.PaidAsync(PaymentProviders.Sandbox, unique: "22222");

        var first = await harness.PassAsync();
        Assert.Equal(
            ["TEST-PAY-2026-000001", "TEST-PAY-2026-000002", "TEST-STM-2026-000001", "TEST-STM-2026-000002"],
            first.Select(outcome => outcome.Number ?? "(none)").Order(StringComparer.Ordinal).ToArray());

        // Money moves on both: a refund settles on one, the office records cash at the other's pickup.
        await harness.ChangeAsync(async context =>
        {
            var booking = await context.Bookings.Include(candidate => candidate.Handovers).SingleAsync(candidate => candidate.Id == refunded.Id);
            var payment = await context.Payments.Include(candidate => candidate.Refunds).SingleAsync(candidate => candidate.Id == refundedPayment.Id);
            Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, harness.Now).IsSuccess);
            var refund = BookingEndingRefunds.Record(booking, payment, harness.Now)!;
            refund.MarkSent("rf_pg", harness.Now.AddMinutes(1));
            refund.MarkSettled(harness.Now.AddMinutes(2));
            var pickedUp = await context.Bookings.Include(candidate => candidate.Handovers).SingleAsync(candidate => candidate.Id == collected.Id);
            Assert.True(pickedUp.RecordPickup(BookingParty.Dealer, Id.New(), harness.Now.AddMinutes(3), cashCollected: Money.Jod(72m)).IsSuccess);
        });
        // Beyond the late-commit margin, so only the checkpoints themselves make the statements candidates.
        harness.Now = harness.Now.AddMinutes(30);

        var second = await harness.PassAsync();

        Assert.Equal(
            ["TEST-RFD-2026-000001", "TEST-STM-2026-000003", "TEST-STM-2026-000004"],
            second.Where(outcome => outcome.Number is not null).Select(outcome => outcome.Number!).Order(StringComparer.Ordinal).ToArray());

        await using var reading = harness.NewContext();
        var reader = new FinancialDocumentReader(reading);
        Assert.Empty(await reader.PendingForBookingAsync(refunded.Id));
        Assert.Equal(4, (await reader.ListForBookingAsync(refunded.Id)).Count);
        // The payment's own receipt and its refund's receipt: what the administrator's payment page lists.
        Assert.Equal(2, (await reader.ListForPaymentAsync(refundedPayment.Id)).Count);
        var receipt = (await reader.ListForPaymentAsync(refundedPayment.Id)).Single(document => document.Type == FinancialDocumentType.PaymentReceipt);
        Assert.Equal(2, (await reader.FamilyAsync(FinancialDocumentType.BookingStatement, refunded.Id)).Count);
        Assert.Null(await reader.VoidOfAsync(receipt.Id));
        Assert.Equal(2, (await reader.ListAsync(new(null, FinancialDocumentStatus.Superseded, null, null, null, null), new(1, 20))).TotalCount);
        Assert.Equal(5, (await reader.ListAsync(new(null, FinancialDocumentStatus.Current, null, null, null, null), new(1, 20))).TotalCount);
        Assert.Equal(0, (await reader.OpenHoldsSummaryAsync()).Count);
        Assert.Equal(0, (await reader.ListOpenHoldsAsync(new(1, 20))).TotalCount);
    }

    [PostgresFact]
    public async Task Holds_are_found_waited_on_and_lifted_on_postgresql()
    {
        var harness = new IssuanceHarness((await FreshDatabaseAsync("hold")).Options) { Issuer = null };
        var (booking, _) = await harness.PaidAsync(PaymentProviders.Sandbox);

        Assert.Equal(2, (await harness.PassAsync()).Count(outcome => outcome.Hold is not null));
        harness.Now = harness.Now.AddMinutes(1);
        Assert.Empty(await harness.PassAsync());

        await using (var reading = harness.NewContext())
        {
            var reader = new FinancialDocumentReader(reading);
            var summary = await reader.OpenHoldsSummaryAsync();
            Assert.Equal(2, summary.Count);
            Assert.Equal([booking.Reference.Value], summary.BookingReferences);
            Assert.Equal(2, (await reader.OpenHoldsForBookingAsync(booking.Id)).Count);
        }

        harness.Issuer = DocumentFixtures.Issuer;
        Assert.Equal(2, (await harness.PassAsync()).Count(outcome => outcome.Number is not null));
    }

    [PostgresFact]
    public async Task Two_issuers_never_take_one_number_and_a_rolled_back_number_comes_back()
    {
        var options = (await FreshDatabaseAsync("number")).Options;
        const string series = "TEST-PAY-2026";
        var now = IssuanceHarness.Start;

        await using var first = new KhadraDbContext(options);
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        Assert.Equal(1, await new FinancialDocumentSeriesCounter(first).TakeNextAsync(series, now));

        // A second issuer asks while the first still holds the (new, uncommitted) row: it waits.
        var second = Task.Run(async () =>
        {
            await using var context = new KhadraDbContext(options);
            await using var transaction = await context.Database.BeginTransactionAsync();
            var number = await new FinancialDocumentSeriesCounter(context).TakeNextAsync(series, now);
            await transaction.CommitAsync();
            return number;
        });
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.False(second.IsCompleted);

        // The first issuer's document fails: its number goes back with the rollback, and the waiting
        // issuer takes it — no gap.
        await firstTransaction.RollbackAsync();
        Assert.Equal(1, await second.WaitAsync(TimeSpan.FromSeconds(30)));

        await using var third = new KhadraDbContext(options);
        await using var thirdTransaction = await third.Database.BeginTransactionAsync();
        Assert.Equal(2, await new FinancialDocumentSeriesCounter(third).TakeNextAsync(series, now));
        await thirdTransaction.CommitAsync();
    }

    [PostgresFact]
    public async Task Two_administrators_voiding_one_document_at_once_leave_one_void_and_one_correction()
    {
        var harness = new IssuanceHarness((await FreshDatabaseAsync("void")).Options);
        await harness.PaidAsync(PaymentProviders.Sandbox);
        await harness.PassAsync();
        var receipt = (await harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.PaymentReceipt);

        var results = await Task.WhenAll(
            Task.Run(() => harness.VoidAsync(receipt.Id, "Wrong, says the first.")),
            Task.Run(() => harness.VoidAsync(receipt.Id, "Wrong, says the second.")));

        Assert.Single(results, result => result.IsSuccess);
        var refused = Assert.Single(results, result => result.IsFailure);
        Assert.Contains(refused.Error.Code, new[] { FinancialDocumentErrors.AlreadyVoided.Code, FinancialDocumentErrors.NotCurrent.Code });

        await using var context = harness.NewContext();
        Assert.Single(await context.FinancialDocumentVoids.ToListAsync());
        Assert.Single(await context.AuditEntries.ToListAsync());
        var family = await context.FinancialDocuments
            .Where(document => document.Type == FinancialDocumentType.PaymentReceipt && document.SubjectId == receipt.SubjectId)
            .ToListAsync();
        Assert.Equal([1, 2], family.Select(document => document.Version).Order().ToArray());
        // The loser's number went back with its rollback: the correction is the next number, and no gap follows it.
        Assert.Equal("TEST-PAY-2026-000002", family.Single(document => document.Version == 2).Number);
    }

    [PostgresFact]
    public async Task The_json_column_returns_the_snapshot_exactly_as_it_was_written()
    {
        var scratch = await FreshDatabaseAsync("json");
        var harness = new IssuanceHarness(scratch.Options);
        await harness.PaidAsync(PaymentProviders.Sandbox);
        await harness.PassAsync();
        var documents = await harness.DocumentsAsync();

        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync();
        foreach (var document in documents)
        {
            await using var command = new NpgsqlCommand("SELECT snapshot::text, content_sha256 FROM financial_documents WHERE id = @id", connection);
            command.Parameters.AddWithValue("id", document.Id.Value);
            await using var row = await command.ExecuteReaderAsync();
            Assert.True(await row.ReadAsync());
            var stored = row.GetString(0);

            // Byte for byte: json keeps the text — key order, Arabic, isolates — where jsonb would rewrite it.
            Assert.Equal(document.Snapshot, stored);
            Assert.StartsWith("""{"schemaVersion":1,"document":{"type":""", stored, StringComparison.Ordinal);
            Assert.Equal(FinancialDocument.Sha256(stored), row.GetString(1));
        }
    }

    /// <summary>A fresh scratch database: the options for a context over it, and its connection string.</summary>
    private sealed record Scratch(DbContextOptions<KhadraDbContext> Options, string ConnectionString);

    /// <summary>A fresh database named after the configured scratch one, migrated to the latest schema.</summary>
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
