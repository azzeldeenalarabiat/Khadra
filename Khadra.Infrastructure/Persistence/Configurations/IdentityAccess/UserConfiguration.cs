using Khadra.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.IdentityAccess;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> entity)
    {
        ConfigureAggregate(entity, "users");

        entity.Property(user => user.Email)
            .HasConversion(email => email.Value, value => EmailAddress.Create(value).Value)
            .HasMaxLength(EmailAddress.MaxLength)
            .IsRequired();
        entity.Property(user => user.Phone)
            .HasConversion(phone => phone.Value, value => PhoneNumber.Create(value).Value)
            .HasMaxLength(PhoneNumber.MaxLength)
            .IsRequired();
        entity.Property(user => user.Name)
            .HasConversion(name => name.Value, value => PersonName.Create(value).Value)
            .HasMaxLength(PersonName.MaxLength)
            .IsRequired();
        entity.Property(user => user.PasswordHash)
            .HasConversion(hash => hash.Value, value => PasswordHash.FromHash(value))
            .HasMaxLength(PasswordHash.MaxLength)
            .IsRequired();

        ConfigureEnumeration(entity.Property(user => user.Role), 20);
        ConfigureEnumeration(entity.Property(user => user.Status), 20);

        entity.Property(user => user.IsEmailVerified).IsRequired();
        entity.Property(user => user.MustChangePassword).IsRequired();
        entity.Property(user => user.SecurityStamp).IsRequired();
        entity.Property(user => user.SuspensionReason).HasMaxLength(500);
        entity.Property(user => user.CreatedAt).IsRequired();
        entity.Property(user => user.IsDeleted).IsRequired();

        entity.HasIndex(user => user.Email).IsUnique();
        entity.HasIndex(user => user.Phone).IsUnique();
        entity.HasIndex(user => user.Role);

        // Soft delete: deleted accounts disappear from every query unless IgnoreQueryFilters() is used
        // with an explicit, commented justification.
        entity.HasQueryFilter(user => !user.IsDeleted);
    }
}
