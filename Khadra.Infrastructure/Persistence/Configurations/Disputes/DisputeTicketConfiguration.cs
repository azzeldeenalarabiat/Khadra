using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.Disputes;

internal sealed class DisputeTicketConfiguration : IEntityTypeConfiguration<DisputeTicket>
{
    public void Configure(EntityTypeBuilder<DisputeTicket> entity)
    {
        ConfigureAggregate(entity, "dispute_tickets");

        ConfigureId(entity.Property(ticket => ticket.BookingId));
        ConfigureId(entity.Property(ticket => ticket.OpenedByUserId));
        ConfigureId(entity.Property(ticket => ticket.AssignedAdminId));
        ConfigureEnumeration(entity.Property(ticket => ticket.OpenedByParty), 20);
        ConfigureEnumeration(entity.Property(ticket => ticket.Status), 20);
        entity.Property(ticket => ticket.Reason).HasMaxLength(2000).IsRequired();
        entity.Property(ticket => ticket.OpenedAt).IsRequired();
        entity.Property(ticket => ticket.SlaDeadline).IsRequired();

        // The resolution is a three-way split of the held deposit plus an optional dealer charge.
        // Stored as JSON: Payments will read it whole when it settles, and nothing filters on it.
        entity.OwnsOne(ticket => ticket.Resolution, resolution =>
        {
            resolution.ToJson();
            resolution.OwnsOne(value => value.Deposit, deposit =>
            {
                ConfigureMoney(deposit.OwnsOne(split => split.DepositHeld));
                ConfigureMoney(deposit.OwnsOne(split => split.RefundToCustomer));
                ConfigureMoney(deposit.OwnsOne(split => split.RetainedByPlatform));
                ConfigureMoney(deposit.OwnsOne(split => split.TransferredToDealer));
            });
            ConfigureMoney(resolution.OwnsOne(value => value.DealerCharge));
            resolution.Property(value => value.ResolvedByAdminId).HasConversion(IdConverter);
        });

        entity.HasMany(ticket => ticket.Statements)
            .WithOne()
            .HasForeignKey(statement => statement.TicketId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.Metadata.FindNavigation(nameof(DisputeTicket.Statements))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // At most ONE live ticket per booking, enforced by the database rather than promised by a
        // comment. IDisputeTicketRepository.GetLiveByBookingAsync uses SingleOrDefault on the strength
        // of this rule; without the constraint, two concurrent opens produce two live tickets and that
        // booking's reads throw for good. A partial unique index is valid in both PostgreSQL and
        // SQLite, so the persistence tests exercise the same guarantee production has.
        entity.HasIndex(ticket => ticket.BookingId)
            .IsUnique()
            .HasFilter("status IN ('Open', 'UnderReview')");
        entity.HasIndex(ticket => ticket.Status);
        // The overdue count and the attention queue both ask for live tickets past their deadline.
        entity.HasIndex(ticket => ticket.SlaDeadline);

        // Like a booking, a ticket is a record of a decision. Withdrawn is its delete.
    }

    private static void ConfigureMoney<TOwner>(OwnedNavigationBuilder<TOwner, Money> money)
        where TOwner : class
    {
        money.Property(value => value.Amount).HasPrecision(18, 3);
        money.Property(value => value.CurrencyCode).HasMaxLength(3);
    }
}

internal sealed class DisputeStatementConfiguration : IEntityTypeConfiguration<DisputeStatement>
{
    public void Configure(EntityTypeBuilder<DisputeStatement> entity)
    {
        entity.ToTable("dispute_statements");
        entity.HasKey(statement => statement.Id);
        entity.Property(statement => statement.Id).HasConversion(IdConverter).ValueGeneratedNever();

        ConfigureId(entity.Property(statement => statement.TicketId));
        ConfigureId(entity.Property(statement => statement.AuthorUserId));
        ConfigureEnumeration(entity.Property(statement => statement.Party), 20);
        entity.Property(statement => statement.Body).HasMaxLength(4000).IsRequired();
        entity.Property(statement => statement.CreatedAt).IsRequired();

        entity.PrimitiveCollection(statement => statement.EvidenceStorageKeys)
            .HasField("_evidenceStorageKeys")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        entity.HasIndex(statement => new { statement.TicketId, statement.CreatedAt });
    }
}
