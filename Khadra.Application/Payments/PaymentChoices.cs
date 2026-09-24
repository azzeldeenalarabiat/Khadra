using CSharpFunctionalExtensions;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;

namespace Khadra.Application.Payments;

/// <summary>One way of paying an approved booking, fully worked out.</summary>
/// <param name="BookingAmount">What the payment puts towards the booking (the deposit, or the total).</param>
/// <param name="ProcessingFee">The optional card-processing fee on top; zero for a deposit.</param>
/// <param name="ChargedNow">What the provider will capture: <paramref name="BookingAmount"/> + fee.</param>
/// <param name="RemainingAfter">What is still owed on the booking once this payment succeeds.</param>
public sealed record PaymentChoice(
    PaymentPurpose Purpose,
    Money BookingAmount,
    Money ProcessingFee,
    Money ChargedNow,
    Money RemainingAfter);

/// <summary>
/// The two ways an approved booking can be paid (owner, 2026-09-24): the mandatory deposit, or the full
/// amount. Computed HERE and only here, so the website, the app, the checkout and the receipt can
/// never disagree about a figure.
/// </summary>
/// <remarks>
/// Every figure comes from the booking's frozen pricing and the fee policy in force when the checkout
/// opens; the payment row then freezes what it was opened with. `RemainingBalance` exists in the
/// model and is deliberately NOT offered in this release.
/// </remarks>
public static class PaymentChoices
{
    /// <summary>The purposes a customer may choose between today, in the order they are shown.</summary>
    public static IReadOnlyList<PaymentPurpose> Offered { get; } = [PaymentPurpose.Deposit, PaymentPurpose.FullPayment];

    public static IReadOnlyList<PaymentChoice> For(Booking booking, ProcessingFeePolicy fee) =>
        Offered.Select(purpose => Of(booking, purpose, fee)).ToList();

    public static Result<PaymentChoice, Error> Choose(Booking booking, PaymentPurpose purpose, ProcessingFeePolicy fee)
    {
        ArgumentNullException.ThrowIfNull(purpose);
        return Offered.Contains(purpose) ? Of(booking, purpose, fee) : PaymentErrors.PurposeUnavailable;
    }

    /// <summary>The live fee policy, from the business rules. Anything unset reads as "no fee".</summary>
    public static ProcessingFeePolicy PolicyFrom(BusinessRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.ProcessingFee is not { Enabled: true } configured)
            return ProcessingFeePolicy.Disabled;

        return new ProcessingFeePolicy(
            true,
            Percentage.FromValidated(configured.Percent),
            Enumeration.FromName<ProcessingFeeBasis>(configured.Basis),
            configured.Refundable);
    }

    private static PaymentChoice Of(Booking booking, PaymentPurpose purpose, ProcessingFeePolicy fee)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(fee);

        var currency = booking.Pricing.CurrencyCode;
        // Fresh Money values throughout: the booking's own instances are tracked by EF, and one owned
        // instance reachable from two places is the pattern the architecture rules forbid.
        var total = Money.Create(booking.Pricing.TotalPrice.Amount, currency);
        var deposit = Money.Create(booking.Pricing.DepositAmount.Amount, currency);

        var bookingAmount = purpose == PaymentPurpose.FullPayment ? total : deposit;
        var processingFee = fee.FeeFor(purpose, total, deposit);
        return new PaymentChoice(
            purpose,
            bookingAmount,
            processingFee,
            bookingAmount.Add(processingFee),
            Money.Create(Math.Max(0m, total.Amount - bookingAmount.Amount), currency));
    }
}
