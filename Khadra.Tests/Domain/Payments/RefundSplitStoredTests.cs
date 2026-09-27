using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Payments;

/// <summary>
/// The refund split, STORED (owner, 2026-09-26; payments Phase 5): written once when a refund is recorded,
/// by the payment's frozen fee rule — for every reason, with and without a fee, refundable or not — and
/// read back unchanged by <c>FeeInside</c> and <c>BookingMoneyIn</c>. The migration's SQL fills the refunds
/// recorded before the split was stored by the same rule; <c>PostgresRefundSplitBackfillTests</c> proves
/// the two agree.
/// </summary>
public sealed class RefundSplitStoredTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    public static TheoryData<string, decimal, bool> Cases()
    {
        var data = new TheoryData<string, decimal, bool>();
        foreach (var (reason, fee, refundable) in RefundSplitCases.All())
            data.Add(reason.Name, fee, refundable);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_refund_stores_the_split_by_its_payments_frozen_rule(string reasonName, decimal fee, bool refundable)
    {
        var reason = Enumeration.FromName<RefundReason>(reasonName);

        var (payment, refund) = RefundSplitCases.Record(reason, fee, refundable, Now);

        var expected = RefundSplitCases.ExpectedFeePart(reason, fee, refundable, refund.Amount.Amount);
        Assert.Equal(Money.Create(expected, refund.Amount.CurrencyCode), refund.FeePart);
        Assert.Equal(refund.Amount.Subtract(refund.FeePart), refund.BookingPart);
        Assert.Equal(refund.FeePart, payment.FeeInside(refund));
        Assert.Equal(refund.BookingPart, payment.BookingMoneyIn(refund));
    }

    [Fact]
    public void A_capture_in_another_currency_carries_none_of_the_fee()
    {
        var payment = RefundSplitCases.Opened(RefundSplitCases.Fee, refundable: true, Now);

        Assert.True(payment.Orphan(Money.Create(50m, "USD"), Now, "booking.not_awaiting_payment", Now).IsSuccess);

        var refund = Assert.Single(payment.Refunds);
        Assert.Equal(Money.Create(0m, "USD"), refund.FeePart);
        Assert.Equal(Money.Create(50m, "USD"), refund.BookingPart);
    }
}
