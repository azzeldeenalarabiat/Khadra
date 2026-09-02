using Khadra.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Khadra.Infrastructure.Persistence.Configurations;

internal static class ModelConventions
{
    public static readonly ValueConverter<Id, Guid> IdConverter = new(id => id.Value, value => Id.From(value));

    // Shared shape for every aggregate: Id PK, DomainEvents unmapped, shadow updated_at.
    public static void ConfigureAggregate<T>(EntityTypeBuilder<T> entity, string tableName) where T : AggregateRoot
    {
        entity.ToTable(tableName);
        entity.HasKey(aggregate => aggregate.Id);
        entity.Property(aggregate => aggregate.Id).HasConversion(IdConverter).ValueGeneratedNever();
        entity.Ignore(aggregate => aggregate.DomainEvents);
        entity.Property<DateTimeOffset?>(KhadraDbContext.UpdatedAtShadowProperty);
    }

    public static PropertyBuilder<Id> ConfigureId(PropertyBuilder<Id> property) => property.HasConversion(IdConverter);

    public static PropertyBuilder<Id?> ConfigureId(PropertyBuilder<Id?> property) => property.HasConversion(IdConverter);

    // Smart enums persist by stable Name (readable in SQL, safe against reordering).
    public static PropertyBuilder<TEnumeration> ConfigureEnumeration<TEnumeration>(
        PropertyBuilder<TEnumeration> property,
        int maxLength = 30)
        where TEnumeration : Enumeration =>
        property
            .HasConversion(item => item.Name, name => Enumeration.FromName<TEnumeration>(name))
            .HasMaxLength(maxLength)
            .IsRequired();
}
