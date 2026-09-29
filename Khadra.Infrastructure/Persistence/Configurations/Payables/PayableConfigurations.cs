using Khadra.Domain.Common;
using Khadra.Domain.Payables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.Payables;

// The office payables ledger (payments Phase 8). A payable's figures and lines are frozen: the application never
// changes them, and the migration adds a trigger that refuses any UPDATE touching anything but its settlement, and
// every DELETE and TRUNCATE. Settlements, their lines and their voids are append-only like documents. The CHECKs on
// money are PostgreSQL's only, in the migration: SQLite, which the unit tests run on, compares decimals as floating
// point. Every index is created with its table, while it is empty.
internal sealed class OfficePayableConfiguration : IEntityTypeConfiguration<OfficePayable>
{
    public void Configure(EntityTypeBuilder<OfficePayable> entity)
    {
        ConfigureAggregate(entity, "office_payables");

        ConfigureId(entity.Property(payable => payable.BookingId)).IsRequired();
        ConfigureId(entity.Property(payable => payable.DealerId)).IsRequired();
        entity.Property(payable => payable.BookingReference).HasMaxLength(OfficePayable.BookingReferenceMaxLength).IsRequired();
        entity.Property(payable => payable.Currency).HasMaxLength(3).IsRequired();
        entity.Property(payable => payable.Provider).HasMaxLength(OfficePayable.ProviderMaxLength).IsRequired();
        ConfigureEnumeration(entity.Property(payable => payable.Outcome), 30);
        entity.Property(payable => payable.FinalAt).IsRequired();
        entity.Property(payable => payable.RecordedAt).IsRequired();
        entity.Property(payable => payable.OfficeMoney).HasPrecision(18, OfficePayable.AmountScale).IsRequired();
        entity.Property(payable => payable.Commission).HasPrecision(18, OfficePayable.AmountScale).IsRequired();
        entity.Property(payable => payable.OfficeCharges).HasPrecision(18, OfficePayable.AmountScale).IsRequired();
        entity.Property(payable => payable.Net).HasPrecision(18, OfficePayable.AmountScale).IsRequired();
        entity.Property(payable => payable.CalculatorVersion).IsRequired();
        ConfigureId(entity.Property(payable => payable.SettlementId));
        entity.Property(payable => payable.SettledAt);
        entity.Ignore(payable => payable.IsTest);
        entity.Ignore(payable => payable.IsSettled);

        // The booking, the office and the people are other contexts', referenced by id only. The settlement is this
        // context's, so a real key, restricting as every constraint here is.
        entity.HasOne<OfficeSettlement>()
            .WithMany()
            .HasForeignKey(payable => payable.SettlementId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_office_payables_settlement");

        // The aggregate never removes a line, so Restrict: there is nothing to sever.
        entity.HasMany(payable => payable.Lines)
            .WithOne()
            .HasForeignKey(line => line.PayableId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_office_payable_lines_payable");
        entity.Metadata.FindNavigation(nameof(OfficePayable.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // At most one payable per booking: two processes recording the same booking collide here.
        entity.HasIndex(payable => payable.BookingId).IsUnique().HasDatabaseName("ix_office_payables_booking");
        // What a settlement may close — an office's open payables in one currency and kind of money — and the
        // balances, which read the open ones only: the size of what is owed, not of history.
        entity.HasIndex(payable => new { payable.DealerId, payable.Currency, payable.Provider })
            .HasFilter("settlement_id IS NULL")
            .HasDatabaseName("ix_office_payables_open");
        entity.HasIndex(payable => payable.SettlementId)
            .HasFilter("settlement_id IS NOT NULL")
            .HasDatabaseName("ix_office_payables_settlement");
        // The lists, newest outcome first, in a total order so a page boundary never drops a row.
        entity.HasIndex(payable => new { payable.FinalAt, payable.Id })
            .IsDescending(true, true)
            .HasDatabaseName("ix_office_payables_final");
        entity.HasIndex(payable => new { payable.DealerId, payable.FinalAt, payable.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_office_payables_dealer_final");

        entity.ToTable(table =>
        {
            table.HasCheckConstraint("ck_office_payables_calculator_version", "calculator_version >= 1");
            table.HasCheckConstraint("ck_office_payables_settled", "(settlement_id IS NULL) = (settled_at IS NULL)");
        });
    }
}

internal sealed class OfficePayableLineConfiguration : IEntityTypeConfiguration<OfficePayableLine>
{
    public void Configure(EntityTypeBuilder<OfficePayableLine> entity)
    {
        entity.ToTable("office_payable_lines");
        entity.HasKey(line => line.Id);
        entity.Property(line => line.Id).HasConversion(IdConverter).ValueGeneratedNever();

        ConfigureId(entity.Property(line => line.PayableId)).IsRequired();
        entity.Property(line => line.Position).IsRequired();
        ConfigureEnumeration(entity.Property(line => line.Kind), 20);
        entity.Property(line => line.Amount).HasPrecision(18, OfficePayable.AmountScale).IsRequired();
        // The dispute ticket a share or a charge came from: another context's, by id only.
        ConfigureId(entity.Property(line => line.SourceId));
        entity.Ignore(line => line.SignedAmount);

        entity.HasIndex(line => new { line.PayableId, line.Position }).IsUnique();

        entity.ToTable(table => table.HasCheckConstraint("ck_office_payable_lines_position", "position >= 1"));
    }
}

internal sealed class OfficeSettlementConfiguration : IEntityTypeConfiguration<OfficeSettlement>
{
    public void Configure(EntityTypeBuilder<OfficeSettlement> entity)
    {
        ConfigureAggregate(entity, "office_settlements");

        entity.Property(settlement => settlement.Number)
            .HasColumnName("settlement_number")
            .HasMaxLength(OfficeSettlement.NumberMaxLength)
            .IsRequired();
        ConfigureId(entity.Property(settlement => settlement.DealerId)).IsRequired();
        entity.Property(settlement => settlement.Currency).HasMaxLength(3).IsRequired();
        entity.Property(settlement => settlement.Provider).HasMaxLength(OfficePayable.ProviderMaxLength).IsRequired();
        ConfigureEnumeration(entity.Property(settlement => settlement.Direction), 10);
        entity.Property(settlement => settlement.Amount).HasPrecision(18, OfficePayable.AmountScale).IsRequired();
        entity.Property(settlement => settlement.PaidOn).IsRequired();
        entity.Property(settlement => settlement.Reference).HasMaxLength(OfficeSettlement.ReferenceMaxLength);
        entity.Property(settlement => settlement.Note).HasMaxLength(OfficeSettlement.NoteMaxLength);
        ConfigureId(entity.Property(settlement => settlement.RecordedByAdminId)).IsRequired();
        entity.Property(settlement => settlement.RecordedAt).IsRequired();
        entity.Ignore(settlement => settlement.IsTest);

        entity.HasMany(settlement => settlement.Lines)
            .WithOne()
            .HasForeignKey(line => line.SettlementId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_office_settlement_lines_settlement");
        entity.Metadata.FindNavigation(nameof(OfficeSettlement.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        entity.HasIndex(settlement => settlement.Number).IsUnique();
        // An office's settlements, newest first, in a total order.
        entity.HasIndex(settlement => new { settlement.DealerId, settlement.RecordedAt, settlement.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_office_settlements_dealer_recorded");
        entity.HasIndex(settlement => settlement.PaidOn);
    }
}

internal sealed class OfficeSettlementLineConfiguration : IEntityTypeConfiguration<OfficeSettlementLine>
{
    public void Configure(EntityTypeBuilder<OfficeSettlementLine> entity)
    {
        entity.ToTable("office_settlement_lines");
        entity.HasKey(line => line.Id);
        entity.Property(line => line.Id).HasConversion(IdConverter).ValueGeneratedNever();

        ConfigureId(entity.Property(line => line.SettlementId)).IsRequired();
        ConfigureId(entity.Property(line => line.PayableId)).IsRequired();
        entity.Property(line => line.Net).HasPrecision(18, OfficePayable.AmountScale).IsRequired();

        entity.HasOne<OfficePayable>()
            .WithMany()
            .HasForeignKey(line => line.PayableId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_office_settlement_lines_payable");

        // A settlement names each payable once; a payable's history of settlements is read by it.
        entity.HasIndex(line => new { line.SettlementId, line.PayableId }).IsUnique();
        entity.HasIndex(line => line.PayableId);
    }
}

// A void, keyed by the settlement's own id: at most one per settlement. Append-only.
internal sealed class OfficeSettlementVoidConfiguration : IEntityTypeConfiguration<OfficeSettlementVoid>
{
    public void Configure(EntityTypeBuilder<OfficeSettlementVoid> entity)
    {
        ConfigureAggregate(entity, "office_settlement_voids");
        entity.Property(voided => voided.Id).HasColumnName("settlement_id");
        entity.Ignore(voided => voided.SettlementId);

        entity.Property(voided => voided.VoidedAt).IsRequired();
        ConfigureId(entity.Property(voided => voided.VoidedByAdminId)).IsRequired();
        entity.Property(voided => voided.Reason).HasMaxLength(OfficeSettlementVoid.MaxReasonLength).IsRequired();

        entity.HasOne<OfficeSettlement>()
            .WithOne()
            .HasForeignKey<OfficeSettlementVoid>(voided => voided.Id)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_office_settlement_voids_settlement");
    }
}

// Holds on bookings' payables: mutable — a hold is opened, checked and released — and never deleted. One open hold
// per booking and reason, enforced here.
internal sealed class OfficePayableHoldConfiguration : IEntityTypeConfiguration<OfficePayableHold>
{
    public void Configure(EntityTypeBuilder<OfficePayableHold> entity)
    {
        ConfigureAggregate(entity, "office_payable_holds");

        ConfigureId(entity.Property(hold => hold.BookingId)).IsRequired();
        ConfigureId(entity.Property(hold => hold.DealerId)).IsRequired();
        ConfigureId(entity.Property(hold => hold.PayableId));
        ConfigureEnumeration(entity.Property(hold => hold.Reason), 30);
        entity.Property(hold => hold.Detail).HasMaxLength(OfficePayableHold.MaxDetailLength);
        entity.Property(hold => hold.OpenedAt).IsRequired();
        ConfigureId(entity.Property(hold => hold.OpenedByAdminId));
        entity.Property(hold => hold.Checks).IsRequired();
        entity.Property(hold => hold.LastCheckedAt).IsRequired();
        entity.Property(hold => hold.NextCheckAt);
        entity.Property(hold => hold.ReleasedAt);
        ConfigureId(entity.Property(hold => hold.ReleasedByAdminId));
        entity.Property(hold => hold.ReleaseNote).HasMaxLength(OfficePayableHold.MaxDetailLength);
        entity.Ignore(hold => hold.IsOpen);

        entity.HasOne<OfficePayable>()
            .WithMany()
            .HasForeignKey(hold => hold.PayableId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_office_payable_holds_payable");

        entity.HasIndex(hold => new { hold.BookingId, hold.Reason })
            .IsUnique()
            .HasFilter("released_at IS NULL")
            .HasDatabaseName("ix_office_payable_holds_one_open");
        // The balances and the work queue ask for open holds only: the size of what is held, not of history.
        entity.HasIndex(hold => hold.DealerId)
            .HasFilter("released_at IS NULL")
            .HasDatabaseName("ix_office_payable_holds_open_dealer");
        entity.HasIndex(hold => hold.NextCheckAt)
            .HasFilter("released_at IS NULL")
            .HasDatabaseName("ix_office_payable_holds_open_next_check");

        entity.ToTable(table =>
        {
            table.HasCheckConstraint("ck_office_payable_holds_checks", "checks >= 0");
            // A hold that stops a payable being recorded names none; every other one names the payable it holds.
            table.HasCheckConstraint(
                "ck_office_payable_holds_payable",
                "(payable_id IS NULL) = (reason IN ('NeedsReview', 'PenaltyNotWholeDeposit'))");
            // A manual hold is an administrator's, and only an administrator's hold is released by one.
            table.HasCheckConstraint(
                "ck_office_payable_holds_manual",
                "(reason = 'Manual') = (opened_by_admin_id IS NOT NULL) AND (released_by_admin_id IS NULL OR reason = 'Manual')");
        });
    }
}
