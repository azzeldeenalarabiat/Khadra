using Khadra.Domain.Auditing;
using Khadra.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.Auditing;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> entity)
    {
        ConfigureAggregate(entity, "audit_entries");

        entity.Property(audit => audit.OccurredAt).IsRequired();
        ConfigureId(entity.Property(audit => audit.ActorUserId));
        entity.Property(audit => audit.ActorName).HasMaxLength(AuditEntry.MaxLabelLength).IsRequired();
        // Optional: a system-recorded entry has no role.
        entity.Property(audit => audit.ActorRole)
            .HasConversion(role => role!.Name, name => Domain.Common.Enumeration.FromName<UserRole>(name))
            .HasMaxLength(20);
        ConfigureEnumeration(entity.Property(audit => audit.Action), 40);
        ConfigureEnumeration(entity.Property(audit => audit.EntityType), 20);
        ConfigureId(entity.Property(audit => audit.EntityId));
        entity.Property(audit => audit.SubjectLabel).HasMaxLength(AuditEntry.MaxLabelLength).IsRequired();
        entity.Property(audit => audit.PreviousValue).HasMaxLength(AuditEntry.MaxValueLength);
        entity.Property(audit => audit.NewValue).HasMaxLength(AuditEntry.MaxValueLength);
        entity.Property(audit => audit.Reason).HasMaxLength(AuditEntry.MaxReasonLength);
        entity.Property(audit => audit.CorrelationId).HasMaxLength(64);

        // The activity feed and the audit screen both read newest-first, and the screen filters by
        // actor and by entity.
        entity.HasIndex(audit => audit.OccurredAt).IsDescending();
        entity.HasIndex(audit => audit.ActorUserId);
        entity.HasIndex(audit => new { audit.EntityType, audit.EntityId });

        // No soft-delete filter and no ISoftDeletable: an audit entry is never removed or hidden.
    }
}
