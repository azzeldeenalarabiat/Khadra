using Khadra.Application.Payables.Pass;
using Khadra.Application.Payables.Settlements;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments.Repositories;
using Khadra.Domain.Payables;
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
/// The office payables ledger on PostgreSQL itself (payments Phase 8, migration <c>20260929195432_OfficePayables</c>):
/// the tables, their money CHECKs and their triggers — a payable frozen but for its settlement, the rest append-only,
/// holds never deleted; two administrators settling one office at once, forced, which records one settlement and uses
/// one number; and the rollback, which refuses to run over real money. Opt-in: set <c>KHADRA_TEST_POSTGRES</c> to a
/// scratch database. Each scenario gets a fresh database of its own.
/// </summary>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresOfficePayablesTests
{
    private const string Previous = "20260929164711_FinancialDocumentDeliveries";

    [PostgresFact]
    public async Task The_ledgers_constraints_and_triggers_are_created_and_a_payable_is_frozen_but_for_its_settlement()
    {
        var database = await FreshDatabaseAsync("ledger");
        var harness = new PayablesHarness(database.Options);
        var booking = await CompletedAsync(harness, "71717");
        await harness.PassAsync();
        var payable = Assert.Single(await harness.PayablesAsync());
        Assert.Equal(12m, payable.Net);
        var settled = await harness.SettleAsync(booking.DealerId, PaymentProviders.Sandbox, 12m);
        Assert.True(settled.IsSuccess, settled.IsFailure ? settled.Error.Code : null);
        Assert.Equal("TEST-SET-2026-000001", settled.Value.Settlement.Number);

        await using var connection = await OpenAsync(database);
        Assert.Equal(
            [
                "ck_office_payable_holds_checks",
                "ck_office_payable_holds_manual",
                "ck_office_payable_holds_payable",
                "ck_office_payable_lines_amount",
                "ck_office_payable_lines_position",
                "ck_office_payables_calculator_version",
                "ck_office_payables_figures",
                "ck_office_payables_settled",
                "ck_office_settlements_direction",
            ],
            (await NamesAsync(connection, @"
SELECT conname FROM pg_constraint
 WHERE conrelid IN ('office_payables'::regclass, 'office_payable_lines'::regclass, 'office_settlements'::regclass,
                    'office_settlement_lines'::regclass, 'office_settlement_voids'::regclass, 'office_payable_holds'::regclass)
   AND contype = 'c'")).Order(StringComparer.Ordinal));
        Assert.Contains("ix_dispute_tickets_booking", await NamesAsync(connection, "SELECT indexname FROM pg_indexes WHERE tablename = 'dispute_tickets'"));

        // Only the settlement moves — by a void, which the application sends — and a figure, when it was settled, a
        // delete and a truncate never do.
        await ExecuteAsync(connection, "UPDATE office_payables SET updated_at = now()");
        await RefusedAsync(connection, "UPDATE office_payables SET settled_at = settled_at + interval '1 minute'", "never rewritten");
        await RefusedAsync(connection, "UPDATE office_payables SET net = 0", "figures are frozen");
        await RefusedAsync(connection, "UPDATE office_payables SET outcome = 'PenaltyKept'", "figures are frozen");
        await RefusedAsync(connection, "UPDATE office_payables SET settlement_id = gen_random_uuid()", "only when that settlement is voided");
        await RefusedAsync(connection, "DELETE FROM office_payables", "DELETE is not permitted");
        await RefusedAsync(connection, "TRUNCATE office_payables CASCADE", "is append-only");
        foreach (var table in new[] { "office_payable_lines", "office_settlements", "office_settlement_lines" })
        {
            await RefusedAsync(connection, $"UPDATE {table} SET id = id", "is append-only");
            await RefusedAsync(connection, $"DELETE FROM {table}", "is append-only");
            await RefusedAsync(connection, $"TRUNCATE {table} CASCADE", "is append-only");
        }

        // The money CHECKs: a net that is not the figures, a line of nothing, a sign that is not the direction.
        await CheckRefusedAsync(connection, "ck_office_payables_figures", $@"
INSERT INTO office_payables (id, booking_id, dealer_id, booking_reference, currency, provider, outcome, final_at, recorded_at,
                             office_money, commission, office_charges, net, calculator_version)
VALUES (gen_random_uuid(), gen_random_uuid(), '{booking.DealerId.Value}', 'KH-CHECKNET', 'JOD', 'SANDBOX', 'Rental', now(), now(),
        18, 6, 0, 13, 2)");
        await CheckRefusedAsync(connection, "ck_office_payables_figures", @"
INSERT INTO office_payables (id, booking_id, dealer_id, booking_reference, currency, provider, outcome, final_at, recorded_at,
                             office_money, commission, office_charges, net, calculator_version)
VALUES (gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), 'KH-CHECKCOM', 'JOD', 'SANDBOX', 'Rental', now(), now(),
        4, 6, 0, -2, 2)");
        await CheckRefusedAsync(connection, "ck_office_payable_lines_amount", $@"
INSERT INTO office_payable_lines (id, payable_id, position, kind, amount) VALUES (gen_random_uuid(), '{payable.Id.Value}', 9, 'RentalRevenue', 0)");
        await CheckRefusedAsync(connection, "ck_office_settlements_direction", $@"
INSERT INTO office_settlements (id, settlement_number, dealer_id, currency, provider, direction, amount, paid_on, recorded_by_admin_id, recorded_at)
VALUES (gen_random_uuid(), 'TEST-SET-2026-999999', '{booking.DealerId.Value}', 'JOD', 'SANDBOX', 'Payout', -1, current_date, gen_random_uuid(), now())");

        // A void is append-only too, and a hold is never deleted.
        var voided = await harness.VoidAsync(Id.From(settled.Value.Settlement.SettlementId), "Wrong office.");
        Assert.True(voided.IsSuccess, voided.IsFailure ? voided.Error.Code : null);
        await RefusedAsync(connection, "UPDATE office_settlement_voids SET reason = 'rewritten'", "is append-only");
        await RefusedAsync(connection, "DELETE FROM office_settlement_voids", "is append-only");
        Assert.True((await harness.HoldAsync(payable.Id, "Bank details unconfirmed.")).IsSuccess);
        Assert.True((await harness.ReleaseAsync(payable.Id)).IsSuccess);
        await RefusedAsync(connection, "DELETE FROM office_payable_holds", "keeps every hold");
        await RefusedAsync(connection, "TRUNCATE office_payable_holds", "keeps every hold");
    }

    [PostgresFact]
    public async Task Two_administrators_settling_one_office_at_once_record_one_settlement_and_use_one_number()
    {
        var database = await FreshDatabaseAsync("race");
        var harness = new PayablesHarness(database.Options);
        var booking = await CompletedAsync(harness, "72727");
        await harness.PassAsync();

        // Forced, not hoped for: the second administrator's settlement has found the payable due and is about to take
        // its number when the first administrator's settlement commits.
        CSharpFunctionalExtensions.Result<Khadra.Application.Payables.Dtos.OfficeSettlementDetailDto, Error>? first = null;
        await using var context = harness.NewContext();
        var handler = SettleHandler(harness, context, new InterleavedSeries(
            new FinancialDocumentSeriesCounter(context),
            async () => first = await harness.SettleAsync(booking.DealerId, PaymentProviders.Sandbox, 12m)));

        var second = await handler.Handle(Settle(harness, booking, 12m), CancellationToken.None);

        Assert.True(first!.Value.IsSuccess);
        Assert.Equal("TEST-SET-2026-000001", first.Value.Value.Settlement.Number);
        // Checked again while holding the series row that serialises settlements: nothing is due any more.
        Assert.Equal("payables.balance_changed", second.Error.Code);
        Assert.Equal(0m, Assert.IsType<Dictionary<string, object?>>(second.Error.Extensions!["currentAmount"])["amount"]);
        await using var check = harness.NewContext();
        Assert.Single(await check.OfficeSettlements.AsNoTracking().ToListAsync());
        // The loser's number went back with its transaction: the series stays gapless.
        Assert.Equal(1, (await check.FinancialDocumentSeries.AsNoTracking().SingleAsync(row => row.SeriesKey == "TEST-SET-2026")).LastNumber);
        Assert.Single(await harness.AuditAsync());
    }

    [PostgresFact]
    public async Task A_payable_changed_since_it_was_read_is_never_saved_over()
    {
        var database = await FreshDatabaseAsync("xmin");
        var harness = new PayablesHarness(database.Options);
        var booking = await CompletedAsync(harness, "74747");
        await harness.PassAsync();
        var payableId = Assert.Single(await harness.PayablesAsync()).Id;

        // Two readers of the same open payable — settlements in different series (TEST and real money never share a
        // payable, but a year's turn does), or a settlement beside a void — and the other one saves first.
        await using var stale = harness.NewContext();
        var staleCopy = (await new OfficePayableRepository(stale).GetAsync(payableId))!;
        var settled = await harness.SettleAsync(booking.DealerId, PaymentProviders.Sandbox, 12m);
        Assert.True(settled.IsSuccess, settled.IsFailure ? settled.Error.Code : null);

        Assert.True(staleCopy.SettleUnder(Id.From(settled.Value.Settlement.SettlementId), harness.Now).IsSuccess);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => IssuanceHarness.UnitOfWork(stale).SaveChangesAsync());
    }

    [PostgresFact]
    public async Task A_payable_held_while_a_settlement_is_being_recorded_stops_it_and_its_number_goes_back()
    {
        var database = await FreshDatabaseAsync("heldrace");
        var harness = new PayablesHarness(database.Options);
        var booking = await CompletedAsync(harness, "73737");
        await harness.PassAsync();
        var payable = Assert.Single(await harness.PayablesAsync());

        // Forced: the settlement found the payable due, and another administrator holds it before the number is taken.
        await using var context = harness.NewContext();
        var handler = SettleHandler(harness, context, new InterleavedSeries(
            new FinancialDocumentSeriesCounter(context),
            async () => Assert.True((await harness.HoldAsync(payable.Id, "Held while it was being settled.")).IsSuccess)));

        var result = await handler.Handle(Settle(harness, booking, 12m), CancellationToken.None);

        Assert.Equal("payables.balance_changed", result.Error.Code);
        var current = Assert.IsType<Dictionary<string, object?>>(result.Error.Extensions!["currentAmount"]);
        Assert.Equal(0m, current["amount"]);
        await using var check = harness.NewContext();
        Assert.Empty(await check.OfficeSettlements.AsNoTracking().ToListAsync());
        Assert.Null((await check.OfficePayables.AsNoTracking().SingleAsync()).SettlementId);
        // The number went back with the transaction: the series was never started.
        Assert.False(await check.FinancialDocumentSeries.AsNoTracking().AnyAsync(row => row.SeriesKey == "TEST-SET-2026"));
    }

    [PostgresFact]
    public async Task The_rollback_refuses_to_run_over_real_money_and_runs_over_test_money()
    {
        var real = await FreshDatabaseAsync("realpayable");
        await using (var context = new KhadraDbContext(real.Options))
        {
            context.OfficePayables.Add(Payable("Tap"));
            await context.SaveChangesAsync();
        }

        await using (var context = new KhadraDbContext(real.Options))
        {
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => context.GetService<IMigrator>().MigrateAsync(Previous));
            Assert.Contains("fixed forward", refusal.MessageText, StringComparison.Ordinal);
        }

        var test = await FreshDatabaseAsync("testpayable");
        await using (var context = new KhadraDbContext(test.Options))
        {
            context.OfficePayables.Add(Payable(PaymentProviders.Sandbox));
            await context.SaveChangesAsync();
            await context.GetService<IMigrator>().MigrateAsync(Previous);
        }

        await using var connection = await OpenAsync(test);
        await using var gone = new NpgsqlCommand(@"
SELECT to_regclass('office_payables') IS NULL AND to_regclass('office_settlements') IS NULL
   AND to_regclass('office_payable_holds') IS NULL AND to_regclass('ix_dispute_tickets_booking') IS NULL
   AND NOT EXISTS (SELECT 1 FROM pg_proc WHERE proname IN ('khadra_office_payable_is_frozen', 'khadra_office_payable_hold_is_kept'))
   AND EXISTS (SELECT 1 FROM pg_proc WHERE proname = 'khadra_table_is_append_only')", connection);
        Assert.True((bool)(await gone.ExecuteScalarAsync())!);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>A paid booking, completed when its window closed; the harness's clock just past the margin.</summary>
    private static async Task<Booking> CompletedAsync(PayablesHarness harness, string unique)
    {
        harness.Bookings.Now = harness.Now;
        var (booking, _) = await harness.Bookings.PaidAsync(PaymentProviders.Sandbox, unique: unique);
        Booking? completed = null;
        await harness.Bookings.ChangeAsync(async context =>
        {
            completed = (await new BookingRepository(context).GetByIdAsync(booking.Id))!;
            Assert.True(completed.RecordPickup(BookingParty.Dealer, Id.New(), completed.Period.Start).IsSuccess);
            Assert.True(completed.RecordReturn(BookingParty.Dealer, Id.New(), completed.Period.End).IsSuccess);
            Assert.True(completed.Settle(completed.DisputeWindowEndsAt!.Value, hasOpenDispute: false).IsSuccess);
        });
        harness.Now = completed!.FinishedAt!.Value.AddMinutes(11);
        return completed;
    }

    private static OfficePayable Payable(string provider) =>
        OfficePayable.Record(
            new PayableDraft(
                Id.New(), Id.New(), "KH-ROLLBACK", "JOD", provider, PayableOutcome.Rental, Build.Now, 2,
                [new PayableLineDraft(PayableLineKind.RentalRevenue, 18m), new PayableLineDraft(PayableLineKind.Commission, 6m)]),
            Build.Now);

    private static RecordOfficeSettlementHandler SettleHandler(PayablesHarness harness, KhadraDbContext context, IFinancialDocumentSeries series) =>
        new(
            new OfficeLedgerReader(context),
            new OfficePayableRepository(context),
            new OfficePayableHoldRepository(context),
            new OfficeSettlementRepository(context),
            new FinancialDocumentFactsReader(new BookingRepository(context), new PaymentRepository(context), new DisputeTicketRepository(context), context),
            series,
            new Khadra.Application.Auditing.AdminActionRecorder(
                new AuditTrail(context), NSubstitute.Substitute.For<Khadra.Application.Common.ICurrentActor>(), new TestClock(harness.Now)),
            new DealerRepository(context),
            new Khadra.Application.Notifications.DealerTeamNotifier(new Notifier(context), new UserRepository(context)),
            IssuanceHarness.UnitOfWork(context),
            DocumentFixtures.Amman,
            new TestClock(harness.Now),
            new RecordingLogger<RecordOfficeSettlementHandler>());

    private static RecordOfficeSettlementCommand Settle(PayablesHarness harness, Booking booking, decimal expected) =>
        new(booking.DealerId, "JOD", PaymentProviders.Sandbox, expected, Build.AmmanDate(harness.Now), null, null, harness.AdminId);

    /// <summary>Runs <paramref name="first"/> to completion the moment the wrapped settlement asks for its number.</summary>
    private sealed class InterleavedSeries(IFinancialDocumentSeries inner, Func<Task> first) : IFinancialDocumentSeries
    {
        public async Task<long> TakeNextAsync(string seriesKey, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            await first();
            return await inner.TakeNextAsync(seriesKey, now, cancellationToken);
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

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
