using Khadra.Application.Bookings;
using Khadra.Application.Bookings.SettleBookings;
using Khadra.Application.Common;
using Khadra.Application.Notifications;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Notifications;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Npgsql;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The settlement sweep on the engine whose concurrency token makes it lose races (Wave 4, checklist 233).
/// </summary>
/// <remarks>
/// <para>
/// The pass used to load every candidate together and save each in turn on one tracker. A conflict on one booking
/// left it tracked with its stale token, so every LATER save in the pass re-issued it and failed too — and the
/// conflicted booking's staged notifications sat in the tracker, waiting to go out with somebody else's save. Only
/// PostgreSQL has the token (<c>xmin</c>), so only here can the conflict be real.
/// </para>
/// <para>
/// The race is made deterministic at the one moment it matters: while the sweep is loading the first booking, another
/// writer touches its row. Everything else is the real handler over the real repositories. A fresh database per run,
/// so the sweep sees only this test's bookings.
/// </para>
/// </remarks>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresSettlementSweepTests
{
    [PostgresFact]
    public async Task A_conflict_on_one_booking_costs_only_that_booking_and_its_staged_notifications()
    {
        var options = await FreshDatabaseAsync("sweep");
        var contested = Build.Booking(Build.Now);
        var quiet = Build.Booking(Build.Now);
        contested.ClearDomainEvents();
        quiet.ClearDomainEvents();
        await using (var seed = new KhadraDbContext(options))
        {
            seed.Bookings.AddRange(contested, quiet);
            await seed.SaveChangesAsync();
        }

        var clock = new TestClock((contested.DecisionDeadline > quiet.DecisionDeadline ? contested.DecisionDeadline : quiet.DecisionDeadline).AddMinutes(1));
        await using var sweep = new KhadraDbContext(options);
        var team = new DealerTeamNotifier(new Notifier(sweep), new UserRepository(sweep));

        // The other writer: while the sweep loads the contested booking — the moment its expiry is announced — the row
        // changes underneath it, as a dealer's request or another process would change it.
        var dealers = Substitute.For<IDealerRepository>();
        dealers.GetByIdAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            if (call.Arg<Id>() == contested.DealerId)
            {
                await using var other = new NpgsqlConnection(sweep.Database.GetConnectionString());
                await other.OpenAsync();
                await using var touch = new NpgsqlCommand("UPDATE bookings SET status = status WHERE id = @id", other);
                touch.Parameters.AddWithValue("id", contested.Id.Value);
                Assert.Equal(1, await touch.ExecuteNonQueryAsync());
            }

            return (Dealer?)null;
        });

        var handler = new SettleDueBookingsHandler(
            new SettlingBookingRepository(new BookingRepository(sweep), clock, new BookingExpiryAnnouncer(team, dealers)),
            new PaymentRepository(sweep),
            new DisputeTicketRepository(sweep),
            new DealerRepository(sweep),
            team,
            clock,
            new UnitOfWork(sweep, Substitute.For<IDomainEventDispatcher>()),
            new RepeatedFailureLog(),
            NullLogger<SettleDueBookingsHandler>.Instance);

        var report = (await handler.Handle(new SettleDueBookingsCommand(), CancellationToken.None)).Value;

        Assert.Equal(1, report.ExpiredUnanswered);
        Assert.Equal(1, report.Failed);

        await using var read = new KhadraDbContext(options);
        // The contested booking is left for the next pass, untouched; the other is expired and saved after it.
        Assert.Same(BookingStatus.Requested, (await read.Bookings.AsNoTracking().SingleAsync(row => row.Id == contested.Id)).Status);
        Assert.Same(BookingStatus.Expired, (await read.Bookings.AsNoTracking().SingleAsync(row => row.Id == quiet.Id)).Status);
        // And the contested booking's staged announcement went with its failed save, never out with the next one.
        var told = await read.Notifications.AsNoTracking().ToListAsync();
        var only = Assert.Single(told);
        Assert.Equal(quiet.Id, only.SubjectId);
        Assert.Same(NotificationKind.YourBookingExpired, only.Kind);

        // The next pass finds it still due, and settles it.
        var again = (await new SettleDueBookingsHandler(
                new SettlingBookingRepository(new BookingRepository(read), clock, new BookingExpiryAnnouncer(new DealerTeamNotifier(new Notifier(read), new UserRepository(read)), new DealerRepository(read))),
                new PaymentRepository(read),
                new DisputeTicketRepository(read),
                new DealerRepository(read),
                new DealerTeamNotifier(new Notifier(read), new UserRepository(read)),
                clock,
                new UnitOfWork(read, Substitute.For<IDomainEventDispatcher>()),
                new RepeatedFailureLog(),
                NullLogger<SettleDueBookingsHandler>.Instance)
            .Handle(new SettleDueBookingsCommand(), CancellationToken.None)).Value;
        Assert.Equal((1, 0), (again.ExpiredUnanswered, again.Failed));
    }

    /// <summary>A database nothing else has used, named after the configured scratch one. It is never dropped.</summary>
    private static async Task<DbContextOptions<KhadraDbContext>> FreshDatabaseAsync(string scenario)
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

        var options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseNpgsql(fresh.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new KhadraDbContext(options);
        await context.Database.MigrateAsync();
        return options;
    }
}
