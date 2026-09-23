using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Notifications;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The parts of push and reminders only PostgreSQL can answer: the outbox claim is raw SQL with
/// <c>FOR UPDATE SKIP LOCKED</c>, and the reminder and device queries must translate on Npgsql rather
/// than only on SQLite. Opt-in, like <see cref="PostgresConstraintTranslationTests"/>: set
/// <c>KHADRA_TEST_POSTGRES</c> to a scratch database. Rows carry unique ids; nothing is ever dropped.
/// </summary>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresOutboxAndReminderTests : IAsyncLifetime
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

        // The real migrations, including the four added for push, reminders and handovers.
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
    public async Task Two_dispatchers_claiming_at_once_never_take_the_same_delivery()
    {
        // Far in the past, so only these rows are due before "now" below, whatever earlier runs left.
        var at = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(Random.Shared.Next(1, 1_000_000));
        var owned = new List<Id>();
        await using (var context = NewContext())
        {
            for (var i = 0; i < 6; i++)
            {
                var notification = Notification.Raise(Id.New(), NotificationKind.YourBookingConfirmed, "Petra", at, Id.New(), "KH-PG");
                context.Notifications.Add(notification);
                var delivery = NotificationDelivery.Owe(notification.Id, NotificationChannel.Push, at);
                context.NotificationDeliveries.Add(delivery);
                owned.Add(delivery.Id);
            }
            await context.SaveChangesAsync();
        }

        await using var first = NewContext();
        await using var second = NewContext();
        await using var firstTx = await first.Database.BeginTransactionAsync();
        await using var secondTx = await second.Database.BeginTransactionAsync();

        // Inside two open transactions, so the first claim's row locks are still held when the second
        // one runs: SKIP LOCKED must pass over them rather than wait or take them.
        var a = await new NotificationDeliveryRepository(first).ClaimDueAsync(at.AddSeconds(1), TimeSpan.FromMinutes(2), 4);
        var b = await new NotificationDeliveryRepository(second).ClaimDueAsync(at.AddSeconds(1), TimeSpan.FromMinutes(2), 4);
        await firstTx.CommitAsync();
        await secondTx.CommitAsync();

        var mine = a.Concat(b).Where(d => owned.Contains(d.Id)).Select(d => d.Id).ToList();
        Assert.Equal(mine.Count, mine.Distinct().Count());
        Assert.All(a.Concat(b), d => Assert.Equal(1, d.Attempts));
    }

    [PostgresFact]
    public async Task A_claimed_row_is_leased_and_comes_back_after_the_lease()
    {
        var at = new DateTimeOffset(2002, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(Random.Shared.Next(1, 1_000_000));
        Id deliveryId;
        await using (var context = NewContext())
        {
            var notification = Notification.Raise(Id.New(), NotificationKind.YourBookingConfirmed, "Petra", at, Id.New(), "KH-PG");
            context.Notifications.Add(notification);
            var delivery = NotificationDelivery.Owe(notification.Id, NotificationChannel.Push, at);
            context.NotificationDeliveries.Add(delivery);
            deliveryId = delivery.Id;
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
        {
            var claimed = await new NotificationDeliveryRepository(context).ClaimDueAsync(at.AddSeconds(1), TimeSpan.FromMinutes(2), 500);
            Assert.Contains(claimed, d => d.Id == deliveryId);
        }

        await using (var context = NewContext())
        {
            var again = await new NotificationDeliveryRepository(context).ClaimDueAsync(at.AddMinutes(1), TimeSpan.FromMinutes(2), 500);
            Assert.DoesNotContain(again, d => d.Id == deliveryId);
        }

        await using var later = NewContext();
        var retried = await new NotificationDeliveryRepository(later).ClaimDueAsync(at.AddMinutes(3), TimeSpan.FromMinutes(2), 500);
        Assert.Equal(2, Assert.Single(retried, d => d.Id == deliveryId).Attempts);
    }

    [PostgresFact]
    public async Task The_reminder_queries_translate_and_answer_on_postgres()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var booking = Build.ConfirmedBooking(now: now);
        booking.ClearDomainEvents();
        await using (var context = NewContext())
        {
            context.Bookings.Add(booking);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var candidates = new ReminderCandidateReader(reader);
        var start = booking.Period.Start;

        Assert.Contains(await candidates.ListDueAsync(ReminderKind.Pickup, start.AddMinutes(-30), TimeSpan.FromHours(1)),
            c => c.BookingId == booking.Id && c.AnchorAt == start);
        Assert.DoesNotContain(await candidates.ListDueAsync(ReminderKind.Payment, start.AddMinutes(-30), TimeSpan.FromHours(1)),
            c => c.BookingId == booking.Id);

        reader.BookingReminders.Add(BookingReminder.Record(booking.Id, ReminderKind.Pickup, start, start.AddMinutes(-30)));
        await reader.SaveChangesAsync();
        Assert.DoesNotContain(await candidates.ListDueAsync(ReminderKind.Pickup, start.AddMinutes(-20), TimeSpan.FromHours(1)),
            c => c.BookingId == booking.Id);
    }

    [PostgresFact]
    public async Task Only_a_device_on_a_live_session_is_deliverable_on_postgres()
    {
        var suffix = Random.Shared.Next(1_000_000, 9_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var user = Build.Customer(email: $"pg-{Guid.NewGuid():N}@example.com", phone: $"079{suffix}");
        var now = DateTimeOffset.UtcNow;
        var session = RefreshToken.IssueNewFamily(user.Id, Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            now, TimeSpan.FromDays(14), TimeSpan.FromDays(30), null, null);
        var device = PushDevice.Register($"pg-token-{Guid.NewGuid():N}", PushPlatform.Android, user.Id, session.FamilyId, Language.Arabic, null, now);
        await using (var context = NewContext())
        {
            context.Users.Add(user);
            context.RefreshTokens.Add(session);
            context.PushDevices.Add(device);
            await context.SaveChangesAsync();
        }

        await using (var context = NewContext())
            Assert.Single(await new PushDeviceRepository(context).ListDeliverableAsync(user.Id, now.AddMinutes(1)));

        await using (var context = NewContext())
            await new RefreshTokenRepository(context).RevokeFamilyAsync(session.FamilyId, now.AddMinutes(2));

        await using var after = NewContext();
        Assert.Empty(await new PushDeviceRepository(after).ListDeliverableAsync(user.Id, now.AddMinutes(3)));
    }
}
