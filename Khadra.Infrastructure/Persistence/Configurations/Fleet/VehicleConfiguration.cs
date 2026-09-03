using Khadra.Domain.Common;
using Khadra.Domain.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.Fleet;

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> entity)
    {
        ConfigureAggregate(entity, "vehicles");

        ConfigureId(entity.Property(vehicle => vehicle.DealerId));
        ConfigureId(entity.Property(vehicle => vehicle.CarTypeId));
        ConfigureEnumeration(entity.Property(vehicle => vehicle.Status), 20);

        entity.Property(vehicle => vehicle.PlateNumber)
            .HasConversion(plate => plate.Value, value => PlateNumber.Create(value).Value)
            .HasMaxLength(PlateNumber.MaxDigits)
            .IsRequired();

        // The car's own description of itself: flat columns rather than JSON, because customer
        // search will filter on make, model, year and seats.
        entity.OwnsOne(vehicle => vehicle.Details, details =>
        {
            details.Property(value => value.Make).HasColumnName("make").HasMaxLength(60).IsRequired();
            details.Property(value => value.Model).HasColumnName("model").HasMaxLength(60).IsRequired();
            details.Property(value => value.Year).HasColumnName("year").IsRequired();
            details.Property(value => value.Color).HasColumnName("color").HasMaxLength(40);
            details.Property(value => value.Seats).HasColumnName("seats").IsRequired();
            details.Property(value => value.Description)
                .HasColumnName("description").HasMaxLength(VehicleDetails.MaxDescriptionLength);
            details.Property(value => value.Transmission)
                .HasColumnName("transmission")
                .HasConversion(type => type.Name, name => Enumeration.FromName<TransmissionType>(name))
                .HasMaxLength(20).IsRequired();
            details.Property(value => value.FuelType)
                .HasColumnName("fuel_type")
                .HasConversion(type => type.Name, name => Enumeration.FromName<FuelType>(name))
                .HasMaxLength(20).IsRequired();
        });
        entity.Navigation(vehicle => vehicle.Details).IsRequired();

        ConfigureMoney(entity, vehicle => vehicle.DailyRate, "daily_rate");
        ConfigureMoney(entity, vehicle => vehicle.SecurityDeposit, "security_deposit");

        // Spec 4.3: per-car mileage policy, either unlimited or a daily cap with an excess fee.
        entity.OwnsOne(vehicle => vehicle.Mileage, mileage =>
        {
            mileage.Property(policy => policy.IsUnlimited).HasColumnName("mileage_unlimited").IsRequired();
            mileage.Property(policy => policy.DailyLimitKm).HasColumnName("mileage_daily_limit_km");
            mileage.OwnsOne(policy => policy.ExcessFeePerKm, fee =>
            {
                fee.Property(money => money.Amount).HasColumnName("mileage_excess_fee").HasPrecision(18, 3);
                fee.Property(money => money.CurrencyCode).HasColumnName("mileage_excess_currency").HasMaxLength(3);
            });
        });
        entity.Navigation(vehicle => vehicle.Mileage).IsRequired();

        entity.Property(vehicle => vehicle.FuelPolicy)
            .HasConversion(policy => policy.Name, name => Enumeration.FromName<FuelPolicy>(name))
            .HasMaxLength(20).IsRequired();

        entity.Property(vehicle => vehicle.IsDeliveryEligible).IsRequired();
        entity.Property(vehicle => vehicle.CreatedAt).IsRequired();
        entity.Property(vehicle => vehicle.IsDeleted).IsRequired();

        entity.HasMany(vehicle => vehicle.Images)
            .WithOne()
            .HasForeignKey(image => image.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.Metadata.FindNavigation(nameof(Vehicle.Images))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        entity.HasIndex(vehicle => vehicle.DealerId);
        entity.HasIndex(vehicle => vehicle.Status);
        // The same car cannot be listed by two dealers. Unique across soft-deleted rows too, so a
        // deleted listing does not free the plate for someone else.
        entity.HasIndex(vehicle => vehicle.PlateNumber).IsUnique();

        entity.HasQueryFilter(vehicle => !vehicle.IsDeleted);
    }

    private static void ConfigureMoney(
        EntityTypeBuilder<Vehicle> entity,
        System.Linq.Expressions.Expression<Func<Vehicle, Money?>> property,
        string prefix)
    {
        entity.OwnsOne(property, money =>
        {
            money.Property(value => value.Amount).HasColumnName(prefix).HasPrecision(18, 3).IsRequired();
            money.Property(value => value.CurrencyCode)
                .HasColumnName($"{prefix}_currency").HasMaxLength(3).IsRequired();
        });
        entity.Navigation(property).IsRequired();
    }
}

internal sealed class VehicleImageConfiguration : IEntityTypeConfiguration<VehicleImage>
{
    public void Configure(EntityTypeBuilder<VehicleImage> entity)
    {
        entity.ToTable("vehicle_images");
        entity.HasKey(image => image.Id);
        entity.Property(image => image.Id).HasConversion(IdConverter).ValueGeneratedNever();

        ConfigureId(entity.Property(image => image.VehicleId));
        // A public marketing image, unlike the private identity documents elsewhere (spec 7). The key
        // is still stored rather than a URL, so the serving strategy stays ours to change.
        entity.Property(image => image.StorageKey).HasMaxLength(500).IsRequired();
        entity.Property(image => image.Position).IsRequired();
        entity.Property(image => image.IsPrimary).IsRequired();
        entity.Property(image => image.UploadedAt).IsRequired();

        entity.HasIndex(image => new { image.VehicleId, image.Position });
    }
}
