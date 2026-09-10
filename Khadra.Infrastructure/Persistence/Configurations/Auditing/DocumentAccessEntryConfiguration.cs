using Khadra.Domain.Auditing;
using Khadra.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.Auditing;

// The disclosure log (pre-launch item 86). Narrow, insert-only, and read by exactly three questions:
// "who has seen this customer's papers", "what happened on this booking", "what has this dealership
// been opening".
internal sealed class DocumentAccessEntryConfiguration : IEntityTypeConfiguration<DocumentAccessEntry>
{
    public void Configure(EntityTypeBuilder<DocumentAccessEntry> entity)
    {
        ConfigureAggregate(entity, "document_access_entries");

        entity.Property(access => access.OccurredAt).IsRequired();
        ConfigureId(entity.Property(access => access.ActorUserId));
        entity.Property(access => access.ActorName)
            .HasMaxLength(DocumentAccessEntry.MaxActorNameLength).IsRequired();
        ConfigureEnumeration(entity.Property(access => access.ActorRole), 20);
        ConfigureId(entity.Property(access => access.DealerId));
        ConfigureId(entity.Property(access => access.BookingId));
        ConfigureId(entity.Property(access => access.SubjectUserId));
        ConfigureId(entity.Property(access => access.DocumentId));
        // An IdentityAccess smart enum on an Auditing record, converted explicitly. Left to
        // convention EF would try to make CustomerDocumentType an entity type and fail the whole
        // model build -- the same trap User.MissingRenterDocumentTypes documents.
        ConfigureEnumeration(entity.Property(access => access.DocumentType), 30);
        entity.Property(access => access.DocumentUploadedAt).IsRequired();
        ConfigureEnumeration(entity.Property(access => access.Action), 20);
        entity.Property(access => access.CorrelationId).HasMaxLength(64);

        // Added NOW while the table is empty, for the reason AuditEntryConfiguration gives: this
        // table only ever grows, and indexing it later means CREATE INDEX CONCURRENTLY, which cannot
        // run inside the transaction EF wraps a migration in.
        //
        // The first one is the point of the table. "Who has looked at my documents" must be an index
        // range on the customer's own id -- not a join through customer_documents, which is exactly
        // the table that may have been emptied by the time anybody asks.
        entity.HasIndex(access => new { access.SubjectUserId, access.OccurredAt, access.Id })
            .IsDescending(false, true, true);
        // "What happened on this booking" -- small, and the deep link from a booking's own screen.
        entity.HasIndex(access => new { access.BookingId, access.OccurredAt });
        // "What has this dealership been opening" -- the one an investigation starts from.
        entity.HasIndex(access => new { access.DealerId, access.OccurredAt, access.Id })
            .IsDescending(false, true, true);

        // No soft-delete filter and no ISoftDeletable, the same as audit_entries: a disclosure record
        // is never removed or hidden. There is deliberately no index on document_id alone -- a
        // document is always reached through its subject or its booking, and an index nobody uses is
        // a write cost on the request path of a handover screen.
    }
}
