using Khadra.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.IdentityAccess;

internal sealed class CustomerDocumentConfiguration : IEntityTypeConfiguration<CustomerDocument>
{
    public void Configure(EntityTypeBuilder<CustomerDocument> entity)
    {
        entity.ToTable("customer_documents");
        entity.HasKey(document => document.Id);
        entity.Property(document => document.Id).HasConversion(IdConverter).ValueGeneratedNever();

        ConfigureId(entity.Property(document => document.UserId));
        ConfigureEnumeration(entity.Property(document => document.Type), 30);
        ConfigureEnumeration(entity.Property(document => document.Status), 20);
        // The KEY, never a URL: spec 7 requires these files to be unreachable without a signed link.
        entity.Property(document => document.StorageKey)
            .HasMaxLength(CustomerDocument.MaxStorageKeyLength).IsRequired();
        entity.Property(document => document.ContentType).HasMaxLength(100).IsRequired();
        entity.Property(document => document.SizeBytes).IsRequired();
        entity.Property(document => document.UploadedAt).IsRequired();
        entity.Property(document => document.ReviewNote).HasMaxLength(500);

        // One current document per type per customer: re-uploading replaces rather than accumulates.
        entity.HasIndex(document => new { document.UserId, document.Type }).IsUnique();
    }
}
