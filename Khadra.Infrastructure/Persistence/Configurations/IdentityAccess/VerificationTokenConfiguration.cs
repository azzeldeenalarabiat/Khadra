using Khadra.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.IdentityAccess;

internal sealed class VerificationTokenConfiguration : IEntityTypeConfiguration<VerificationToken>
{
    public void Configure(EntityTypeBuilder<VerificationToken> entity)
    {
        ConfigureAggregate(entity, "verification_tokens");

        ConfigureId(entity.Property(token => token.UserId)).IsRequired();
        ConfigureEnumeration(entity.Property(token => token.Purpose), 30);
        entity.Property(token => token.TokenHash).HasMaxLength(VerificationToken.TokenHashLength).IsRequired();
        entity.Property(token => token.CreatedAt).IsRequired();
        entity.Property(token => token.ExpiresAt).IsRequired();

        entity.HasIndex(token => token.TokenHash).IsUnique();
        entity.HasIndex(token => new { token.UserId, token.Purpose });

        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
