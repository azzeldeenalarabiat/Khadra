using Khadra.Application.Bookings;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Bookings;

/// <summary>
/// A booking says how it was paid (E2E F54, Fix & Polish Wave 3 E8): every booking used to report <c>DepositOnly</c>,
/// a booking paid in full included. The confirming payment's purpose now sets it, through the one seam where Payments
/// touches a booking. No client reads the field; it is kept true for whoever does next.
/// </summary>
public sealed class PaymentOptionInStepTests
{
    private static Payment Captured(Booking booking, bool inFull)
    {
        var part = inFull ? booking.Pricing.TotalPrice : booking.Pricing.DepositAmount;
        var charged = Money.Create(part.Amount, part.CurrencyCode);
        var payment = Payment.Open(
            booking.Id, booking.CustomerId, charged, PaymentProviders.Sandbox, Build.Now.AddMinutes(30), Build.Now,
            inFull ? PaymentPurpose.FullPayment : PaymentPurpose.Deposit, Money.Jod(0m), true);
        Assert.True(payment.AttachProviderSession("sess_" + booking.Reference.Value, "https://provider.test/checkout").IsSuccess);
        Assert.True(payment.Apply(Money.Create(charged.Amount, charged.CurrencyCode), Build.Now, Build.Now).IsSuccess);
        return payment;
    }

    [Fact]
    public void A_booking_paid_in_full_says_so()
    {
        var booking = Build.ApprovedBooking();

        Assert.True(BookingDepositSettlement.Confirm(booking, Captured(booking, inFull: true), Build.Now).IsSuccess);

        Assert.Same(PaymentOption.FullUpfront, booking.PaymentOption);
        Assert.True(booking.IsPaidInFull);
    }

    [Fact]
    public void A_booking_paid_by_its_deposit_keeps_DepositOnly()
    {
        var booking = Build.ApprovedBooking();

        Assert.True(BookingDepositSettlement.Confirm(booking, Captured(booking, inFull: false), Build.Now).IsSuccess);

        Assert.Same(PaymentOption.DepositOnly, booking.PaymentOption);
    }

    [Fact]
    public void A_retried_confirmation_of_the_same_payment_changes_nothing()
    {
        var booking = Build.ApprovedBooking();
        var payment = Captured(booking, inFull: true);
        Assert.True(BookingDepositSettlement.Confirm(booking, payment, Build.Now).IsSuccess);

        Assert.True(booking.ConfirmPayment(payment.Id, payment.AppliedToBooking, Build.Now.AddMinutes(5), PaymentOption.DepositOnly).IsSuccess);

        Assert.Same(PaymentOption.FullUpfront, booking.PaymentOption);
    }

    [Fact]
    public void A_booking_not_yet_paid_keeps_the_option_it_was_made_with()
    {
        Assert.Same(PaymentOption.DepositOnly, Build.ApprovedBooking().PaymentOption);
    }
}
