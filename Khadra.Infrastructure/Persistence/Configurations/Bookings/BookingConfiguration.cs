using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Fleet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.Bookings;

// The priced offer, the frozen rules and the penalty assessment are deep value objects that nothing
// queries into: the console filters bookings by status, dealer and date, never by a term inside the
// snapshot. They are therefore stored as JSON documents rather than flattened into forty columns,
// which keeps the table readable and keeps a change to BookingTerms from being a schema migration.
//
// Everything the read side does filter on (status, dealer, customer, period, created_at) stays a
// first-class indexed column.
internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> entity)
    {
        ConfigureAggregate(entity, "bookings");

        entity.Property(booking => booking.Reference)
            .HasConversion(reference => reference.Value, value => BookingReference.Create(value).Value)
            .HasMaxLength(20)
            .IsRequired();

        ConfigureId(entity.Property(booking => booking.CustomerId));
        ConfigureId(entity.Property(booking => booking.DealerId));
        ConfigureId(entity.Property(booking => booking.VehicleId));
        ConfigureId(entity.Property(booking => booking.DepositPaymentId));
        ConfigureId(entity.Property(booking => booking.ActedByUserId));
        ConfigureId(entity.Property(booking => booking.ExtendedFromBookingId));

        ConfigureEnumeration(entity.Property(booking => booking.PickupMethod), 20);
        ConfigureEnumeration(entity.Property(booking => booking.PaymentOption), 20);
        ConfigureEnumeration(entity.Property(booking => booking.Status), 20);
        entity.Property(booking => booking.CancelledBy)
            .HasConversion(party => party!.Name, name => Enumeration.FromName<BookingParty>(name))
            .HasMaxLength(20);

        entity.Property(booking => booking.CancellationReason).HasMaxLength(1000);
        entity.Property(booking => booking.CreatedAt).IsRequired();
        entity.Property(booking => booking.PaymentDeadline).IsRequired();

        entity.OwnsOne(booking => booking.Period, period =>
        {
            period.Property(range => range.Start).HasColumnName("period_start").IsRequired();
            period.Property(range => range.End).HasColumnName("period_end").IsRequired();
        });
        entity.Navigation(booking => booking.Period).IsRequired();

        entity.OwnsOne(booking => booking.DeliveryLocation, location =>
        {
            location.Property(point => point.Latitude).HasColumnName("delivery_latitude");
            location.Property(point => point.Longitude).HasColumnName("delivery_longitude");
        });

        // EVERY property of a ToJson value object is mapped by hand. EF Core includes a property by
        // convention only when it has a setter; these value objects are get-only by design, so anything
        // left to convention is silently dropped on the way to the database. That is how every
        // Percentage became {} and how the frozen windows, Days, Reason and AssessedAt were lost --
        // with no error, because a JSON document has no schema to complain.
        entity.OwnsOne(booking => booking.Pricing, pricing =>
        {
            pricing.ToJson();
            ConfigureMoney(pricing.OwnsOne(value => value.DailyRate));
            ConfigureMoney(pricing.OwnsOne(value => value.RentalTotal));
            ConfigureMoney(pricing.OwnsOne(value => value.DeliveryFee));
            ConfigureMoney(pricing.OwnsOne(value => value.TotalPrice));
            ConfigureMoney(pricing.OwnsOne(value => value.DepositAmount));
            ConfigureMoney(pricing.OwnsOne(value => value.BalanceDue));
            ConfigureMoney(pricing.OwnsOne(value => value.SecurityDeposit));
            ConfigurePercentage(pricing.OwnsOne(value => value.DepositPercent));
            pricing.OwnsOne(value => value.Mileage, mileage =>
            {
                mileage.Property(policy => policy.IsUnlimited);
                mileage.Property(policy => policy.DailyLimitKm);
                ConfigureMoney(mileage.OwnsOne(policy => policy.ExcessFeePerKm));
            });
            pricing.Property(value => value.Days);
            pricing.Property(value => value.FuelPolicy)
                .HasConversion(policy => policy.Name, name => Enumeration.FromName<FuelPolicy>(name));
        });
        entity.Navigation(booking => booking.Pricing).IsRequired();

        entity.OwnsOne(booking => booking.Terms, terms =>
        {
            terms.ToJson();
            ConfigurePercentage(terms.OwnsOne(value => value.DepositPercent));
            ConfigurePercentage(terms.OwnsOne(value => value.CommissionPercent));
            ConfigurePercentage(terms.OwnsOne(value => value.CustomerCancellationPenaltyPercent));
            ConfigurePercentage(terms.OwnsOne(value => value.DealerPenaltyMinPercent));
            ConfigurePercentage(terms.OwnsOne(value => value.DealerPenaltyMaxPercent));
            terms.Property(value => value.FreeCancellationWindow);
            terms.Property(value => value.NoShowTimeout);
            terms.Property(value => value.PaymentWindow);
            terms.Property(value => value.PostReturnSettlementWindow);
            terms.Property(value => value.RulesVersion);
        });
        entity.Navigation(booking => booking.Terms).IsRequired();

        entity.OwnsOne(booking => booking.Penalty, penalty =>
        {
            penalty.ToJson();
            ConfigurePercentage(penalty.OwnsOne(value => value.MinPercent));
            ConfigurePercentage(penalty.OwnsOne(value => value.MaxPercent));
            ConfigureMoney(penalty.OwnsOne(value => value.MinAmount));
            ConfigureMoney(penalty.OwnsOne(value => value.MaxAmount));
            penalty.Property(value => value.Reason).HasMaxLength(500);
            penalty.Property(value => value.AssessedAt);
            penalty.Property(value => value.AttributedTo)
                .HasConversion(party => party.Name, name => Enumeration.FromName<BookingParty>(name));
        });

        entity.HasMany(booking => booking.Handovers)
            .WithOne()
            .HasForeignKey(handover => handover.BookingId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.Metadata.FindNavigation(nameof(Booking.Handovers))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        entity.HasMany(booking => booking.StatusHistory)
            .WithOne()
            .HasForeignKey(change => change.BookingId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.Metadata.FindNavigation(nameof(Booking.StatusHistory))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        entity.HasIndex(booking => booking.Reference).IsUnique();
        entity.HasIndex(booking => booking.DealerId);
        entity.HasIndex(booking => booking.CustomerId);
        entity.HasIndex(booking => booking.Status);
        // The dashboard counts bookings created per day over a rolling window.
        entity.HasIndex(booking => booking.CreatedAt).IsDescending();

        // A booking is a financial record: Cancelled and Expired are its deletes. No soft-delete flag,
        // and therefore no query filter.
    }

    private static void ConfigureMoney<TOwner>(OwnedNavigationBuilder<TOwner, Money> money)
        where TOwner : class
    {
        money.Property(value => value.Amount).HasPrecision(18, 3);
        money.Property(value => value.CurrencyCode).HasMaxLength(3);
    }

    /// <summary>
    /// Maps a Percentage's single value EXPLICITLY, exactly as ConfigureMoney does for an amount.
    ///
    /// Percentage.Value is a get-only property set through a private constructor, and EF does not
    /// pick it up by convention inside a ToJson-mapped owned type. Left implicit, every rate wrote
    /// itself to the database as an empty object -- which meant BookingTerms, whose whole purpose is
    /// to freeze the rules a booking was made under, was freezing nothing at all. Money escaped the
    /// same fate only because it was always configured by hand.
    /// </summary>
    private static void ConfigurePercentage<TOwner>(OwnedNavigationBuilder<TOwner, Percentage> percentage)
        where TOwner : class
    {
        // 0-100 rounded to four decimals by Percentage.Create.
        percentage.Property(value => value.Value).HasPrecision(7, 4);
    }
}

internal sealed class HandoverRecordConfiguration : IEntityTypeConfiguration<HandoverRecord>
{
    public void Configure(EntityTypeBuilder<HandoverRecord> entity)
    {
        entity.ToTable("booking_handovers");
        entity.HasKey(handover => handover.Id);
        entity.Property(handover => handover.Id).HasConversion(IdConverter).ValueGeneratedNever();

        ConfigureId(entity.Property(handover => handover.BookingId));
        ConfigureId(entity.Property(handover => handover.RecordedByUserId));
        ConfigureEnumeration(entity.Property(handover => handover.Type), 20);
        ConfigureEnumeration(entity.Property(handover => handover.RecordedBy), 20);
        entity.Property(handover => handover.Notes).HasMaxLength(2000);
        entity.Property(handover => handover.FuelLevel).HasPrecision(4, 3);
        entity.Property(handover => handover.RecordedAt).IsRequired();

        entity.OwnsOne(handover => handover.CashCollected, money =>
        {
            money.Property(value => value.Amount).HasColumnName("cash_collected_amount").HasPrecision(18, 3);
            money.Property(value => value.CurrencyCode).HasColumnName("cash_collected_currency").HasMaxLength(3);
        });

        // Spec 5.4: opt-in photos. Stored as a primitive collection because nothing queries them.
        entity.PrimitiveCollection(handover => handover.PhotoStorageKeys)
            .HasField("_photoStorageKeys")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        entity.HasIndex(handover => new { handover.BookingId, handover.Type }).IsUnique();
    }
}

internal sealed class BookingStatusChangeConfiguration : IEntityTypeConfiguration<BookingStatusChange>
{
    public void Configure(EntityTypeBuilder<BookingStatusChange> entity)
    {
        entity.ToTable("booking_status_changes");
        entity.HasKey(change => change.Id);
        entity.Property(change => change.Id).HasConversion(IdConverter).ValueGeneratedNever();

        ConfigureId(entity.Property(change => change.BookingId));
        ConfigureId(entity.Property(change => change.ActorUserId));
        // Null on the first row: the booking came from nowhere.
        entity.Property(change => change.From)
            .HasConversion(status => status!.Name, name => Enumeration.FromName<BookingStatus>(name))
            .HasMaxLength(20);
        ConfigureEnumeration(entity.Property(change => change.To), 20);
        ConfigureEnumeration(entity.Property(change => change.ActorParty), 20);
        entity.Property(change => change.Reason).HasMaxLength(1000);
        entity.Property(change => change.OccurredAt).IsRequired();

        entity.HasIndex(change => new { change.BookingId, change.OccurredAt });
    }
}
