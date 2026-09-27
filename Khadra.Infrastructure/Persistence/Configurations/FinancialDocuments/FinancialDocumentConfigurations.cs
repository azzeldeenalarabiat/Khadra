using Khadra.Domain.FinancialDocuments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.FinancialDocuments;

// Issued financial documents (payments Phase 5, owner 2026-09-27). Append-only: the application refuses
// to modify or delete a row (IAppendOnly), and the migration adds the database triggers that refuse it
// too, TRUNCATE included. Every index is created now, while the table is empty: it only ever grows, and
// indexing it later would need CREATE INDEX CONCURRENTLY, which cannot run inside a migration's
// transaction.
internal sealed class FinancialDocumentConfiguration : IEntityTypeConfiguration<FinancialDocument>
{
    public void Configure(EntityTypeBuilder<FinancialDocument> entity)
    {
        ConfigureAggregate(entity, "financial_documents");

        ConfigureEnumeration(entity.Property(document => document.Type), 20).HasColumnName("document_type");
        entity.Property(document => document.Number)
            .HasColumnName("document_number")
            .HasMaxLength(FinancialDocumentNumbers.MaxLength)
            .IsRequired();
        ConfigureId(entity.Property(document => document.SubjectId)).IsRequired();
        entity.Property(document => document.Version).IsRequired();
        ConfigureId(entity.Property(document => document.PreviousVersionId));
        ConfigureId(entity.Property(document => document.RelatedDocumentId));
        ConfigureId(entity.Property(document => document.BookingId)).IsRequired();
        entity.Property(document => document.BookingReference)
            .HasMaxLength(FinancialDocument.BookingReferenceMaxLength)
            .IsRequired();
        ConfigureId(entity.Property(document => document.CustomerId)).IsRequired();
        ConfigureId(entity.Property(document => document.DealerId)).IsRequired();
        ConfigureId(entity.Property(document => document.PaymentId));
        ConfigureId(entity.Property(document => document.RefundId));
        ConfigureEnumeration(entity.Property(document => document.Cause), 20);
        entity.Property(document => document.OccurredAt).IsRequired();
        entity.Property(document => document.IssuedAt).IsRequired();
        entity.Property(document => document.CoversThrough);
        entity.Property(document => document.CheckpointFingerprint)
            .HasMaxLength(FinancialDocument.HashLength)
            .IsFixedLength();

        entity.OwnsOne(document => document.HeadlineAmount, money =>
        {
            money.Property(value => value.Amount).HasColumnName("headline_amount").HasPrecision(18, 3).IsRequired();
            money.Property(value => value.CurrencyCode).HasColumnName("currency").HasMaxLength(3).IsRequired();
        });
        entity.Navigation(document => document.HeadlineAmount).IsRequired();

        entity.Property(document => document.Provider).HasMaxLength(FinancialDocument.ProviderMaxLength).IsRequired();
        entity.Property(document => document.CalculatorVersion).IsRequired();
        entity.Property(document => document.SnapshotSchemaVersion).IsRequired();
        // `json`, not `jsonb`: PostgreSQL keeps the exact text of a json value — key order, spacing —
        // so the bytes read back are the bytes issued and content_sha256 stays provable. jsonb would
        // re-encode them. Nothing queries inside it; the scalar columns above serve every list.
        entity.Property(document => document.Snapshot).HasColumnType("json").IsRequired();
        entity.Property(document => document.ContentSha256)
            .HasMaxLength(FinancialDocument.HashLength)
            .IsFixedLength()
            .IsRequired();
        entity.Ignore(document => document.IsTest);

        // Versions and a refund receipt's payment receipt are in this context, so they are real keys.
        // Everything else — the booking, the payment, the refund, the people — is another context's,
        // referenced by id only, as everywhere in this database.
        entity.HasOne<FinancialDocument>()
            .WithMany()
            .HasForeignKey(document => document.PreviousVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<FinancialDocument>()
            .WithMany()
            .HasForeignKey(document => document.RelatedDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        // A number names exactly one document; a family has exactly one row per version.
        entity.HasIndex(document => document.Number).IsUnique();
        entity.HasIndex(document => new { document.Type, document.SubjectId, document.Version }).IsUnique();
        // The Invoices & Receipts area, a booking's documents, and the administrator's list: each a
        // range in a total order, (issued_at DESC, id DESC), so a page boundary never drops a row.
        entity.HasIndex(document => new { document.CustomerId, document.IssuedAt, document.Id })
            .IsDescending(false, true, true);
        entity.HasIndex(document => new { document.BookingId, document.IssuedAt, document.Id })
            .IsDescending(false, true, true);
        entity.HasIndex(document => new { document.IssuedAt, document.Id })
            .IsDescending(true, true);
        entity.HasIndex(document => document.PaymentId).HasFilter("payment_id IS NOT NULL");

        entity.ToTable(table =>
        {
            table.HasCheckConstraint("ck_financial_documents_version", "version >= 1");
            table.HasCheckConstraint(
                "ck_financial_documents_previous",
                "(version = 1) = (previous_version_id IS NULL)");
            table.HasCheckConstraint(
                "ck_financial_documents_subject",
                "(document_type = 'PaymentReceipt' AND payment_id IS NOT NULL AND refund_id IS NULL AND subject_id = payment_id)"
                + " OR (document_type = 'RefundReceipt' AND payment_id IS NOT NULL AND refund_id IS NOT NULL AND subject_id = refund_id)"
                + " OR (document_type = 'BookingStatement' AND refund_id IS NULL AND subject_id = booking_id)");
            table.HasCheckConstraint(
                "ck_financial_documents_statement_coverage",
                "(document_type = 'BookingStatement') = (covers_through IS NOT NULL AND checkpoint_fingerprint IS NOT NULL)");
        });
    }
}

// A void, keyed by the voided document's own id: at most one per document. Append-only.
internal sealed class FinancialDocumentVoidConfiguration : IEntityTypeConfiguration<FinancialDocumentVoid>
{
    public void Configure(EntityTypeBuilder<FinancialDocumentVoid> entity)
    {
        ConfigureAggregate(entity, "financial_document_voids");
        entity.Property(voided => voided.Id).HasColumnName("document_id");
        entity.Ignore(voided => voided.DocumentId);

        entity.Property(voided => voided.VoidedAt).IsRequired();
        ConfigureId(entity.Property(voided => voided.VoidedByAdminId)).IsRequired();
        entity.Property(voided => voided.Reason).HasMaxLength(FinancialDocumentVoid.MaxReasonLength).IsRequired();

        entity.HasOne<FinancialDocument>()
            .WithOne()
            .HasForeignKey<FinancialDocumentVoid>(voided => voided.Id)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

// Families owed a document that cannot be issued yet: one row each, upserted per failed attempt.
internal sealed class FinancialDocumentIssuanceHoldConfiguration : IEntityTypeConfiguration<FinancialDocumentIssuanceHold>
{
    public void Configure(EntityTypeBuilder<FinancialDocumentIssuanceHold> entity)
    {
        ConfigureAggregate(entity, "financial_document_issuance_holds");

        ConfigureEnumeration(entity.Property(hold => hold.DocumentType), 20);
        ConfigureId(entity.Property(hold => hold.SubjectId)).IsRequired();
        ConfigureId(entity.Property(hold => hold.BookingId)).IsRequired();
        ConfigureEnumeration(entity.Property(hold => hold.Reason), 30);
        entity.Property(hold => hold.Attempts).IsRequired();
        entity.Property(hold => hold.FirstFailedAt).IsRequired();
        entity.Property(hold => hold.LastFailedAt).IsRequired();
        entity.Property(hold => hold.NextAttemptAt).IsRequired();
        entity.Property(hold => hold.LastError).HasMaxLength(FinancialDocumentIssuanceHold.MaxErrorLength);
        entity.Property(hold => hold.ResolvedAt);
        entity.Ignore(hold => hold.IsResolved);

        entity.HasIndex(hold => new { hold.DocumentType, hold.SubjectId }).IsUnique();
        // The retry sweep and the work queue read only the unresolved ones.
        entity.HasIndex(hold => hold.NextAttemptAt).HasFilter("resolved_at IS NULL");
    }
}

// The counters behind the numbers. Not a domain aggregate: a number is taken with one atomic upsert in
// the transaction that inserts its document, so a rolled-back issue gives its number back.
internal sealed class FinancialDocumentSeriesConfiguration : IEntityTypeConfiguration<FinancialDocumentSeries>
{
    public void Configure(EntityTypeBuilder<FinancialDocumentSeries> entity)
    {
        entity.ToTable(
            "financial_document_series",
            table => table.HasCheckConstraint("ck_financial_document_series_last_number", "last_number >= 1"));
        entity.HasKey(series => series.SeriesKey);
        entity.Property(series => series.SeriesKey).HasMaxLength(FinancialDocumentNumbers.MaxSeriesKeyLength);
        entity.Property(series => series.LastNumber).IsRequired();
        entity.Property(series => series.UpdatedAt).IsRequired();
    }
}
