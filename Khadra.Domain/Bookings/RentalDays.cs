namespace Khadra.Domain.Bookings;

/// <summary>
/// How many days a rental is billed for.
/// </summary>
/// <remarks>
/// Settled by the owner on 2026-09-07: rentals are billed in CALENDAR days, the way a hotel counts
/// nights. Monday 09:00 to Thursday 11:00 is three days, not four — the difference between the two
/// dates, with a same-day rental counting as one.
///
/// This replaced a duration-based count that rounded any part of a day up. Two consequences are
/// worth stating where the rule lives, because neither is obvious from the arithmetic:
///
/// - The new rule is never dearer than the old one, and is cheaper whenever the car comes back later
///   in the day than it went out. That is a transfer from the gallery to the customer on most
///   rentals, and it was the owner's decision to make.
/// - The return TIME no longer affects the price at all: 00:01 and 23:59 on the same date cost the
///   same. Late returns are therefore unpriced. The usual answer is a grace period plus an hourly
///   overage; the spec has neither, and it is logged in docs/pre-launch-checklist.md rather than
///   guessed at here.
///
/// The dates are LOCAL dates and this type does not know which zone produced them. That is
/// deliberate and matches how the rest of the domain handles time zones — RenterAgePolicy takes
/// today's date, OperatingHours takes local times, VehicleDetails takes the current year. The caller
/// converts through IReportingCalendar; the domain stays free of ambient time and configuration.
/// </remarks>
public static class RentalDays
{
    /// <summary>
    /// The billable days between two local calendar dates. Never fewer than one.
    /// </summary>
    /// <remarks>
    /// A return date before the pickup date is a caller bug, not a customer-reachable outcome — the
    /// period it came from is validated half-open — so it clamps to one day rather than returning a
    /// negative count that would silently price a rental at nothing.
    /// </remarks>
    public static int Between(DateOnly localPickupDate, DateOnly localReturnDate) =>
        Math.Max(1, localReturnDate.DayNumber - localPickupDate.DayNumber);
}
