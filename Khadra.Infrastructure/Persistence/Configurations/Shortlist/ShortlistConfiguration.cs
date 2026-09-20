using Khadra.Domain.Shortlist;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.Shortlist;

/// <summary>
/// The cars a customer has saved.
/// </summary>
/// <remarks>
/// <para>
/// The aggregate's id IS the customer's, so there is no separate customer column and no index for
/// one: "this person's shortlist" is a primary-key lookup, and two concurrent first saves collide on
/// the key instead of creating two lists.
/// </para>
/// <para>
/// NOT soft-deletable, and deliberately. Soft delete exists here for records the platform must be
/// able to account for afterwards — a booking, a dealership, an account. A shortlist is browsing
/// interest; a removed entry is a customer saying they are no longer interested, and keeping a
/// tombstone of that would be retaining personal data for no purpose anyone could name. The row
/// goes.
/// </para>
/// <para>
/// No FK to <c>vehicles</c>: Fleet is another bounded context and the reference is by id, like every
/// other cross-context reference on this platform. That is also what lets an entry outlive the
/// listing being hidden, put into maintenance or soft-deleted — which it must, because
/// Maintenance → Hidden → Active is a normal round trip and a list that edited itself on the way
/// through would be losing a customer's choices without being asked.
/// </para>
/// </remarks>
internal sealed class ShortlistConfiguration : IEntityTypeConfiguration<CustomerShortlist>
{
    public void Configure(EntityTypeBuilder<CustomerShortlist> entity)
    {
        ConfigureAggregate(entity, "customer_shortlists");

        entity.Property(shortlist => shortlist.CreatedAt).IsRequired();

        // `CustomerId` is a read-only projection of `Id`; mapping it would try to write the key
        // twice under two column names.
        entity.Ignore(shortlist => shortlist.CustomerId);

        // The ordered projection, not a mapped collection. EF binds the backing field.
        entity.Ignore(shortlist => shortlist.Entries);
        entity.Ignore(shortlist => shortlist.Count);

        entity.HasMany<ShortlistEntry>("_entries")
            .WithOne()
            .HasForeignKey(entry => entry.ShortlistId)
            // Every FK on this platform restricts. A shortlist is never hard-deleted, so this is
            // the convention holding rather than a decision about cascade.
            .OnDelete(DeleteBehavior.Restrict);

        entity.Navigation("_entries").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ShortlistEntryConfiguration : IEntityTypeConfiguration<ShortlistEntry>
{
    public void Configure(EntityTypeBuilder<ShortlistEntry> entity)
    {
        entity.ToTable("shortlist_entries");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).HasConversion(IdConverter).ValueGeneratedNever();

        ConfigureId(entity.Property(item => item.VehicleId));
        entity.Property(item => item.VehicleId).IsRequired();
        entity.Property(item => item.SavedAt).IsRequired();

        ConfigureId(entity.Property(item => item.ShortlistId));
        entity.Property(item => item.ShortlistId).IsRequired();

        // One entry per car per customer. The aggregate checks it too, and the check is the one that
        // produces a sensible answer -- but two taps on a slow connection are two requests and the
        // read-then-write between them loses that race. This is what makes it impossible.
        entity.HasIndex(item => new { item.ShortlistId, item.VehicleId }).IsUnique();

        // The saved list is read newest-first, and the heart on a catalogue card asks "is this car in
        // this customer's list". Both go through the owner column, which the key above already leads
        // with; this adds the date the list is ordered by.
        entity.HasIndex(item => new { item.ShortlistId, item.SavedAt });
    }
}
