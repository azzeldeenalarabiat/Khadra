using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using Khadra.Domain.Fleet;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.Bookings;

// The priced offer the customer accepted, frozen at request time along with the vehicle terms they
// agreed to. A dealer raising the daily rate or tightening the mileage cap afterwards cannot change
// what this booking costs or allows.
//
// The deposit and the commission are both taken on RentalTotal, deliberately excluding the delivery
// fee: the fee is a pass-through for the driver's trip, not rental revenue to be shared.
public sealed class BookingPricing : ValueObject
{
    public Money DailyRate { get; }
    public int Days { get; }
    public Money RentalTotal { get; }
    public Money DeliveryFee { get; }
    public Money TotalPrice { get; }
    public Percentage DepositPercent { get; }
    public Money DepositAmount { get; }
    // Paid in cash to the dealer at handover; never moves through the platform (spec 5.3).
    public Money BalanceDue { get; }
    // Vehicle terms as agreed, snapshotted so later fleet edits cannot rewrite the contract.
    public Money SecurityDeposit { get; }
    public MileagePolicy Mileage { get; }
    public FuelPolicy FuelPolicy { get; }

    private BookingPricing(
        Money dailyRate,
        int days,
        Money rentalTotal,
        Money deliveryFee,
        Money totalPrice,
        Percentage depositPercent,
        Money depositAmount,
        Money balanceDue,
        Money securityDeposit,
        MileagePolicy mileage,
        FuelPolicy fuelPolicy)
    {
        DailyRate = dailyRate;
        Days = days;
        RentalTotal = rentalTotal;
        DeliveryFee = deliveryFee;
        TotalPrice = totalPrice;
        DepositPercent = depositPercent;
        DepositAmount = depositAmount;
        BalanceDue = balanceDue;
        SecurityDeposit = securityDeposit;
        Mileage = mileage;
        FuelPolicy = fuelPolicy;
    }

    public static Result<BookingPricing, Error> Calculate(
        Money dailyRate,
        int days,
        Money deliveryFee,
        Percentage depositPercent,
        Money securityDeposit,
        MileagePolicy mileage,
        FuelPolicy fuelPolicy)
    {
        ArgumentNullException.ThrowIfNull(dailyRate);
        ArgumentNullException.ThrowIfNull(deliveryFee);
        ArgumentNullException.ThrowIfNull(depositPercent);
        ArgumentNullException.ThrowIfNull(securityDeposit);
        ArgumentNullException.ThrowIfNull(mileage);
        ArgumentNullException.ThrowIfNull(fuelPolicy);

        if (days <= 0)
            return BookingErrors.PeriodTooShort;

        var currency = dailyRate.CurrencyCode;
        if (!string.Equals(deliveryFee.CurrencyCode, currency, StringComparison.Ordinal) ||
            !string.Equals(securityDeposit.CurrencyCode, currency, StringComparison.Ordinal))
        {
            return BookingErrors.CurrencyMismatch;
        }

        var rentalTotal = dailyRate.MultiplyBy(days);
        var totalPrice = rentalTotal.Add(deliveryFee);
        var depositAmount = depositPercent.Of(rentalTotal);
        // The balance is what the customer still owes the dealer in cash: the rental not covered by
        // the deposit, plus the delivery fee, which the driver collects on arrival.
        var balanceDue = rentalTotal.Subtract(depositAmount).Add(deliveryFee);

        return new BookingPricing(
            dailyRate,
            days,
            rentalTotal,
            deliveryFee,
            totalPrice,
            depositPercent,
            depositAmount,
            balanceDue,
            securityDeposit,
            mileage,
            fuelPolicy);
    }

    public string CurrencyCode => TotalPrice.CurrencyCode;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DailyRate;
        yield return Days;
        yield return RentalTotal;
        yield return DeliveryFee;
        yield return TotalPrice;
        yield return DepositPercent;
        yield return DepositAmount;
        yield return BalanceDue;
        yield return SecurityDeposit;
        yield return Mileage;
        yield return FuelPolicy;
    }
}
