using Khadra.Domain.Common;
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

// A stored rendering of a document in one language (payments Phase 6). Append-only, like the document: one
// per document, language, format and template version, never replaced; its bytes live in private document
// storage and the row is the only pointer to them. Every index is created with the table, as for the
// documents: it only ever grows.
internal sealed class FinancialDocumentRenditionConfiguration : IEntityTypeConfiguration<FinancialDocumentRendition>
{
    public void Configure(EntityTypeBuilder<FinancialDocumentRendition> entity)
    {
        ConfigureAggregate(entity, "financial_document_renditions");

        ConfigureId(entity.Property(rendition => rendition.DocumentId)).IsRequired();
        entity.Property(rendition => rendition.Language)
            .HasConversion(language => language.Name, name => Enumeration.FromName<Language>(name))
            .HasMaxLength(2)
            .IsRequired();
        ConfigureEnumeration(entity.Property(rendition => rendition.Format), 10);
        ConfigureEnumeration(entity.Property(rendition => rendition.Kind), 10);
        entity.Property(rendition => rendition.TemplateVersion).IsRequired();
        entity.Property(rendition => rendition.RendererVersion)
            .HasMaxLength(FinancialDocumentRendition.MaxRendererVersionLength)
            .IsRequired();
        entity.Property(rendition => rendition.StorageKey)
            .HasMaxLength(FinancialDocumentRendition.MaxStorageKeyLength)
            .IsRequired();
        entity.Property(rendition => rendition.ContentSha256).HasMaxLength(64).IsFixedLength().IsRequired();
        entity.Property(rendition => rendition.SizeBytes).IsRequired();
        entity.Property(rendition => rendition.SnapshotSha256).HasMaxLength(64).IsFixedLength().IsRequired();
        entity.Property(rendition => rendition.RenderedAt).IsRequired();

        // One context, so a real reference; restricting, as every constraint here is.
        entity.HasOne<FinancialDocument>()
            .WithMany()
            .HasForeignKey(rendition => rendition.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        // One of each kind — as issued, and for a voided document its voided copy — per language, format and template.
        entity.HasIndex(rendition => new { rendition.DocumentId, rendition.Language, rendition.Format, rendition.Kind, rendition.TemplateVersion })
            .IsUnique();
        entity.HasIndex(rendition => rendition.StorageKey).IsUnique();

        entity.ToTable(table =>
        {
            table.HasCheckConstraint("ck_financial_document_renditions_template_version", "template_version >= 1");
            table.HasCheckConstraint("ck_financial_document_renditions_size_bytes", "size_bytes > 0");
        });
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

// The emails owed for issued receipts (payments Phase 7). The header is mutable — it is the outbox row, and it moves
// from Queued to where it ended — while its attempts are append-only children: the history the administrator reads.
// The recipient's address lives on the header only, which a future erasure can blank; the attempts carry none.
internal sealed class FinancialDocumentDeliveryConfiguration : IEntityTypeConfiguration<FinancialDocumentDelivery>
{
    public void Configure(EntityTypeBuilder<FinancialDocumentDelivery> entity)
    {
        ConfigureAggregate(entity, "financial_document_deliveries");

        ConfigureId(entity.Property(delivery => delivery.DocumentId)).IsRequired();
        ConfigureEnumeration(entity.Property(delivery => delivery.Channel), 10);
        ConfigureEnumeration(entity.Property(delivery => delivery.State), 10);
        // By id only: the administrator is another context's.
        ConfigureId(entity.Property(delivery => delivery.RequestedByAdminId));
        entity.Property(delivery => delivery.QueuedAt).IsRequired();
        entity.Property(delivery => delivery.NextAttemptAt).IsRequired();
        // The proof of a claim: every claim moves it, so a process whose claim was taken over cannot write the row.
        entity.Property(delivery => delivery.Claims).IsRequired().IsConcurrencyToken();
        entity.Property(delivery => delivery.SendAttempts).IsRequired();
        entity.Property(delivery => delivery.CompletedAt);
        entity.Property(delivery => delivery.RecipientAddress).HasMaxLength(FinancialDocumentDelivery.MaxAddressLength);
        entity.Property(delivery => delivery.Languages).HasMaxLength(FinancialDocumentDelivery.MaxLanguagesLength);
        entity.Property(delivery => delivery.WaitingReason)
            .HasConversion(reason => reason!.Name, name => Enumeration.FromName<FinancialDocumentDeliveryWait>(name))
            .HasMaxLength(20);
        entity.Property(delivery => delivery.WaitingSince);
        entity.Property(delivery => delivery.LastError).HasMaxLength(FinancialDocumentDelivery.MaxErrorLength);

        // One context, so a real reference; restricting, as every constraint here is.
        entity.HasOne<FinancialDocument>()
            .WithMany()
            .HasForeignKey(delivery => delivery.DocumentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_financial_document_deliveries_document");

        // The aggregate never removes an attempt, so Restrict rather than ClientCascade: there is nothing to sever.
        entity.HasMany(delivery => delivery.Attempts)
            .WithOne()
            .HasForeignKey(attempt => attempt.DeliveryId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_financial_document_delivery_attempts_delivery");
        entity.Metadata.FindNavigation(nameof(FinancialDocumentDelivery.Attempts))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // One email of a document on its way at a time: queueing at issue is idempotent, and a second "email it again"
        // while one is queued is refused by the database as well as by the handler.
        entity.HasIndex(delivery => delivery.DocumentId)
            .IsUnique()
            .HasFilter("state = 'Queued'")
            .HasDatabaseName("ix_financial_document_deliveries_one_queued");
        // The dispatcher's question, which only queued rows answer: the size of the backlog, not of history.
        entity.HasIndex(delivery => delivery.NextAttemptAt)
            .HasFilter("state = 'Queued'")
            .HasDatabaseName("ix_financial_document_deliveries_queued_due");
        // A document's page lists its emails newest first.
        entity.HasIndex(delivery => new { delivery.DocumentId, delivery.QueuedAt });
        // The work queue's other question, "which emails failed": failures only, so it stays the size of the failures
        // however long history grows.
        entity.HasIndex(delivery => delivery.QueuedAt)
            .HasFilter("state = 'Failed'")
            .HasDatabaseName("ix_financial_document_deliveries_failed");

        entity.ToTable(table =>
        {
            table.HasCheckConstraint("ck_financial_document_deliveries_counts", "claims >= 0 AND send_attempts >= 0");
            table.HasCheckConstraint("ck_financial_document_deliveries_completed", "(state = 'Queued') = (completed_at IS NULL)");
            // A wait has a reason and a start, both or neither — and only a queued email waits: one that was sent or ended
            // never says "waiting for its PDF" above its end.
            table.HasCheckConstraint(
                "ck_financial_document_deliveries_waiting",
                "(waiting_reason IS NULL) = (waiting_since IS NULL) AND (state = 'Queued' OR waiting_reason IS NULL)");
        });
    }
}

// One attempt at a document email (payments Phase 7). Append-only: the application refuses to modify or delete a row
// (IAppendOnly), and the migration adds the database triggers that refuse it too, TRUNCATE included.
internal sealed class FinancialDocumentDeliveryAttemptConfiguration : IEntityTypeConfiguration<FinancialDocumentDeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<FinancialDocumentDeliveryAttempt> entity)
    {
        entity.ToTable("financial_document_delivery_attempts");
        entity.HasKey(attempt => attempt.Id);
        entity.Property(attempt => attempt.Id).HasConversion(IdConverter).ValueGeneratedNever();

        ConfigureId(entity.Property(attempt => attempt.DeliveryId)).IsRequired();
        entity.Property(attempt => attempt.Number).IsRequired();
        ConfigureEnumeration(entity.Property(attempt => attempt.Outcome), 10);
        entity.Property(attempt => attempt.AttemptedAt).IsRequired();
        entity.Property(attempt => attempt.Error).HasMaxLength(FinancialDocumentDelivery.MaxErrorLength);
        entity.Property(attempt => attempt.Provider).HasMaxLength(FinancialDocumentDeliveryAttempt.MaxProviderLength);
        entity.Property(attempt => attempt.ProviderMessageId).HasMaxLength(FinancialDocumentDeliveryAttempt.MaxProviderMessageIdLength);
        ConfigureId(entity.Property(attempt => attempt.EnglishRenditionId));
        ConfigureId(entity.Property(attempt => attempt.ArabicRenditionId));

        // The PDFs it carried: real references in one context, restricting — each named for its language, since the
        // generated names differ only by a trailing digit.
        entity.HasOne<FinancialDocumentRendition>()
            .WithMany()
            .HasForeignKey(attempt => attempt.EnglishRenditionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_financial_document_delivery_attempts_english_rendition");
        entity.HasOne<FinancialDocumentRendition>()
            .WithMany()
            .HasForeignKey(attempt => attempt.ArabicRenditionId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_financial_document_delivery_attempts_arabic_rendition");

        entity.HasIndex(attempt => new { attempt.DeliveryId, attempt.Number }).IsUnique();

        entity.ToTable(table => table.HasCheckConstraint("ck_financial_document_delivery_attempts_number", "number >= 1"));
    }
}
