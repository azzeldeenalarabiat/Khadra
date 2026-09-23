using Khadra.Domain.Common;
using Khadra.Domain.Notifications;

namespace Khadra.Tests.Domain.Notifications;

/// <summary>One outbox row: owed, then sent, skipped, retried or given up — and the first outcome wins.</summary>
public sealed class NotificationDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_delivery_is_pending_and_due_at_once()
    {
        var delivery = NotificationDelivery.Owe(Id.New(), NotificationChannel.Push, Now);

        Assert.Same(DeliveryState.Pending, delivery.State);
        Assert.Equal(Now, delivery.NextAttemptAt);
        Assert.Equal(0, delivery.Attempts);
        Assert.Null(delivery.CompletedAt);
    }

    [Fact]
    public void A_failure_before_the_limit_is_scheduled_again()
    {
        var delivery = NotificationDelivery.Owe(Id.New(), NotificationChannel.Email, Now);

        delivery.RecordFailure("timeout", maxAttempts: 3, retryAt: Now.AddMinutes(1), now: Now);

        Assert.Same(DeliveryState.Pending, delivery.State);
        Assert.Equal(Now.AddMinutes(1), delivery.NextAttemptAt);
        Assert.Equal("timeout", delivery.LastError);
    }

    [Fact]
    public void Sent_is_final_and_a_late_failure_cannot_undo_it()
    {
        var delivery = NotificationDelivery.Owe(Id.New(), NotificationChannel.Push, Now);

        delivery.RecordSent(Now);
        delivery.RecordFailure("late", maxAttempts: 0, retryAt: Now, now: Now.AddSeconds(1));
        delivery.RecordSkipped("late", Now.AddSeconds(2));

        Assert.Same(DeliveryState.Sent, delivery.State);
        Assert.Equal(Now, delivery.CompletedAt);
        Assert.Null(delivery.LastError);
    }

    [Fact]
    public void An_error_is_bounded_so_a_provider_cannot_fill_the_column()
    {
        var delivery = NotificationDelivery.Owe(Id.New(), NotificationChannel.Push, Now);

        delivery.RecordSkipped(new string('x', 5000), Now);

        Assert.Equal(NotificationDelivery.MaxErrorLength, delivery.LastError!.Length);
    }

    [Fact]
    public void Only_the_customers_own_kinds_wake_a_phone()
    {
        foreach (var kind in Enumeration.GetAll<NotificationKind>())
        {
            var wakes = kind.DeliveredOn().Contains(NotificationChannel.Push);
            Assert.Equal(kind.Name.StartsWith("Your", StringComparison.Ordinal), wakes);
        }
    }

    [Fact]
    public void No_delivery_without_a_notification()
    {
        Assert.Throws<DomainException>(() => NotificationDelivery.Owe(Id.Empty, NotificationChannel.Push, Now));
    }
}
