using Khadra.Domain.Bookings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.Bookings;

internal sealed class HandoverCodeConfiguration : IEntityTypeConfiguration<HandoverCode>
{
    public void Configure(EntityTypeBuilder<HandoverCode> entity)
    {
        ConfigureAggregate(entity, "handover_codes");

        ConfigureId(entity.Property(code => code.BookingId)).IsRequired();
        ConfigureEnumeration(entity.Property(code => code.Type), 10);
        // The keyed hash only. The code itself is never stored.
        entity.Property(code => code.CodeHash).HasMaxLength(HandoverCode.HashLength).IsFixedLength().IsRequired();
        entity.Property(code => code.CreatedAt).IsRequired();
        entity.Property(code => code.ExpiresAt).IsRequired();
        entity.Property(code => code.FailedAttempts).IsRequired();
        entity.Property(code => code.UsedAt);
        ConfigureId(entity.Property(code => code.UsedByUserId));
        entity.Property(code => code.SupersededAt);

        // "The current code for this booking and handover" is the only question ever asked.
        entity.HasIndex(code => new { code.BookingId, code.Type, code.CreatedAt });

        entity.HasOne<Booking>()
            .WithMany()
            .HasForeignKey(code => code.BookingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
