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
//
// The two calendar dates are frozen here alongside the day count, and that is not redundancy. The
// count is a fact about a LOCAL calendar, and the zone it was computed in is configuration
// (ReportingTimeZone). Storing only the instants would leave the count re-derivable — and therefore
// re-judgeable — the day that setting changed. Storing the dates the customer agreed to means the
// contract reads the same forever, in the calendar the customer was actually looking at.
public sealed class BookingPricing : ValueObject
{
    public Money DailyRate { get; }
    // The local calendar dates this rental was priced between, in the platform's reporting zone.
    public DateOnly PickupDate { get; }
    public DateOnly ReturnDate { get; }
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

#pragma warning disable CS8618 // EF materialises this value object by writing its backing fields;
    // the public factories remain the only way application code can create one.
    private BookingPricing()
    {
    }
#pragma warning restore CS8618

    private BookingPricing(
        Money dailyRate,
        DateOnly pickupDate,
        DateOnly returnDate,
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
        PickupDate = pickupDate;
        ReturnDate = returnDate;
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

    /// <summary>
    /// Prices a rental between two LOCAL calendar dates.
    /// </summary>
    /// <remarks>
    /// The caller passes dates, not a day count, because there is exactly one right way to count and
    /// this is where it lives (<see cref="RentalDays"/>). A quote and the booking it becomes cannot
    /// disagree about the number of days if neither of them is allowed to name it.
    ///
    /// Convert the period's instants with IReportingCalendar before calling; the domain does not
    /// resolve time zones.
    /// </remarks>
    public static Result<BookingPricing, Error> Calculate(
        Money dailyRate,
        DateOnly pickupDate,
        DateOnly returnDate,
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

        var currency = dailyRate.CurrencyCode;
        if (!string.Equals(deliveryFee.CurrencyCode, currency, StringComparison.Ordinal) ||
            !string.Equals(securityDeposit.CurrencyCode, currency, StringComparison.Ordinal))
        {
            return BookingErrors.CurrencyMismatch;
        }

        var days = RentalDays.Between(pickupDate, returnDate);

        var rentalTotal = dailyRate.MultiplyBy(days);
        var totalPrice = rentalTotal.Add(deliveryFee);
        var depositAmount = depositPercent.Of(rentalTotal);
        // The balance is what the customer still owes the dealer in cash: the rental not covered by
        // the deposit, plus the delivery fee, which the driver collects on arrival.
        var balanceDue = rentalTotal.Subtract(depositAmount).Add(deliveryFee);

        return new BookingPricing(
            dailyRate,
            pickupDate,
            returnDate,
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
        yield return PickupDate;
        yield return ReturnDate;
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
