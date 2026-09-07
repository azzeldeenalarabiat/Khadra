using Khadra.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.Notifications;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> entity)
    {
        ConfigureAggregate(entity, "notifications");

        ConfigureId(entity.Property(notification => notification.RecipientUserId));
        entity.Property(notification => notification.RecipientUserId).IsRequired();
        ConfigureEnumeration(entity.Property(notification => notification.Kind), 40);
        ConfigureId(entity.Property(notification => notification.SubjectId));
        entity.Property(notification => notification.SubjectReference)
            .HasMaxLength(Notification.MaxReferenceLength);
        ConfigureId(entity.Property(notification => notification.ActorUserId));
        entity.Property(notification => notification.ActorName)
            .HasMaxLength(Notification.MaxActorNameLength)
            .IsRequired();
        entity.Property(notification => notification.OccurredAt).IsRequired();
        entity.Property(notification => notification.ReadAt);
        entity.Property(notification => notification.IsDeleted).IsRequired();
        entity.Property(notification => notification.DeletedAt);
        entity.HasQueryFilter(notification => !notification.IsDeleted);

        // Every read is "this person's, newest first", and the bell asks for the unread count on
        // every navigation. Both are served by one index, added now while the table is empty.
        //
        // It TRAILS the sort key and ends in Id because the ORDER BY does: occurred_at alone is not
        // unique, and a non-total order lets a page boundary drop a row — the defect the audit log
        // documents and orders (occurred_at DESC, id DESC) to avoid.
        entity.HasIndex(notification => new
        {
            notification.RecipientUserId,
            notification.OccurredAt,
            notification.Id
        }).IsDescending(false, true, true);

        // The unread count is a filtered read, so it gets a partial index rather than counting over
        // everything this person has ever been told.
        entity.HasIndex(notification => new { notification.RecipientUserId, notification.ReadAt })
            .HasFilter("read_at IS NULL");
    }
}
