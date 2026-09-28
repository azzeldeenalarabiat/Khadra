using System.Collections.Concurrent;
using Khadra.Application.FinancialDocuments.Queries;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Issuing financial documents on PostgreSQL itself (payments Phase 5): the detection and reading queries
/// translate, the number counter holds its row lock so two issuers never take one number and a rolled-back
/// number comes back, two administrators voiding one document at once — the overlap forced, not hoped for —
/// leave one void, one correction and no gap, a receipt's correction brings the booking's statement one
/// version, and the <c>json</c> column returns the snapshot exactly as it was written. Opt-in: set
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

    /// <summary>
    /// Two administrators void one document at the same moment, and the overlap is FORCED rather than hoped
    /// for (pre-launch item 182). The test holds the number series' row and lets go only once PostgreSQL
    /// reports BOTH voids waiting on it: each is then past the "already voided?" and "still current?" checks
    /// and inside its own transaction, so the one that takes a number second can no longer be turned away by
    /// a check. Only the database can refuse it — the unique index its void or its correction collides with —
    /// and that refusal is the handler's race branch.
    /// </summary>
    [PostgresFact]
    public async Task Two_administrators_voiding_one_document_at_once_leave_one_void_one_correction_and_no_gap()
    {
        var scratch = await FreshDatabaseAsync("void");
        var collisions = new UniqueViolationRecorder();
        var harness = new IssuanceHarness(new DbContextOptionsBuilder<KhadraDbContext>()
            .UseNpgsql(scratch.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(collisions)
            .Options);
        await harness.PaidAsync(PaymentProviders.Sandbox);
        await harness.PassAsync();
        var receipt = (await harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.PaymentReceipt);
        Assert.Equal("TEST-PAY-2026-000001", receipt.Number);
        const string series = "TEST-PAY-2026";

        // The series row, held the way an issuer part-way through its own transaction holds it.
        await using var holder = new NpgsqlConnection(scratch.ConnectionString);
        await holder.OpenAsync();
        await using var holding = await holder.BeginTransactionAsync();
        Assert.Equal(1L, await LastNumberAsync(holder, series, holding));

        string[] reasons = ["Wrong, says the first.", "Wrong, says the second."];
        var voids = reasons.Select(reason => Task.Run(() => harness.VoidAsync(receipt.Id, reason))).ToArray();
        await BothWaitForTheNumberAsync(scratch.ConnectionString, holder.ProcessID, voids);
        Assert.Empty(collisions.Constraints);

        // Let go: one takes number 2 and commits; the other then takes number 3 and collides with it.
        await holding.RollbackAsync();
        var results = await Task.WhenAll(voids).WaitAsync(TimeSpan.FromSeconds(30));

        var winner = Array.FindIndex(results, result => result.IsSuccess);
        Assert.Single(results, result => result.IsSuccess);
        var refused = Assert.Single(results, result => result.IsFailure);
        // Refused by the database, not by a check it had already passed: one unique violation, on an index
        // the race branch maps to exactly the refusal the loser returned.
        var collision = Assert.Single(collisions.Constraints);
        Assert.Equal(
            collision switch
            {
                "pk_financial_document_voids" => FinancialDocumentErrors.AlreadyVoided.Code,
                "ix_financial_documents_document_type_subject_id_version" => FinancialDocumentErrors.NotCurrent.Code,
                _ => $"(an index the race branch does not expect: {collision})",
            },
            refused.Error.Code);

        await using var context = harness.NewContext();
        // One void — the winner's, of the receipt — and one audit entry.
        var voided = Assert.Single(await context.FinancialDocumentVoids.ToListAsync());
        Assert.Equal(receipt.Id, voided.DocumentId);
        Assert.Equal(reasons[winner], voided.Reason);
        Assert.Single(await context.AuditEntries.ToListAsync());
        // One correction: the receipt's family is the receipt and version 2, the document the winner reported.
        var family = await context.FinancialDocuments
            .Where(document => document.Type == FinancialDocumentType.PaymentReceipt && document.SubjectId == receipt.SubjectId)
            .ToListAsync();
        Assert.Equal([1, 2], family.Select(document => document.Version).Order().ToArray());
        var correction = family.Single(document => document.Version == 2);
        Assert.Equal(FinancialDocumentCause.Correction, correction.Cause);
        Assert.Equal(results[winner].Value.ReplacementDocumentId, correction.Id.Value);
        // No gap: the loser was holding number 3 when the index refused it, and its rollback gave it back.
        Assert.Equal("TEST-PAY-2026-000002", correction.Number);
        Assert.Equal(2L, await LastNumberAsync(holder, series, transaction: null));
        Assert.Equal(
            ["TEST-PAY-2026-000001", "TEST-PAY-2026-000002"],
            (await context.FinancialDocuments.Select(document => document.Number).ToListAsync())
                .Where(number => number.StartsWith(series, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// A receipt's correction brings the booking's statement one version (owner, 2026-09-28; pre-launch item
    /// 181), on PostgreSQL itself: the sweep's new predicate and the "being prepared" query translate and
    /// answer, and both count a RECEIPT's correction only — a statement's own correction never brings its
    /// booking back.
    /// </summary>
    [PostgresFact]
    public async Task A_receipts_correction_brings_the_statement_one_version_and_a_statements_own_does_not()
    {
        var harness = new IssuanceHarness((await FreshDatabaseAsync("corrected")).Options);
        var (booking, payment) = await harness.PaidAsync(PaymentProviders.Sandbox);
        await harness.PassAsync();
        var receipt = (await harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.PaymentReceipt);

        harness.Now = harness.Now.AddHours(1);
        var voided = await harness.VoidAsync(receipt.Id, "The office's name was wrong.");
        Assert.True(voided.IsSuccess, voided.IsFailure ? voided.Error.Code : null);
        await using (var reading = harness.NewContext())
        {
            var pending = Assert.Single(await new FinancialDocumentReader(reading).PendingForBookingAsync(booking.Id));
            Assert.Equal(FinancialDocumentType.BookingStatement, pending.Type);
            Assert.Equal(payment.AppliedAt, pending.OccurredAt);
        }

        // Past the late-commit margin, so the correction alone makes the statement a candidate.
        harness.Now = harness.Now.AddMinutes(30);
        Assert.Equal("TEST-STM-2026-000002", Assert.Single(await harness.PassAsync()).Number);
        var restated = (await harness.DocumentsAsync()).Single(document => document.Type == FinancialDocumentType.BookingStatement && document.Version == 2);
        Assert.Equal(FinancialDocumentCause.ReceiptCorrected, restated.Cause);
        Assert.Equal(payment.AppliedAt, restated.OccurredAt);
        harness.Now = harness.Now.AddMinutes(30);
        Assert.Empty(await harness.PassAsync());

        // The statement voided in its turn: its correction is issued with the void, and nothing follows it.
        harness.Now = harness.Now.AddHours(1);
        Assert.True((await harness.VoidAsync(restated.Id, "Wrong total.")).IsSuccess);
        harness.Now = harness.Now.AddMinutes(30);
        Assert.Empty(await harness.PassAsync());
        await using (var reading = harness.NewContext())
            Assert.Empty(await new FinancialDocumentReader(reading).PendingForBookingAsync(booking.Id));
        Assert.Equal(5, (await harness.DocumentsAsync()).Count);
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

    /// <summary>The series' last number; read under the row's lock when a transaction is given, as an issuer holds it.</summary>
    private static async Task<long> LastNumberAsync(NpgsqlConnection connection, string series, NpgsqlTransaction? transaction)
    {
        await using var command = new NpgsqlCommand(
            transaction is null
                ? "SELECT last_number FROM financial_document_series WHERE series_key = @series"
                : "SELECT last_number FROM financial_document_series WHERE series_key = @series FOR UPDATE",
            connection,
            transaction);
        command.Parameters.AddWithValue("series", series);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Returns once PostgreSQL itself reports both voids waiting on a lock inside the number series' statement
    /// — where a void stands once its checks have passed and its transaction has begun — with the holder at
    /// the head of that queue. (The second waiter queues behind the first, so only one is blocked by the
    /// holder directly.) A void that finishes first never waited, and the race would not have been forced:
    /// the failure then says how it ended, because a void refused or broken before its transaction is a
    /// different fault from a race that did not overlap.
    /// </summary>
    /// <remarks>
    /// Matched on the table's name rather than the statement's text, so a reformatted upsert cannot turn this
    /// into a timeout. Nothing else in the test waits on a lock with that name in its query: the holder sits
    /// idle in its transaction, pooled connections idle on the client, and the observer is left out by pid.
    /// </remarks>
    private static async Task BothWaitForTheNumberAsync(
        string connectionString,
        int holder,
        IReadOnlyList<Task<CSharpFunctionalExtensions.Result<VoidedFinancialDocumentDto, Error>>> voids)
    {
        await using var observer = new NpgsqlConnection(connectionString);
        await observer.OpenAsync();
        await using var waiting = new NpgsqlCommand(
            """
            SELECT count(*), count(*) FILTER (WHERE @holder = ANY (pg_blocking_pids(pid)))
            FROM pg_stat_activity
            WHERE datname = current_database()
              AND pid <> pg_backend_pid()
              AND wait_event_type = 'Lock'
              AND query LIKE '%financial_document_series%'
            """,
            observer);
        waiting.Parameters.AddWithValue("holder", holder);

        var (queued, behindHolder) = (0L, 0L);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (voids.FirstOrDefault(task => task.IsCompleted) is { } finished)
            {
                // Awaited, so a void that faulted fails the test with its own exception.
                var outcome = await finished;
                Assert.Fail(
                    $"A void finished ({(outcome.IsSuccess ? "voided" : outcome.Error.Code)}) before both reached the number series: the race was not forced.");
            }

            await using (var row = await waiting.ExecuteReaderAsync())
            {
                Assert.True(await row.ReadAsync());
                (queued, behindHolder) = (row.GetInt64(0), row.GetInt64(1));
            }

            if (queued == 2 && behindHolder >= 1)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        Assert.Fail($"In 30 seconds, {queued} of the two voids reached the number series ({behindHolder} behind the holder).");
    }

    /// <summary>
    /// Every unique index PostgreSQL refused a save on, by name: the failure <c>UnitOfWork</c> turns into the
    /// <c>UniqueConstraintConflictException</c> the void handler's race branch catches, seen before anything
    /// translates it.
    /// </summary>
    private sealed class UniqueViolationRecorder : SaveChangesInterceptor
    {
        private readonly ConcurrentQueue<string> _constraints = new();

        public IReadOnlyList<string> Constraints => [.. _constraints];

        public override void SaveChangesFailed(DbContextErrorEventData eventData)
        {
            Record(eventData);
            base.SaveChangesFailed(eventData);
        }

        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Record(eventData);
            return base.SaveChangesFailedAsync(eventData, cancellationToken);
        }

        private void Record(DbContextErrorEventData eventData)
        {
            if (eventData.Exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation })
                _constraints.Enqueue(violation.ConstraintName ?? "(unnamed)");
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
