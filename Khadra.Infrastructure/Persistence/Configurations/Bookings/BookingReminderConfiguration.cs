using Khadra.Domain.Bookings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.Bookings;

internal sealed class BookingReminderConfiguration : IEntityTypeConfiguration<BookingReminder>
{
    public void Configure(EntityTypeBuilder<BookingReminder> entity)
    {
        ConfigureAggregate(entity, "booking_reminders");

        ConfigureId(entity.Property(reminder => reminder.BookingId)).IsRequired();
        ConfigureEnumeration(entity.Property(reminder => reminder.Kind), 10);
        entity.Property(reminder => reminder.AnchorAt).IsRequired();
        entity.Property(reminder => reminder.SentAt).IsRequired();

        // THE idempotency guarantee: one reminder per booking, per kind, per moment. Two sweeps racing
        // both try to insert and the database keeps exactly one. See BookingReminder.
        entity.HasIndex(reminder => new { reminder.BookingId, reminder.Kind, reminder.AnchorAt }).IsUnique();

        // Same context; Restrict, as everywhere.
        entity.HasOne<Booking>()
            .WithMany()
            .HasForeignKey(reminder => reminder.BookingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
