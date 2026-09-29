using Khadra.Application.Payments;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Phase 3's queries on the engine that runs them (owner, 2026-09-26). The deposit-release query is the
/// only one in the repository that FILTERS inside a JSON document — the penalty's party, through a
/// converter, and its amount — and a translation Npgsql refused would fail the booking pass every
/// minute. SQLite proves the meaning (<see cref="EndingRefundPersistenceTests"/>); this proves the SQL.
/// Opt-in, like <see cref="PostgresOutboxAndReminderTests"/>: set <c>KHADRA_TEST_POSTGRES</c> to a
/// scratch database. The database is reused, so every assertion is about THIS test's rows.
/// </summary>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresEndingRefundQueryTests : IAsyncLifetime
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

    private static DateTimeOffset AfterFreeWindow(Booking booking) => booking.FreeCancellationDeadline!.Value.AddMinutes(1);

    private static (Booking Booking, Payment Payment) CancelledBy(BookingParty party, bool inFull = false, BookingTerms? terms = null)
    {
        var (booking, payment) = Build.PaidBooking(inFull, terms: terms);
        var actor = party == BookingParty.Customer ? booking.CustomerId : Id.New();
        Assert.True(booking.Cancel(party, actor, "Ended.", AfterFreeWindow(booking)).IsSuccess);
        return (booking, payment);
    }

    [PostgresFact]
    public async Task The_release_candidates_translate_on_postgres_and_mean_what_they_mean_on_sqlite()
    {
        var (gallery, galleryPayment) = CancelledBy(BookingParty.Dealer);
        var (lenient, lenientPayment) = CancelledBy(BookingParty.Customer, terms: Build.Terms(customerPenaltyPercent: 0m));
        var (customer, customerPayment) = CancelledBy(BookingParty.Customer);
        var (disputed, disputedPayment) = CancelledBy(BookingParty.Dealer);
        var ticket = DisputeTicket.Open(disputed.Id, disputed.CustomerId, BookingParty.Customer, "No car.", TimeSpan.FromHours(48), AfterFreeWindow(disputed)).Value;
        var (released, releasedPayment) = CancelledBy(BookingParty.Dealer);
        Assert.NotNull(releasedPayment.RefundHeldDeposit(Money.Jod(18m), Now.AddDays(3)).Value);

        await using (var write = NewContext())
        {
            write.Bookings.AddRange(gallery, lenient, customer, disputed, released);
            write.Payments.AddRange(galleryPayment, lenientPayment, customerPayment, disputedPayment, releasedPayment);
            write.DisputeTickets.Add(ticket);
            await write.SaveChangesAsync();
        }

        await using var read = NewContext();
        var found = (await new BookingRepository(read).ListDueForDepositReleaseAsync(Now.AddDays(30)))
            .Select(booking => booking.Id)
            .ToHashSet();

        Assert.Contains(gallery.Id, found);
        Assert.Contains(lenient.Id, found);
        Assert.DoesNotContain(customer.Id, found);
        Assert.DoesNotContain(disputed.Id, found);
        Assert.DoesNotContain(released.Id, found);
        Assert.True(await new DisputeTicketRepository(read).HasClaimOnDepositAsync(disputed.Id));
    }

    [PostgresFact]
    public async Task The_safety_net_and_the_refund_list_translate_on_postgres()
    {
        var (missing, missingPayment) = CancelledBy(BookingParty.Customer, inFull: true);
        var (recorded, recordedPayment) = CancelledBy(BookingParty.Customer, inFull: true);
        Assert.NotNull(BookingEndingRefunds.Record(recorded, recordedPayment, AfterFreeWindow(recorded)));
        var (byAdmin, byAdminPayment) = CancelledBy(BookingParty.Admin);

        await using (var write = NewContext())
        {
            write.Bookings.AddRange(missing, recorded, byAdmin);
            write.Payments.AddRange(missingPayment, recordedPayment, byAdminPayment);
            await write.SaveChangesAsync();
        }

        await using var read = NewContext();
        var owed = (await new PaymentRepository(read).ListEndedWithoutEndingRefundAsync()).Select(payment => payment.Id).ToHashSet();
        Assert.Contains(missingPayment.Id, owed);
        Assert.Contains(byAdminPayment.Id, owed);
        Assert.DoesNotContain(recordedPayment.Id, owed);

        var context = await new BookingReader(read).ContextAsync(recorded.Id);
        var refund = Assert.Single(context.Refunds!);
        Assert.Equal("EndedBeforePickup", refund.Reason);
        Assert.Equal(72m, refund.Amount.Amount);
    }
}
