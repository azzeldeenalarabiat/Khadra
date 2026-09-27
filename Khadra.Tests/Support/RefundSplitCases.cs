using Khadra.Domain.Common;
using Khadra.Domain.Payments;

namespace Khadra.Tests.Support;

/// <summary>
/// A refund recorded through the aggregate for every reason, fee and refundability (payments Phase 5):
/// the cases behind both the domain matrix and the PostgreSQL proof that the migration's SQL gives the
/// refunds recorded before the split was stored the split the aggregate stores now.
/// </summary>
internal static class RefundSplitCases
{
    public const decimal Fee = 4.5m;

    public static IEnumerable<(RefundReason Reason, decimal Fee, bool Refundable)> All() =>
        from reason in Enumeration.GetAll<RefundReason>()
        from fee in new[] { 0m, Fee }
        from refundable in new[] { true, false }
        select (reason, fee, refundable);

    /// <summary>What the fee part must be — the rule, restated as the owner decided it (2026-09-24/26).</summary>
    public static decimal ExpectedFeePart(RefundReason reason, decimal fee, bool refundable, decimal refundAmount) =>
        fee == 0m ? 0m
        : reason == RefundReason.OrphanedCapture ? Math.Min(fee, refundAmount)
        : (reason.ReturnsWholePayment || reason == RefundReason.EndedBeforePickup) && refundable ? Math.Min(fee, refundAmount)
        : 0m;

    /// <summary>A payment with one refund of <paramref name="reason"/>, each through the aggregate's own door.</summary>
    public static (Payment Payment, Refund Refund) Record(RefundReason reason, decimal fee, bool refundable, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (reason == RefundReason.OrphanedCapture)
        {
            var orphaned = Opened(fee, refundable, now);
            if (orphaned.Orphan(Money.Create(orphaned.Amount.Amount, orphaned.Amount.CurrencyCode), now, "booking.not_awaiting_payment", now).IsFailure)
                throw new InvalidOperationException("The test capture could not be orphaned.");
            return (orphaned, orphaned.Refunds.Single());
        }

        var inFull = reason.ReturnsWholePayment || reason == RefundReason.EndedBeforePickup;
        var (booking, payment) = Build.PaidBooking(inFull: inFull, fee: fee, feeRefundable: refundable, now: now);
        var refund =
            reason.ReturnsWholePayment ? payment.RefundWholePayment(reason, now).Value
            : reason == RefundReason.EndedBeforePickup
                ? payment.RefundAboveDeposit(booking.Pricing.TotalPrice.Subtract(booking.Pricing.DepositAmount), now).Value
            : reason == RefundReason.DisputeWindowClosed ? payment.RefundHeldDeposit(booking.Pricing.DepositAmount, now).Value
            : payment.RequestRefund(Money.Jod(9m), Id.New(), now).Value;
        return (payment, refund ?? throw new InvalidOperationException($"No {reason.Name} refund was recorded."));
    }

    /// <summary>An attempt that reached its provider and has not captured: what a late or stray capture lands on.</summary>
    public static Payment Opened(decimal fee, bool refundable, DateTimeOffset now)
    {
        var payment = Payment.Open(
            Id.New(),
            Id.New(),
            Money.Jod(18m + fee),
            "TestProvider",
            now.AddMinutes(30),
            now,
            PaymentPurpose.Deposit,
            Money.Jod(fee),
            refundable);
        if (payment.AttachProviderSession("sess_" + payment.Id.Value.ToString("N"), "https://provider.test/checkout").IsFailure)
            throw new InvalidOperationException("The test payment could not reach its provider.");
        return payment;
    }
}
