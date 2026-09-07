using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;

namespace Khadra.Domain.Bookings;

/// <summary>
/// Whether a gallery is open at the two moments a self-pickup rental needs somebody behind a counter.
/// </summary>
/// <remarks>
/// Settled by the owner on 2026-09-07: a SELF-PICKUP booking is refused when the gallery is shut, and
/// a DELIVERY booking is not judged against counter hours at all. The reasoning is physical rather
/// than commercial — a customer cannot collect keys from a closed office, but a gallery driving a car
/// out to somebody may perfectly well do that outside the hours it keeps its counter.
///
/// Both ends of a self-pickup rental are checked, not just the pickup. The customer brings the car
/// back to the same counter, and a return booked for 03:00 is the same impossibility as a collection
/// at 03:00 — it is simply discovered three days later. They are reported separately so a customer is
/// told WHICH date to move.
///
/// The times are LOCAL and this type does not know which zone produced them, matching
/// <see cref="RentalDays"/> and <c>RenterAgePolicy</c>: the caller converts through
/// <c>IReportingCalendar</c> and the domain stays free of ambient time.
///
/// A gallery that has not set real hours will refuse its own self-pickup bookings. That is loud and
/// immediately visible to them, and fixable from their own console, which is the failure mode worth
/// having — the alternative is a customer told at the counter.
/// </remarks>
public static class PickupHoursPolicy
{
    public static UnitResult<Error> Validate(
        OperatingHours hours,
        PickupMethod pickupMethod,
        DateOnly localPickupDate,
        TimeOnly localPickupTime,
        DateOnly localReturnDate,
        TimeOnly localReturnTime)
    {
        ArgumentNullException.ThrowIfNull(hours);
        ArgumentNullException.ThrowIfNull(pickupMethod);

        // The gallery comes to the customer. Its counter hours are not the constraint, and refusing
        // on them would turn away the deliveries that make out-of-hours rental work at all.
        if (pickupMethod == PickupMethod.Delivery)
            return UnitResult.Success<Error>();

        if (!hours.IsOpenAt(localPickupDate.DayOfWeek, localPickupTime))
            return UnitResult.Failure(BookingErrors.PickupOutsideOpeningHours(Describe(hours, localPickupDate)));

        if (!hours.IsOpenAt(localReturnDate.DayOfWeek, localReturnTime))
            return UnitResult.Failure(BookingErrors.ReturnOutsideOpeningHours(Describe(hours, localReturnDate)));

        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// What the gallery does on that day, in words a customer can act on.
    /// </summary>
    /// <remarks>
    /// "Outside opening hours" on its own tells somebody they are wrong without telling them what
    /// would be right, and they would have to go and find the gallery's page to learn it.
    /// </remarks>
    private static string Describe(OperatingHours hours, DateOnly day)
    {
        var schedule = hours.For(day.DayOfWeek);
        return schedule.IsClosed
            ? $"closed on {day.DayOfWeek}"
            : $"open {schedule.OpensAt:HH\\:mm}-{schedule.ClosesAt:HH\\:mm} on {day.DayOfWeek}";
    }
}
