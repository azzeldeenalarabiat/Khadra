using Khadra.Domain.Common;
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
        // What the payment is for, and the fee inside its amount (2026-09-24). Every earlier row was a
        // deposit with no fee, which is exactly what the column defaults say.
        ConfigureEnumeration(entity.Property(payment => payment.Purpose), 20);
        entity.Property(payment => payment.Purpose).HasDefaultValueSql("'Deposit'");
        entity.Property<decimal>("_processingFee")
            .HasColumnName("processing_fee")
            .HasPrecision(18, 3)
            .HasDefaultValue(0m)
            .IsRequired();
        // Explicit rather than defaulted in SQL: a bool default of true would be EF's sentinel trap (a
        // false value indistinguishable from "not set"), so every insert writes what the row holds.
        entity.Property<bool>("_feeRefundable")
            .HasColumnName("fee_refundable")
            .IsRequired();
        entity.Property(payment => payment.Provider).HasMaxLength(30).IsRequired();
        entity.Property(payment => payment.ProviderReference).HasMaxLength(200);
        entity.Property(payment => payment.ProviderCaptureReference).HasMaxLength(200);
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

        // ONE capture on ONE attempt (Wave 4, B1). The webhook reads first and records a capture another attempt
        // already holds as an incident; this index is the floor under that read, as the receipt's index is under the
        // replay check, and the handler expects to lose to it by this name.
        entity.HasIndex(payment => new { payment.Provider, payment.ProviderCaptureReference })
            .IsUnique()
            .HasDatabaseName(UniqueConstraintConflictException.ProviderCaptureReferenceConstraint)
            .HasFilter("provider_capture_reference IS NOT NULL");

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

        // The stored split (owner, 2026-09-26; payments Phase 5), in the refund's own currency. The
        // migration that added these filled every earlier refund by Payment.FeeFor's rule, and adds a
        // PostgreSQL CHECK that the parts sum to the amount (not modelled here: SQLite, which the tests
        // run on, would compare decimals as floating point).
        entity.Property<decimal>("_bookingPart")
            .HasColumnName("booking_part")
            .HasPrecision(18, 3)
            .IsRequired();
        entity.Property<decimal>("_feePart")
            .HasColumnName("fee_part")
            .HasPrecision(18, 3)
            .IsRequired();

        ConfigureEnumeration(entity.Property(refund => refund.Reason), 30);
        ConfigureEnumeration(entity.Property(refund => refund.Status), 20);
        ConfigureId(entity.Property(refund => refund.DisputeTicketId));
        entity.Property(refund => refund.ProviderReference).HasMaxLength(200);
        entity.Property(refund => refund.FailureCode).HasMaxLength(100);
        entity.Property(refund => refund.RequestedAt).IsRequired();

        // The back-off (Wave 4, B4; checklist 157): refusals counted once per refused send, and when a refused refund
        // may be sent again. A refund recorded before the count existed reads zero, which its migration writes.
        entity.Property(refund => refund.RefusalCount).IsRequired();
        entity.Property(refund => refund.NextAttemptAt);

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
        entity.Property(receipt => receipt.CaptureReference).HasMaxLength(200);
        entity.Property(receipt => receipt.ReceivedAt).IsRequired();

        entity.HasIndex(receipt => new { receipt.Provider, receipt.ProviderEventId }).IsUnique();
        entity.HasIndex(receipt => receipt.PaymentId);
    }
}

/// <summary>
/// Capture incidents (Wave 4, B1): a notice that money may have moved in a way no booking accounts for.
/// </summary>
/// <remarks>
/// Both references are real foreign keys, RESTRICT: an incident always names a payment and the notice that raised it,
/// and neither may disappear from under it. Same context, so a key is allowed; there are no navigation properties.
/// Unique per receipt, so one notice raises one incident at most.
/// </remarks>
internal sealed class PaymentIncidentConfiguration : IEntityTypeConfiguration<PaymentIncident>
{
    public void Configure(EntityTypeBuilder<PaymentIncident> entity)
    {
        ConfigureAggregate(entity, "payment_incidents");

        ConfigureEnumeration(entity.Property(incident => incident.Kind), 30);
        ConfigureId(entity.Property(incident => incident.PaymentId));
        entity.Property(incident => incident.PaymentId).IsRequired();
        ConfigureId(entity.Property(incident => incident.ReceiptId));
        entity.Property(incident => incident.ReceiptId).IsRequired();
        entity.Property(incident => incident.Provider).HasMaxLength(30).IsRequired();
        entity.Property(incident => incident.CaptureReference).HasMaxLength(200);

        entity.OwnsOne(incident => incident.Reported, money =>
        {
            money.Property(value => value.Amount).HasColumnName("reported_amount").HasPrecision(18, 3).IsRequired();
            money.Property(value => value.CurrencyCode).HasColumnName("reported_currency").HasMaxLength(3).IsRequired();
        });
        entity.Navigation(incident => incident.Reported).IsRequired();
        entity.OwnsOne(incident => incident.Expected, money =>
        {
            money.Property(value => value.Amount).HasColumnName("expected_amount").HasPrecision(18, 3).IsRequired();
            money.Property(value => value.CurrencyCode).HasColumnName("expected_currency").HasMaxLength(3).IsRequired();
        });
        entity.Navigation(incident => incident.Expected).IsRequired();

        ConfigureId(entity.Property(incident => incident.OtherPaymentId));
        entity.Property(incident => incident.DetectedAt).IsRequired();
        ConfigureId(entity.Property(incident => incident.HandledByAdminId));
        entity.Property(incident => incident.HandledNote).HasMaxLength(PaymentIncident.MaxNoteLength);
        entity.Ignore(incident => incident.IsHandled);

        entity.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(incident => incident.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ProviderEventReceipt>()
            .WithMany()
            .HasForeignKey(incident => incident.ReceiptId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasIndex(incident => incident.ReceiptId).IsUnique();
        entity.HasIndex(incident => incident.PaymentId);
        // The attention queue's read: every incident nobody has handled yet.
        entity.HasIndex(incident => incident.DetectedAt).HasFilter("handled_at IS NULL");
    }
}
