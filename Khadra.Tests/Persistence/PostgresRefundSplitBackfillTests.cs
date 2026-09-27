using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The migration that stored the refund split filled every refund recorded before it with the rule
/// <c>Payment.FeeFor</c> applies, written in SQL (<c>FinancialDocuments.RefundFeeRule</c>). This proves the
/// SQL and the C# agree: refunds are recorded through the aggregate for every reason, fee, refundability
/// and currency; the aggregate stores its split; and the migration's exact expression, run over the same
/// rows, gives the same fee part for every one. Opt-in, like the other PostgreSQL tests: set
/// <c>KHADRA_TEST_POSTGRES</c> to a scratch database. The database is reused, so every assertion is about
/// THIS test's rows.
/// </summary>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresRefundSplitBackfillTests : IAsyncLifetime
{
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

    [PostgresFact]
    public async Task The_migrations_SQL_gives_every_refund_the_split_the_aggregate_stores()
    {
        var recorded = RefundSplitCases.All()
            .Select(@case => RefundSplitCases.Record(@case.Reason, @case.Fee, @case.Refundable, Build.Now))
            .ToList();
        var otherCurrency = RefundSplitCases.Opened(RefundSplitCases.Fee, refundable: true, Build.Now);
        Assert.True(otherCurrency.Orphan(Money.Create(50m, "USD"), Build.Now, "booking.not_awaiting_payment", Build.Now).IsSuccess);
        var payments = recorded.Select(pair => pair.Payment).Append(otherCurrency).ToList();
        var stored = payments.SelectMany(payment => payment.Refunds).ToDictionary(refund => refund.Id.Value, refund => refund.FeePart.Amount);

        await using (var write = NewContext())
        {
            write.Payments.AddRange(payments);
            await write.SaveChangesAsync();
        }

        await using var read = NewContext();
        var connection = (NpgsqlConnection)read.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT r.id, r.fee_part, " + Khadra.Infrastructure.Persistence.Migrations.FinancialDocuments.RefundFeeRule + " AS rule_fee_part"
            + " FROM payment_refunds AS r JOIN payments AS p ON p.id = r.payment_id WHERE r.id = ANY(@ids)",
            connection);
        command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = stored.Keys.ToArray() });

        var compared = 0;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var id = reader.GetGuid(0);
                Assert.Equal(stored[id], reader.GetDecimal(1));
                Assert.Equal(stored[id], reader.GetDecimal(2));
                compared++;
            }
        }

        Assert.Equal(stored.Count, compared);
    }
}
