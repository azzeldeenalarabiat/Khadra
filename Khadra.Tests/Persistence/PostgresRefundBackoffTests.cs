using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Npgsql;

namespace Khadra.Tests.Persistence;

/// <summary>
/// A refused refund's schedule, and the row it lives on, on the engine that runs them (Wave 4, B4; checklist 157).
/// </summary>
/// <remarks>
/// <para>
/// <b>The token.</b> A refund is its own row, so the payment's concurrency token never moved when only a refund
/// changed: the sweep marking a refund Sent from what it loaded could overwrite a Settled the webhook had written a
/// moment before — last writer wins, with money on the row. <c>xmin</c> on <c>payment_refunds</c> is what refuses
/// that save, and only PostgreSQL has it.
/// </para>
/// <para>
/// <b>The query.</b> "Due" compares a nullable instant with now inside an <c>Any</c> over the refunds; this proves the
/// SQL Npgsql writes for it means what the SQLite tests say it means. Opt-in: set <c>KHADRA_TEST_POSTGRES</c> to a
/// scratch database. It is reused, so every assertion is about THIS test's rows.
/// </para>
/// </remarks>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresRefundBackoffTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = Build.Now;
    private DbContextOptions<KhadraDbContext>? _options;

    public async Task InitializeAsync()
    {
        var connectionString = PostgresTestDatabase.ConnectionString;
        if (connectionString is null) return;

        await EnsureDatabaseExistsAsync(connectionString);
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var context = new KhadraDbContext(_options);
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private KhadraDbContext NewContext() => new(_options
        ?? throw new InvalidOperationException($"{PostgresTestDatabase.VariableName} is not set."));

    private static async Task EnsureDatabaseExistsAsync(string connectionString)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        var maintenance = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();
        await using var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection);
        exists.Parameters.AddWithValue("name", target.Database!);
        if (await exists.ExecuteScalarAsync() is not null) return;
        await using var create = new NpgsqlCommand(
            $"CREATE DATABASE \"{target.Database!.Replace("\"", "\"\"", StringComparison.Ordinal)}\"", connection);
        await create.ExecuteNonQueryAsync();
    }

    /// <summary>An orphaned capture's refund, owed and never sent, under this run's own session reference.</summary>
    private static Payment Owed(DateTimeOffset createdAt)
    {
        var payment = Payment.Open(Id.New(), Id.New(), Money.Jod(18m), "PGREFUND", createdAt.AddMinutes(30), createdAt);
        var reference = PostgresTestDatabase.Unique("sess_refund");
        Assert.True(payment.AttachProviderSession(reference, $"https://provider.test/{reference}").IsSuccess);
        Assert.True(payment.Orphan(Money.Jod(18m), createdAt, "BookingExpired", createdAt).IsSuccess);
        return payment;
    }

    /// <summary>
    /// The sweep loaded a refund to send it; the webhook settled it meanwhile. The sweep's save is refused by the
    /// refund's own token, and the refund stays Settled.
    /// </summary>
    [PostgresFact]
    public async Task A_refund_settled_while_the_sweep_held_it_stays_settled()
    {
        var payment = Owed(Now);
        await using (var seed = NewContext())
        {
            seed.Payments.Add(payment);
            await seed.SaveChangesAsync();
        }

        await using var sweep = NewContext();
        var held = await new PaymentRepository(sweep).GetByIdAsync(payment.Id);
        var heldRefund = Assert.Single(held!.Refunds);

        await using (var webhook = NewContext())
        {
            var settling = await new PaymentRepository(webhook).GetByIdAsync(payment.Id);
            var refund = Assert.Single(settling!.Refunds);
            refund.MarkSent("rf_webhook", Now.AddMinutes(1));
            refund.MarkSettled(Now.AddMinutes(2));
            await webhook.SaveChangesAsync();
        }

        heldRefund.MarkSent("rf_sweep", Now.AddMinutes(3));
        var unitOfWork = new UnitOfWork(sweep, Substitute.For<IDomainEventDispatcher>());
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => unitOfWork.SaveChangesAsync());

        await using var read = NewContext();
        var stored = Assert.Single((await new PaymentRepository(read).GetByIdAsync(payment.Id))!.Refunds);
        Assert.Same(RefundStatus.Settled, stored.Status);
        Assert.Equal("rf_webhook", stored.ProviderReference);
    }

    [PostgresFact]
    public async Task The_due_query_and_the_schedule_columns_translate_on_postgres()
    {
        var requested = Owed(Now.AddMinutes(-10));
        var waiting = Owed(Now.AddMinutes(-9));
        waiting.Refunds.Single().RecordRefusedSend("card_closed", Now, TestPayments.RetryPolicy);
        var waited = Owed(Now.AddMinutes(-8));
        waited.Refunds.Single().RecordRefusedSend("card_closed", Now.AddMinutes(-1), TestPayments.RetryPolicy);
        var sent = Owed(Now.AddMinutes(-7));
        sent.Refunds.Single().MarkSent("rf_sent", Now);
        await using (var seed = NewContext())
        {
            seed.Payments.AddRange(requested, waiting, waited, sent);
            await seed.SaveChangesAsync();
        }

        await using var read = NewContext();
        var repository = new PaymentRepository(read);
        var due = await repository.ListIdsWithRefundsDueAsync(Now);

        Assert.Contains(requested.Id, due);
        Assert.Contains(waited.Id, due);
        Assert.DoesNotContain(waiting.Id, due);
        Assert.DoesNotContain(sent.Id, due);
        Assert.Contains(waiting.Id, await repository.ListIdsWithRefundsDueAsync(Now.AddMinutes(1)));

        var stored = Assert.Single((await repository.GetByIdAsync(waiting.Id))!.Refunds);
        Assert.Equal(1, stored.RefusalCount);
        Assert.Equal(Now.AddMinutes(1), stored.NextAttemptAt);
    }
}
