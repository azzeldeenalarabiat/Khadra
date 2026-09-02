using Khadra.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.IdentityAccess;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> entity)
    {
        ConfigureAggregate(entity, "refresh_tokens");

        ConfigureId(entity.Property(token => token.UserId)).IsRequired();
        ConfigureId(entity.Property(token => token.ReplacedByTokenId));
        entity.Property(token => token.FamilyId).IsRequired();
        entity.Property(token => token.TokenHash).HasMaxLength(RefreshToken.TokenHashLength).IsRequired();
        entity.Property(token => token.CreatedAt).IsRequired();
        entity.Property(token => token.ExpiresAt).IsRequired();
        entity.Property(token => token.FamilyExpiresAt).IsRequired();
        entity.Property(token => token.CreatedByIp).HasMaxLength(45);
        entity.Property(token => token.UserAgent).HasMaxLength(256);

        entity.HasIndex(token => token.TokenHash).IsUnique();
        entity.HasIndex(token => token.UserId);
        entity.HasIndex(token => token.FamilyId);

        // Same context, so a real FK is fine; Restrict keeps user deletion a soft delete.
        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
