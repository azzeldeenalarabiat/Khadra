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

        // Shaped for how the log is actually read, and added NOW while the table is small.
        //
        // This becomes the largest table on the platform — it only ever grows — and indexing it later
        // means CREATE INDEX CONCURRENTLY, which cannot run inside the transaction EF wraps a
        // migration in. The write cost is three extra B-tree inserts on a table written a few times a
        // minute at most.
        //
        // Each one leads with the column being filtered and TRAILS the sort key, so the filtered
        // reads are an index range already in order rather than a scan and a sort. They all end in
        // Id because the ORDER BY does: occurred_at alone is not unique, and a non-total order lets
        // a page boundary drop an entry.
        entity.HasIndex(audit => new { audit.OccurredAt, audit.Id }).IsDescending();
        entity.HasIndex(audit => new { audit.ActorUserId, audit.OccurredAt, audit.Id })
            .IsDescending(false, true, true);
        entity.HasIndex(audit => new { audit.Action, audit.OccurredAt, audit.Id })
            .IsDescending(false, true, true);
        // "Everything about this record" — small, and the deep link from a record's own screen.
        entity.HasIndex(audit => new { audit.EntityType, audit.EntityId });

        // Deliberately NOT indexed: entity_type alone (low cardinality, and always paired above),
        // actor_role, and the text columns. Free-text search is a sequential scan; if an unfiltered
        // search ever exceeds ~100ms on production data, the answer is pg_trgm GIN indexes on
        // lower(subject_label) and lower(actor_name) — not a speculative index before then.

        // No soft-delete filter and no ISoftDeletable: an audit entry is never removed or hidden.
    }
}
