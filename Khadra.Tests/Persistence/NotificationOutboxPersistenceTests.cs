using Khadra.Domain.Common;
using Khadra.Domain.Notifications;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The outbox is written by the notifier in the same SaveChanges as the notification, and claimed with
/// a lease so a delivery is worked by one dispatcher at a time and comes back if that one dies.
/// </summary>
public sealed class NotificationOutboxPersistenceTests : IDisposable
{
    private static readonly DateTimeOffset Now = Users.Now;
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public NotificationOutboxPersistenceTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new KhadraDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private KhadraDbContext NewContext() => new(_options);

    private async Task<Notification> RaiseAsync(NotificationKind kind, DateTimeOffset? dueAt = null)
    {
        var notification = Notification.Raise(Id.New(), kind, "Petra Rentals", Now, Id.New(), "KH-24-0007", dueAt: dueAt);
        await using var context = NewContext();
        new Notifier(context).Raise(notification);
        await context.SaveChangesAsync();
        return notification;
    }

    [Fact]
    public async Task A_customer_kind_is_owed_a_push_in_the_same_save()
    {
        var due = Now.AddHours(2);
        var notification = await RaiseAsync(NotificationKind.YourBookingApproved, due);

        await using var context = NewContext();
        var delivery = await context.NotificationDeliveries.SingleAsync();
        Assert.Equal(notification.Id, delivery.NotificationId);
        Assert.Same(NotificationChannel.Push, delivery.Channel);
        Assert.Same(DeliveryState.Pending, delivery.State);
        Assert.Equal(due, (await context.Notifications.SingleAsync()).DueAt);
    }

    [Fact]
    public async Task A_staff_kind_is_owed_nothing()
    {
        await RaiseAsync(NotificationKind.BookingApproved);

        await using var context = NewContext();
        Assert.Empty(await context.NotificationDeliveries.ToListAsync());
        Assert.Single(await context.Notifications.ToListAsync());
    }

    [Fact]
    public async Task A_claim_leases_the_row_so_a_second_dispatcher_cannot_take_it()
    {
        await RaiseAsync(NotificationKind.YourBookingConfirmed);

        await using (var first = NewContext())
        {
            var claimed = await new NotificationDeliveryRepository(first).ClaimDueAsync(Now, TimeSpan.FromMinutes(2), 10);
            var row = Assert.Single(claimed);
            Assert.Equal(1, row.Attempts);
            Assert.Equal(Now.AddMinutes(2), row.NextAttemptAt);
        }

        await using (var second = NewContext())
            Assert.Empty(await new NotificationDeliveryRepository(second).ClaimDueAsync(Now.AddMinutes(1), TimeSpan.FromMinutes(2), 10));

        // The first dispatcher died holding it: once the lease runs out, it comes back.
        await using var third = NewContext();
        var retried = Assert.Single(await new NotificationDeliveryRepository(third).ClaimDueAsync(Now.AddMinutes(3), TimeSpan.FromMinutes(2), 10));
        Assert.Equal(2, retried.Attempts);
    }

    [Fact]
    public async Task A_finished_delivery_is_never_claimed_again()
    {
        await RaiseAsync(NotificationKind.YourBookingRejected);

        await using (var context = NewContext())
        {
            var row = Assert.Single(await new NotificationDeliveryRepository(context).ClaimDueAsync(Now, TimeSpan.FromMinutes(2), 10));
            row.RecordSent(Now);
            await context.SaveChangesAsync();
        }

        await using var later = NewContext();
        Assert.Empty(await new NotificationDeliveryRepository(later).ClaimDueAsync(Now.AddDays(1), TimeSpan.FromMinutes(2), 10));
    }

    [Fact]
    public async Task A_claim_takes_no_more_than_the_batch()
    {
        for (var i = 0; i < 5; i++)
            await RaiseAsync(NotificationKind.YourBookingExpired);

        await using var context = NewContext();
        Assert.Equal(3, (await new NotificationDeliveryRepository(context).ClaimDueAsync(Now, TimeSpan.FromMinutes(2), 3)).Count);
    }

    [Fact]
    public async Task The_same_notification_cannot_be_owed_twice_on_one_channel()
    {
        var notification = await RaiseAsync(NotificationKind.YourBookingApproved);

        await using var context = NewContext();
        context.NotificationDeliveries.Add(NotificationDelivery.Owe(notification.Id, NotificationChannel.Push, Now));
        await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
    }
}
