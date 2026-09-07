using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Fleet;

namespace Khadra.Application.Bookings;

/// <summary>
/// Prices a rental, and freezes the rules it was priced under.
/// </summary>
/// <remarks>
/// One place, used twice: the quote a customer is shown before booking, and the booking that is
/// actually created. That is the whole point of extracting it. A quote and a create that each
/// assembled their own <see cref="BookingPricing"/> would drift the first time a rule moved, and the
/// customer would be shown one total and charged another.
///
/// It is also where the calendar lives. Rentals are billed in Amman calendar days, and the domain
/// deliberately has no time zone — <see cref="RentalDays"/> takes two local dates and
/// <see cref="BookingPricing"/> freezes them. Converting the period's instants is an application
/// job, and this is the one caller allowed to do it.
/// </remarks>
public sealed class BookingPricer(IBusinessRulesProvider businessRules, IReportingCalendar calendar)
{
    /// <summary>
    /// What this rental would cost, and the rules it would be judged by.
    /// </summary>
    /// <remarks>
    /// The delivery fee is the GALLERY's, never a platform figure — the owner moved it onto the
    /// dealership on 2026-09-06 and there is no platform-wide fee left to fall back on. Self-pickup
    /// is priced at zero in the vehicle's own currency, because <c>Booking.Create</c> refuses a
    /// self-pickup booking that carries a fee at all.
    /// </remarks>
    public async Task<Result<PricedRental, Error>> PriceAsync(
        Vehicle vehicle,
        Dealer dealer,
        DateRange period,
        PickupMethod pickupMethod,
        GeoPoint? deliveryLocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(dealer);
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(pickupMethod);

        if (pickupMethod == PickupMethod.Delivery)
        {
            if (!vehicle.IsDeliveryEligible)
                return BookingErrors.VehicleNotDeliveryEligible;
            if (deliveryLocation is null)
                return BookingErrors.DeliveryLocationRequired;
            if (!dealer.Delivery.IsEnabled || !dealer.CoversLocation(deliveryLocation))
                return BookingErrors.DeliveryOutOfRange;
        }
        else if (deliveryLocation is not null)
        {
            return BookingErrors.DeliveryLocationNotAllowed;
        }

        var deliveryFee = pickupMethod == PickupMethod.Delivery
            // Delivery is enabled, so the fee is set: DeliverySettings makes Fee null exactly when
            // delivery is off, and the guard above has already refused that case.
            ? dealer.Delivery.Fee!
            : Money.ZeroIn(vehicle.DailyRate.CurrencyCode);

        var rules = await businessRules.GetAsync(cancellationToken);

        var depositPercent = Percentage.Create(rules.DepositPercent);
        if (depositPercent.IsFailure)
            return depositPercent.Error;

        var pricing = BookingPricing.Calculate(
            vehicle.DailyRate,
            calendar.DayOf(period.Start),
            calendar.DayOf(period.End),
            deliveryFee,
            depositPercent.Value,
            vehicle.SecurityDeposit,
            vehicle.Mileage,
            vehicle.FuelPolicy);

        if (pricing.IsFailure)
            return pricing.Error;

        var terms = BuildTerms(rules);
        return terms.IsFailure
            ? terms.Error
            : new PricedRental(pricing.Value, terms.Value);
    }

    /// <summary>
    /// The live rules, shaped for freezing onto a booking.
    /// </summary>
    /// <remarks>
    /// `RulesVersion` is 1 for every booking, which is pre-launch checklist item 25. It stays a
    /// literal here rather than being quietly invented from something else: the number is meant to
    /// come from the settings aggregate, and that aggregate is not built.
    /// </remarks>
    private static Result<BookingTerms, Error> BuildTerms(BusinessRules rules)
    {
        var deposit = Percentage.Create(rules.DepositPercent);
        var commission = Percentage.Create(rules.CommissionPercent);
        var customerPenalty = Percentage.Create(rules.CustomerCancellationPenaltyPercent);
        var dealerMin = Percentage.Create(rules.DealerNonDeliveryPenaltyMinPercent);
        var dealerMax = Percentage.Create(rules.DealerNonDeliveryPenaltyMaxPercent);

        foreach (var percentage in new[] { deposit, commission, customerPenalty, dealerMin, dealerMax })
        {
            if (percentage.IsFailure)
                return percentage.Error;
        }

        return BookingTerms.Create(
            deposit.Value,
            commission.Value,
            TimeSpan.FromMinutes(rules.FreeCancellationWindowMinutes),
            TimeSpan.FromHours(rules.NoShowTimeoutHours),
            TimeSpan.FromMinutes(rules.PaymentWindowMinutes),
            TimeSpan.FromHours(rules.PostReturnSettlementHours),
            customerPenalty.Value,
            dealerMin.Value,
            dealerMax.Value,
            TimeSpan.FromMinutes(rules.TurnaroundMinutes),
            rulesVersion: 1);
    }
}

/// <summary>A priced rental and the frozen rules that go with it.</summary>
public sealed record PricedRental(BookingPricing Pricing, BookingTerms Terms);
