using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.IdentityAccess;

internal sealed class PushDeviceConfiguration : IEntityTypeConfiguration<PushDevice>
{
    public void Configure(EntityTypeBuilder<PushDevice> entity)
    {
        ConfigureAggregate(entity, "push_devices");

        entity.Property(device => device.Token).HasMaxLength(PushDevice.MaxTokenLength).IsRequired();
        ConfigureEnumeration(entity.Property(device => device.Platform), 10);
        ConfigureId(entity.Property(device => device.UserId)).IsRequired();
        entity.Property(device => device.SessionFamilyId);
        entity.Property(device => device.Language)
            .HasConversion(language => language.Name, name => Enumeration.FromName<Language>(name))
            .HasMaxLength(2)
            .IsRequired();
        entity.Property(device => device.AppVersion).HasMaxLength(PushDevice.MaxAppVersionLength);
        entity.Property(device => device.CreatedAt).IsRequired();
        entity.Property(device => device.LastSeenAt).IsRequired();
        entity.Property(device => device.RevokedAt);

        // One row per install: a token registering again updates its row rather than adding one.
        entity.HasIndex(device => device.Token).IsUnique();
        entity.HasIndex(device => device.UserId);
        entity.HasIndex(device => device.SessionFamilyId);

        // Same context, so a real FK is fine; Restrict keeps user deletion a soft delete.
        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(device => device.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
