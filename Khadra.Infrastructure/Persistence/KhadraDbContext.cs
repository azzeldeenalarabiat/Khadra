using Khadra.Domain.Auditing;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Disputes;
using Khadra.Domain.Fleet;
using Khadra.Domain.IdentityAccess;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Infrastructure.Persistence;

public sealed class KhadraDbContext(DbContextOptions<KhadraDbContext> options) : DbContext(options)
{
    public const string UpdatedAtShadowProperty = "UpdatedAt";

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<VerificationToken> VerificationTokens => Set<VerificationToken>();
    public DbSet<Dealer> Dealers => Set<Dealer>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<DisputeTicket> DisputeTickets => Set<DisputeTicket>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KhadraDbContext).Assembly);

        // Optimistic concurrency, where losing a race silently would cost more than failing loudly.
        // xmin is a PostgreSQL system column, so it is applied only when running on Npgsql; it needs
        // no migration because the column already exists on every table.
        //
        // - RefreshToken: two refreshes of the same token must not both succeed.
        // - DisputeTicket and Booking: two admins resolving the same ticket would otherwise both pass
        //   the in-memory status check and both save, so the second silently overwrites the first's
        //   decision about money, leaving two audit entries asserting different outcomes.
        if (Database.IsNpgsql())
        {
            foreach (var type in new[] { typeof(RefreshToken), typeof(DisputeTicket), typeof(Booking) })
            {
                modelBuilder.Entity(type)
                    .Property<uint>("xmin")
                    .HasColumnType("xid")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsConcurrencyToken();
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GuardAuditTrailIsAppendOnly();

        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<AggregateRoot>())
        {
            if (entry.State == EntityState.Modified)
                entry.Property(UpdatedAtShadowProperty).CurrentValue = now;
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    // The database enforces this too (a trigger added in the migration), but a request that tries it
    // should fail here with a message that names the problem rather than surfacing a Postgres error.
    // The check also holds on SQLite, where the tests run and the trigger does not exist.
    //
    // PRE-LAUNCH (see docs/pre-launch-checklist.md): the database trigger is FOR EACH ROW BEFORE
    // DELETE OR UPDATE, and row-level triggers do not fire on TRUNCATE. Anyone holding table
    // privileges can therefore erase the whole audit trail in one statement without tripping either
    // guard. Deliberately deferred while this is a development database that gets reseeded; it must
    // be closed with a FOR EACH STATEMENT ... ON TRUNCATE trigger before real audit data exists.
    private void GuardAuditTrailIsAppendOnly()
    {
        foreach (var entry in ChangeTracker.Entries<AuditEntry>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Audit entries are append-only: an existing entry cannot be modified or deleted.");
            }
        }
    }
}
