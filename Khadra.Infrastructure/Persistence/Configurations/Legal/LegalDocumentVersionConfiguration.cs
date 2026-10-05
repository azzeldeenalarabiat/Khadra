using Khadra.Domain.Common;
using Khadra.Domain.Legal;
using Khadra.Domain.Legal.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.Legal;

// Published legal texts (Wave 2 G1; reviewed by the advisor before its migration, 2026-10-05). Append-only: the
// application refuses to modify or delete a row (IAppendOnly), and the migration adds the database triggers that refuse
// it too, TRUNCATE included. Every rule here is portable SQL, so the SQLite suite holds the same constraints PostgreSQL
// does.
internal sealed class LegalDocumentVersionConfiguration : IEntityTypeConfiguration<LegalDocumentVersion>
{
    public void Configure(EntityTypeBuilder<LegalDocumentVersion> entity)
    {
        ConfigureAggregate(entity, "legal_document_versions");

        ConfigureEnumeration(entity.Property(version => version.Kind));
        entity.Property(version => version.VersionLabel)
            .HasMaxLength(LegalDocumentVersion.MaxLabelLength)
            .IsRequired();
        entity.Property(version => version.EffectiveFrom).IsRequired();
        entity.Property(version => version.PublishedAt).IsRequired();
        // IdentityAccess's id, by value only, as every cross-context reference in this database. The publisher's
        // name is in the audit entry written with the version, not here.
        ConfigureId(entity.Property(version => version.PublishedByAdminId)).IsRequired();
        entity.Property(version => version.BodyEn).IsRequired();
        entity.Property(version => version.BodyAr).IsRequired();
        entity.Property(version => version.BodyEnSha256)
            .HasMaxLength(LegalDocumentVersion.HashLength)
            .IsFixedLength()
            .IsRequired();
        entity.Property(version => version.BodyArSha256)
            .HasMaxLength(LegalDocumentVersion.HashLength)
            .IsFixedLength()
            .IsRequired();

        // Named once, for good: the publish handler recognises a lost race by these names.
        entity.HasIndex(version => new { version.Kind, version.EffectiveFrom })
            .IsUnique()
            .HasDatabaseName(ILegalDocumentVersionRepository.KindEffectiveFromIndex);
        entity.HasIndex(version => new { version.Kind, version.VersionLabel })
            .IsUnique()
            .HasDatabaseName(ILegalDocumentVersionRepository.KindLabelIndex);

        entity.ToTable(table =>
        {
            // Built from the enumeration, so adding a kind changes the model and the migration cannot be forgotten.
            var kinds = string.Join(", ", Enumeration.GetAll<LegalDocumentKind>().Select(kind => $"'{kind.Name}'"));
            table.HasCheckConstraint("ck_legal_document_versions_kind", $"kind IN ({kinds})");
            table.HasCheckConstraint("ck_legal_document_versions_effective_from", "effective_from >= published_at");
            table.HasCheckConstraint(
                "ck_legal_document_versions_version_label",
                $"length(version_label) BETWEEN 1 AND {LegalDocumentVersion.MaxLabelLength} AND version_label = trim(version_label)");
            table.HasCheckConstraint(
                "ck_legal_document_versions_bodies",
                $"length(body_en) BETWEEN 1 AND {LegalDocumentVersion.MaxBodyLength} AND length(body_ar) BETWEEN 1 AND {LegalDocumentVersion.MaxBodyLength}");
            table.HasCheckConstraint(
                "ck_legal_document_versions_hashes",
                $"length(body_en_sha256) = {LegalDocumentVersion.HashLength} AND length(body_ar_sha256) = {LegalDocumentVersion.HashLength}");
        });
    }
}
