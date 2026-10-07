using Khadra.Domain.Common;
using Khadra.Domain.Legal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.Legal;

// Consents to the published legal texts (Wave 4, W4-8; reviewed by the advisor before its migration, 2026-10-07).
// Append-only like the versions they name: the application refuses to modify or delete a row (IAppendOnly), and the
// migration adds the triggers that refuse it too, TRUNCATE included. Every rule here is portable SQL, so the SQLite
// suite holds the same constraints PostgreSQL does.
internal sealed class LegalConsentConfiguration : IEntityTypeConfiguration<LegalConsent>
{
    public void Configure(EntityTypeBuilder<LegalConsent> entity)
    {
        ConfigureAggregate(entity, "legal_consents");

        // IdentityAccess's id, by value only, as every cross-context reference in this database.
        ConfigureId(entity.Property(consent => consent.UserId)).IsRequired();
        ConfigureId(entity.Property(consent => consent.DocumentVersionId)).IsRequired();
        entity.Property(consent => consent.OccurredAt).IsRequired();
        ConfigureEnumeration(entity.Property(consent => consent.Channel), maxLength: 20);
        ConfigureEnumeration(entity.Property(consent => consent.Language), maxLength: 2);
        ConfigureEnumeration(entity.Property(consent => consent.Action), maxLength: 20);

        // The same context, so a real reference: a consent can never name a version that does not exist, and a version
        // somebody accepted can never be removed (it cannot anyway: the versions are append-only too).
        entity.HasOne<LegalDocumentVersion>()
            .WithMany()
            .HasForeignKey(consent => consent.DocumentVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        // A person's record, newest first, as the data subject reads it; and "has this person accepted this version",
        // which the gate asks on every request it judges and the recorder asks before it writes a version once.
        entity.HasIndex(consent => new { consent.UserId, consent.OccurredAt }).IsDescending(false, true);
        entity.HasIndex(consent => new { consent.UserId, consent.DocumentVersionId });

        entity.ToTable(table =>
        {
            // Built from the enumerations, so adding a member changes the model and the migration cannot be forgotten.
            table.HasCheckConstraint("ck_legal_consents_channel", $"channel IN ({Names<ConsentChannel>()})");
            table.HasCheckConstraint("ck_legal_consents_language", $"language IN ({Names<Language>()})");
            table.HasCheckConstraint("ck_legal_consents_action", $"action IN ({Names<ConsentAction>()})");
        });
    }

    private static string Names<T>() where T : Enumeration =>
        string.Join(", ", Enumeration.GetAll<T>().Select(item => $"'{item.Name}'"));
}
