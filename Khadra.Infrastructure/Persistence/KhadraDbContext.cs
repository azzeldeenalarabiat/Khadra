using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence;

public sealed class KhadraDbContext(DbContextOptions<KhadraDbContext> options) : DbContext(options)
{
    public const string UpdatedAtShadowProperty = "UpdatedAt";

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<VerificationToken> VerificationTokens => Set<VerificationToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KhadraDbContext).Assembly);

        // Refresh-token rotation must detect races (two refreshes of the same token). xmin is a
        // PostgreSQL system column, so it is applied only when running on Npgsql.
        if (Database.IsNpgsql())
            modelBuilder.Entity<RefreshToken>().Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<AggregateRoot>())
        {
            if (entry.State == EntityState.Modified)
                entry.Property(UpdatedAtShadowProperty).CurrentValue = now;
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
