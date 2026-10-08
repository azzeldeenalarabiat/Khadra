using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Khadra.Infrastructure.Persistence.Configurations.IdentityAccess;

// Failed sign-ins per account name (pre-launch item 51). Infrastructure only, like the document-number series: the
// count moves by one atomic upsert, so two attempts at once can never both read "seven" and both write "eight".
internal sealed class SignInThrottleEntryConfiguration : IEntityTypeConfiguration<SignInThrottleEntry>
{
    public void Configure(EntityTypeBuilder<SignInThrottleEntry> entity)
    {
        entity.ToTable(
            "sign_in_throttles",
            table => table.HasCheckConstraint("ck_sign_in_throttles_failures", "failures >= 1"));
        entity.HasKey(row => row.SubjectHash);
        entity.Property(row => row.SubjectHash).HasMaxLength(SignInThrottleEntry.SubjectHashLength).IsFixedLength();
        entity.Property(row => row.WindowStartedAt).IsRequired();
        entity.Property(row => row.Failures).IsRequired();
        entity.Property(row => row.BlockedUntil);
        // The sweep deletes by the window's start.
        entity.HasIndex(row => row.WindowStartedAt);
    }
}
