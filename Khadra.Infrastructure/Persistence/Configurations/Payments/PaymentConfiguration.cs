using Khadra.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static Khadra.Infrastructure.Persistence.Configurations.ModelConventions;

namespace Khadra.Infrastructure.Persistence.Configurations.Payments;

/// <summary>
/// One checkout attempt, and the refunds hanging off it.
/// </summary>
/// <remarks>
/// A payment is NOT soft-deletable, for the same reason a booking is not: it is a financial record,
/// and its terminal statuses are its deletes.
/// </remarks>
internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> entity)
    {
        ConfigureAggregate(entity, "payments");

        // Cross-context references by id only: no FK to bookings or users, per the architecture rule.
        ConfigureId(entity.Property(payment => payment.BookingId));
        entity.Property(payment => payment.BookingId).IsRequired();
        ConfigureId(entity.Property(payment => payment.CustomerId));
        entity.Property(payment => payment.CustomerId).IsRequired();

        // Owned columns rather than JSON: these get summed and compared. A JSON money column cannot
        // be aggregated in SQL, and an admin's "what did this booking take" would have to load every
        // row to add two numbers.
        entity.OwnsOne(payment => payment.Amount, money =>
        {
            money.Property(value => value.Amount).HasColumnName("amount").HasPrecision(18, 3).IsRequired();
            money.Property(value => value.CurrencyCode).HasColumnName("currency").HasMaxLength(3).IsRequired();
        });
        entity.Navigation(payment => payment.Amount).IsRequired();

        entity.OwnsOne(payment => payment.AmountCaptured, money =>
        {
            money.Property(value => value.Amount).HasColumnName("amount_captured").HasPrecision(18, 3);
            money.Property(value => value.CurrencyCode).HasColumnName("captured_currency").HasMaxLength(3);
        });

        ConfigureEnumeration(entity.Property(payment => payment.Status), 20);
        entity.Property(payment => payment.Provider).HasMaxLength(30).IsRequired();
        entity.Property(payment => payment.ProviderReference).HasMaxLength(200);
        entity.Property(payment => payment.CheckoutUrl).HasMaxLength(2000);
        entity.Property(payment => payment.ExpiresAt).IsRequired();
        entity.Property(payment => payment.FailureCode).HasMaxLength(100);
        entity.Property(payment => payment.OrphanReason).HasMaxLength(100);
        entity.Property(payment => payment.CreatedAt).IsRequired();

        entity.HasMany(payment => payment.Refunds)
            .WithOne()
            .HasForeignKey(refund => refund.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.Metadata
            .FindNavigation(nameof(Payment.Refunds))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // The reference a webhook resolves by. Unique WITHIN a provider, because a reference means
        // nothing outside the provider that issued it, and filtered so the many rows that never got
        // an answer do not collide on null.
        entity.HasIndex(payment => new { payment.Provider, payment.ProviderReference })
            .IsUnique()
            .HasFilter("provider_reference IS NOT NULL");

        entity.HasIndex(payment => payment.BookingId);

        // ONE live attempt per booking, enforced by the database rather than by the handler.
        //
        // The handler's read-then-insert loses the race between two taps on a slow connection, and
        // what it would produce is two provider sessions for one deposit -- a customer who can pay
        // the same booking twice, with the second capture landing on a booking that already carries
        // another payment's id and having to be refunded. A partial unique index makes it impossible
        // instead of unlikely.
        //
        // Partial rather than plain, because the terminal rows must be allowed to pile up: a customer
        // whose card is declined three times has three Failed attempts and is entitled to a fourth.
        // Both Postgres and SQLite support the filter, so the persistence tests exercise the real
        // constraint rather than a stand-in.
        entity.HasIndex(payment => payment.BookingId)
            .IsUnique()
            .HasDatabaseName("ux_payments_one_live_attempt_per_booking")
            .HasFilter("status IN ('Initiated', 'Pending')");

        // The sweep that closes attempts whose provider never sent an expiry notice.
        entity.HasIndex(payment => new { payment.Status, payment.ExpiresAt });
    }
}

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> entity)
    {
        entity.ToTable("payment_refunds");
        entity.HasKey(refund => refund.Id);
        entity.Property(refund => refund.Id).HasConversion(IdConverter).ValueGeneratedNever();
        entity.Property<DateTimeOffset?>(KhadraDbContext.UpdatedAtShadowProperty);

        ConfigureId(entity.Property(refund => refund.PaymentId));
        entity.Property(refund => refund.PaymentId).IsRequired();

        entity.OwnsOne(refund => refund.Amount, money =>
        {
            money.Property(value => value.Amount).HasColumnName("amount").HasPrecision(18, 3).IsRequired();
            money.Property(value => value.CurrencyCode).HasColumnName("currency").HasMaxLength(3).IsRequired();
        });
        entity.Navigation(refund => refund.Amount).IsRequired();

        ConfigureEnumeration(entity.Property(refund => refund.Reason), 30);
        ConfigureEnumeration(entity.Property(refund => refund.Status), 20);
        ConfigureId(entity.Property(refund => refund.DisputeTicketId));
        entity.Property(refund => refund.ProviderReference).HasMaxLength(200);
        entity.Property(refund => refund.FailureCode).HasMaxLength(100);
        entity.Property(refund => refund.RequestedAt).IsRequired();

        // The sweep's query: everything still owed, oldest first.
        entity.HasIndex(refund => new { refund.Status, refund.RequestedAt });
    }
}

/// <summary>
/// Every provider notification, once each.
/// </summary>
/// <remarks>
/// <para>
/// The unique index below IS the replay guard for the whole payments feature. It is not a hint and
/// not an optimisation: the webhook handler inserts a row here in the same transaction as whatever
/// the event caused, and a duplicate delivery therefore rolls the WHOLE effect back rather than
/// applying it twice. A handler that read this table first and wrote afterwards would leave a window
/// between the two in which two concurrent deliveries both pass the read.
/// </para>
/// <para>
/// It stores no raw payload. A provider body carries the cardholder's name, their billing address and
/// usually an email — none of which this platform needs, and all of which it would then have to
/// protect, expire and answer subject-access requests about.
/// </para>
/// </remarks>
internal sealed class ProviderEventReceiptConfiguration : IEntityTypeConfiguration<ProviderEventReceipt>
{
    public void Configure(EntityTypeBuilder<ProviderEventReceipt> entity)
    {
        ConfigureAggregate(entity, "payment_provider_events");

        entity.Property(receipt => receipt.Provider).HasMaxLength(30).IsRequired();
        entity.Property(receipt => receipt.ProviderEventId).HasMaxLength(200).IsRequired();
        entity.Property(receipt => receipt.ProviderReference).HasMaxLength(200);
        entity.Property(receipt => receipt.Kind).HasMaxLength(40).IsRequired();
        ConfigureId(entity.Property(receipt => receipt.PaymentId));
        ConfigureEnumeration(entity.Property(receipt => receipt.Outcome), 20);
        entity.Property(receipt => receipt.Amount).HasPrecision(18, 3);
        entity.Property(receipt => receipt.CurrencyCode).HasMaxLength(3);
        entity.Property(receipt => receipt.ReceivedAt).IsRequired();

        entity.HasIndex(receipt => new { receipt.Provider, receipt.ProviderEventId }).IsUnique();
        entity.HasIndex(receipt => receipt.PaymentId);
    }
}
