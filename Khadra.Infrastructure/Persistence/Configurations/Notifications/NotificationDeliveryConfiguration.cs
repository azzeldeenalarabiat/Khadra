using Khadra.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.Notifications;

internal sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> entity)
    {
        ConfigureAggregate(entity, "notification_deliveries");

        ConfigureId(entity.Property(delivery => delivery.NotificationId)).IsRequired();
        ConfigureEnumeration(entity.Property(delivery => delivery.Channel), 10);
        ConfigureEnumeration(entity.Property(delivery => delivery.State), 10);
        entity.Property(delivery => delivery.Attempts).IsRequired();
        entity.Property(delivery => delivery.NextAttemptAt).IsRequired();
        entity.Property(delivery => delivery.CreatedAt).IsRequired();
        entity.Property(delivery => delivery.CompletedAt);
        entity.Property(delivery => delivery.LastError).HasMaxLength(NotificationDelivery.MaxErrorLength);

        // One delivery per notification per channel: a notification raised once is owed once.
        entity.HasIndex(delivery => new { delivery.NotificationId, delivery.Channel }).IsUnique();

        // The dispatcher's question, and only pending rows ever answer it: a partial index keeps it
        // the size of the backlog rather than of history.
        entity.HasIndex(delivery => delivery.NextAttemptAt)
            .HasFilter("state = 'Pending'")
            .HasDatabaseName("ix_notification_deliveries_pending_due");

        // Same context; Restrict, as everywhere.
        entity.HasOne<Notification>()
            .WithMany()
            .HasForeignKey(delivery => delivery.NotificationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
