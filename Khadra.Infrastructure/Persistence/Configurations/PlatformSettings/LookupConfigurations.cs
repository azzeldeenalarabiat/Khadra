using Khadra.Domain.Common;
using Khadra.Domain.PlatformSettings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.PlatformSettings;

// Admin-managed lookup data (spec 3.2). Two tables rather than one with a discriminator: they are
// separate concepts that happen to share a shape, and a city carries a centre point a car type
// never will.
//
// LookupEntry itself is deliberately unmapped, the same way AggregateRoot is: nothing queries "any
// lookup", so a base table would exist only to satisfy the class hierarchy.

internal sealed class CarTypeConfiguration : IEntityTypeConfiguration<CarType>
{
    public void Configure(EntityTypeBuilder<CarType> entity)
    {
        ConfigureAggregate(entity, "car_types");
        ConfigureLookup(entity);
    }

    internal static void ConfigureLookup<T>(EntityTypeBuilder<T> entity) where T : LookupEntry
    {
        // Bilingual from day one: the customer app ships in Arabic and English, and a lookup with
        // one name would force one of them to read a language it did not choose.
        entity.Property(lookup => lookup.NameEn).HasMaxLength(100).IsRequired();
        entity.Property(lookup => lookup.NameAr).HasMaxLength(100).IsRequired();
        entity.Property(lookup => lookup.IsActive).IsRequired();
        entity.Property(lookup => lookup.DisplayOrder).IsRequired();
        entity.Property(lookup => lookup.CreatedAt).IsRequired();

        // The order the admin chose, then the name, so two entries sharing an order still come back
        // in a settled sequence rather than whatever the database felt like.
        entity.HasIndex(lookup => new { lookup.DisplayOrder, lookup.NameEn });
    }
}

internal sealed class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> entity)
    {
        ConfigureAggregate(entity, "cities");
        CarTypeConfiguration.ConfigureLookup(entity);

        // Optional, and owned: a city that has not been pinned on the map yet is a real state, and
        // the two coordinates belong to the city rather than being a table of their own.
        entity.OwnsOne(city => city.Centre, centre =>
        {
            centre.Property(point => point.Latitude).HasColumnName("centre_latitude");
            centre.Property(point => point.Longitude).HasColumnName("centre_longitude");
        });
    }
}
