using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Fleet;

namespace Khadra.Tests.Support;

// Builders for the marketplace contexts. Each returns a valid object so a test only states the one
// thing it is actually about.
internal static class Build
{
    public static readonly DateTimeOffset Now = new(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);

    // The admin review SLA a test dealer is registered under (spec 3.1 uses 48 hours).
    public static readonly TimeSpan ReviewSla = TimeSpan.FromHours(48);

    // The owner's confirmed turnaround gap between rentals (BusinessRules:TurnaroundMinutes = 120).
    // Tests about the gap itself pass their own; everything else inherits the real number, so the
    // suite exercises the rule the platform actually runs under.
    public static readonly TimeSpan TurnaroundBuffer = TimeSpan.FromHours(2);

    // Amman is UTC+3 with no daylight saving. The real handlers go through IReportingCalendar; a
    // factory only needs the same answer, and stating it here keeps every test using one calendar.
    public static DateOnly AmmanDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(3)).DateTime);

    public static GeoPoint Amman => GeoPoint.Create(31.9539, 35.9106).Value;

    public static GeoPoint Zarqa => GeoPoint.Create(32.0728, 36.0880).Value;

    public static GeoPoint Aqaba => GeoPoint.Create(29.5321, 35.0063).Value;

    public static Percentage Percent(decimal value) => Percentage.FromValidated(value);

    public static OperatingHours NineToFive =>
        OperatingHours.Uniform(new TimeOnly(9, 0), new TimeOnly(17, 0)).Value;

    public static Dealer Dealer(DateTimeOffset? now = null, Id? ownerUserId = null)
    {
        var moment = now ?? Now;
        return Khadra.Domain.Dealers.Dealer.Register(
            ownerUserId ?? Id.New(),
            BusinessName.Create("Petra Rentals").Value,
            CommercialRegistrationNumber.Create("123456").Value,
            Amman,
            NineToFive,
            moment,
            ReviewSla);
    }

    // A dealer that has cleared the licence check and can trade.
    public static Dealer ApprovedDealer(DateTimeOffset? now = null, Id? ownerUserId = null)
    {
        var moment = now ?? Now;
        var dealer = Dealer(moment, ownerUserId);
        AttachAllDocuments(dealer, moment);
        dealer.Approve(Id.New(), moment);
        dealer.ClearDomainEvents();
        return dealer;
    }

    public static void AttachAllDocuments(Dealer dealer, DateTimeOffset? now = null)
    {
        var moment = now ?? Now;
        foreach (var type in DealerDocumentType.Required)
            dealer.AttachDocument(type, $"docs/{type.Name}.jpg", moment);
    }

    public static VehicleDetails VehicleDetails(int year = 2024) =>
        Khadra.Domain.Fleet.VehicleDetails.Create(
            "Toyota", "Corolla", year, 5, TransmissionType.Automatic, FuelType.Petrol, currentYear: 2026).Value;

    public static Vehicle Vehicle(Id? dealerId = null, decimal dailyRate = 30m, DateTimeOffset? now = null)
    {
        var moment = now ?? Now;
        var vehicle = Khadra.Domain.Fleet.Vehicle.Add(
            dealerId ?? Id.New(),
            Id.New(),
            VehicleDetails(),
            PlateNumber.Create("12-34567").Value,
            Money.Jod(dailyRate),
            Money.Jod(200m),
            MileagePolicy.Unlimited(),
            FuelPolicy.FullToFull,
            isDeliveryEligible: true,
            moment).Value;
        vehicle.ClearDomainEvents();
        return vehicle;
    }

    // The owner's confirmed numbers from spec section 2, with the two still-undecided values set to
    // the reading the code assumes: a customer who cancels late forfeits the whole deposit.
    public static BookingTerms Terms(
        decimal depositPercent = 20m,
        decimal commissionPercent = 20m,
        TimeSpan? freeCancellationWindow = null,
        TimeSpan? noShowTimeout = null,
        TimeSpan? paymentWindow = null,
        TimeSpan? settlementWindow = null,
        decimal customerPenaltyPercent = 100m,
        decimal dealerPenaltyMin = 25m,
        decimal dealerPenaltyMax = 50m,
        TimeSpan? turnaroundBuffer = null) =>
        BookingTerms.Create(
            Percent(depositPercent),
            Percent(commissionPercent),
            freeCancellationWindow ?? TimeSpan.FromHours(1),
            noShowTimeout ?? TimeSpan.FromHours(8),
            paymentWindow ?? TimeSpan.FromMinutes(20),
            settlementWindow ?? TimeSpan.FromHours(48),
            Percent(customerPenaltyPercent),
            Percent(dealerPenaltyMin),
            Percent(dealerPenaltyMax),
            turnaroundBuffer ?? TurnaroundBuffer,
            rulesVersion: 1).Value;

    public static DateRange Period(DateTimeOffset? start = null, int days = 3)
    {
        var from = start ?? Now.AddDays(7);
        return DateRange.Create(from, from.AddDays(days)).Value;
    }

    // Priced between two Amman calendar dates, exactly as a handler does after converting the
    // period through IReportingCalendar. `days` is the gap between the dates, not a number the
    // caller gets to assert independently -- BookingPricing counts it, and nothing else may.
    public static BookingPricing Pricing(
        decimal dailyRate = 30m,
        int days = 3,
        decimal deliveryFee = 0m,
        decimal depositPercent = 20m,
        DateOnly? pickupDate = null)
    {
        var from = pickupDate ?? AmmanDate(Now.AddDays(7));
        return BookingPricing.Calculate(
            Money.Jod(dailyRate),
            from,
            from.AddDays(days),
            Money.Jod(deliveryFee),
            Percent(depositPercent),
            Money.Jod(200m),
            MileagePolicy.Unlimited(),
            FuelPolicy.FullToFull).Value;
    }

    public static Booking Booking(
        DateTimeOffset? now = null,
        DateRange? period = null,
        PickupMethod? pickupMethod = null,
        GeoPoint? deliveryLocation = null,
        BookingTerms? terms = null,
        BookingPricing? pricing = null,
        Id? customerId = null,
        Id? dealerId = null,
        Id? vehicleId = null)
    {
        var moment = now ?? Now;
        var bookingPeriod = period ?? Period(moment.AddDays(7));
        var method = pickupMethod ?? PickupMethod.SelfPickup;
        var booking = Khadra.Domain.Bookings.Booking.Create(
            customerId ?? Id.New(),
            dealerId ?? Id.New(),
            vehicleId ?? Id.New(),
            bookingPeriod,
            method,
            method == PickupMethod.Delivery ? deliveryLocation ?? Amman : null,
            // The dates come from the period, the way a handler derives them, so the pricing the
            // aggregate receives always describes the period being booked.
            pricing ?? Pricing(
                days: RentalDays.Between(AmmanDate(bookingPeriod.Start), AmmanDate(bookingPeriod.End)),
                deliveryFee: method == PickupMethod.Delivery ? 10m : 0m,
                pickupDate: AmmanDate(bookingPeriod.Start)),
            terms ?? Terms(),
            PaymentOption.DepositOnly,
            moment).Value;
        booking.ClearDomainEvents();
        return booking;
    }

    // A booking the dealer has approved: the state most rules hang off.
    public static Booking ApprovedBooking(DateTimeOffset? now = null, PickupMethod? pickupMethod = null, BookingTerms? terms = null)
    {
        var moment = now ?? Now;
        var booking = Booking(moment, pickupMethod: pickupMethod, terms: terms);
        booking.ConfirmDepositPaid(Id.New(), moment);
        booking.Approve(Id.New(), moment);
        booking.ClearDomainEvents();
        return booking;
    }
}
