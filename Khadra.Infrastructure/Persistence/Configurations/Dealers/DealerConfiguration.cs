using Khadra.Domain.Dealers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.Dealers;

internal sealed class DealerConfiguration : IEntityTypeConfiguration<Dealer>
{
    public void Configure(EntityTypeBuilder<Dealer> entity)
    {
        ConfigureAggregate(entity, "dealers");

        ConfigureId(entity.Property(dealer => dealer.OwnerUserId));
        ConfigureId(entity.Property(dealer => dealer.CityId));
        ConfigureId(entity.Property(dealer => dealer.ReviewedByAdminId));

        entity.Property(dealer => dealer.BusinessName)
            .HasConversion(name => name.Value, value => BusinessName.FromPersisted(value))
            .HasMaxLength(BusinessName.MaxLength)
            .IsRequired();
        entity.Property(dealer => dealer.CommercialRegistration)
            .HasConversion(number => number.Value, value => CommercialRegistrationNumber.FromPersisted(value))
            .HasMaxLength(CommercialRegistrationNumber.MaxLength)
            .IsRequired();
        entity.Property(dealer => dealer.OperatingHours)
            .HasConversion(OperatingHoursConverter.Instance, OperatingHoursConverter.Comparer)
            .HasMaxLength(OperatingHoursConverter.MaxLength)
            .IsRequired();

        entity.Property(dealer => dealer.Description).HasMaxLength(2000);
        entity.Property(dealer => dealer.ReviewNote).HasMaxLength(1000);
        entity.Property(dealer => dealer.SuspensionReason).HasMaxLength(1000);
        entity.Property(dealer => dealer.LogoStorageKey).HasMaxLength(500);
        entity.Property(dealer => dealer.CoverStorageKey).HasMaxLength(500);
        entity.Property(dealer => dealer.SubmittedAt).IsRequired();
        entity.Property(dealer => dealer.ReviewDueAt).IsRequired();
        entity.Property(dealer => dealer.CreatedAt).IsRequired();
        entity.Property(dealer => dealer.IsSuspended).IsRequired();
        entity.Property(dealer => dealer.IsDeleted).IsRequired();

        ConfigureEnumeration(entity.Property(dealer => dealer.VerificationStatus), 30);

        entity.OwnsOne(dealer => dealer.Location, location =>
        {
            location.Property(point => point.Latitude).HasColumnName("latitude").IsRequired();
            location.Property(point => point.Longitude).HasColumnName("longitude").IsRequired();
        });
        entity.Navigation(dealer => dealer.Location).IsRequired();

        entity.OwnsOne(dealer => dealer.Delivery, delivery =>
        {
            delivery.Property(settings => settings.IsEnabled).HasColumnName("delivery_enabled").IsRequired();
            delivery.Property(settings => settings.RadiusKm).HasColumnName("delivery_radius_km")
                .HasPrecision(6, 2).IsRequired();
            // The gallery’s own price for a delivery. Optional, because it exists exactly while
            // delivery is on — a disabled dealership is not quoting anything. Mapped explicitly
            // like every other Money on the platform: these are get-only properties, and anything
            // left to convention is silently dropped.
            delivery.OwnsOne(settings => settings.Fee, fee =>
            {
                fee.Property(value => value.Amount).HasColumnName("delivery_fee_amount")
                    .HasPrecision(18, 3);
                fee.Property(value => value.CurrencyCode).HasColumnName("delivery_fee_currency")
                    .HasMaxLength(3);
            });
        });
        entity.Navigation(dealer => dealer.Delivery).IsRequired();

        // Children are reached only through the aggregate, so the navigations are field-backed and
        // the collections load with the dealer.
        entity.HasMany(dealer => dealer.Employees)
            .WithOne()
            .HasForeignKey(employee => employee.DealerId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.Metadata.FindNavigation(nameof(Dealer.Employees))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        entity.HasMany(dealer => dealer.Documents)
            .WithOne()
            .HasForeignKey(document => document.DealerId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.Metadata.FindNavigation(nameof(Dealer.Documents))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // One live dealership per owner, enforced by the database and not only by the handler.
        //
        // SubmitDealerProfileHandler checks ExistsForOwnerAsync first, but a check and an insert in
        // separate statements is a race: two clicks on the same 24 MB multipart, or the same form in
        // two tabs, both pass the check and both insert. The damage is not a duplicate row — it is
        // that GetByOwnerUserIdAsync uses SingleOrDefaultAsync, so from that moment every dealer
        // request for that owner throws, and their console answers 500 for ever.
        //
        // Filtered on is_deleted, deliberately: the handler's check runs behind the soft-delete query
        // filter, so an owner whose dealership was soft-deleted is allowed to apply again, and a
        // total unique index would silently refuse them.
        entity.HasIndex(dealer => dealer.OwnerUserId)
            .IsUnique()
            .HasFilter("is_deleted = false");
        entity.HasIndex(dealer => dealer.VerificationStatus);
        // The admin queue asks "which pending applications are past their promise" on every dashboard
        // load; this makes that an index scan rather than a table scan plus arithmetic.
        entity.HasIndex(dealer => dealer.ReviewDueAt);
        entity.HasIndex(dealer => dealer.CommercialRegistration).IsUnique();

        entity.HasQueryFilter(dealer => !dealer.IsDeleted);
    }
}

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> entity)
    {
        entity.ToTable("dealer_employees");
        entity.HasKey(employee => employee.Id);
        entity.Property(employee => employee.Id).HasConversion(IdConverter).ValueGeneratedNever();

        ConfigureId(entity.Property(employee => employee.DealerId));
        ConfigureId(entity.Property(employee => employee.UserId));
        entity.Property(employee => employee.CanViewReports).IsRequired();
        entity.Property(employee => employee.IsActive).IsRequired();
        entity.Property(employee => employee.CreatedAt).IsRequired();

        // One row per person per dealer: re-hiring reactivates the existing row so past booking
        // decisions stay attached to one identity (see Dealer.HireEmployee).
        entity.HasIndex(employee => new { employee.DealerId, employee.UserId }).IsUnique();
    }
}

internal sealed class DealerDocumentConfiguration : IEntityTypeConfiguration<DealerDocument>
{
    public void Configure(EntityTypeBuilder<DealerDocument> entity)
    {
        entity.ToTable("dealer_documents");
        entity.HasKey(document => document.Id);
        entity.Property(document => document.Id).HasConversion(IdConverter).ValueGeneratedNever();

        ConfigureId(entity.Property(document => document.DealerId));
        ConfigureEnumeration(entity.Property(document => document.Type), 30);
        entity.Property(document => document.StorageKey).HasMaxLength(500).IsRequired();
        entity.Property(document => document.UploadedAt).IsRequired();

        entity.HasIndex(document => new { document.DealerId, document.Type }).IsUnique();
    }
}
