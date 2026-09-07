using CSharpFunctionalExtensions;
using Khadra.Domain.Common;

namespace Khadra.Domain.Bookings;

/// <summary>
/// Whether a rental may happen at all: how soon it may start, how far ahead, and how long it may run.
/// </summary>
/// <remarks>
/// Every VALUE is a configured business rule and arrives from <c>IBusinessRulesProvider</c>, never
/// from a constant here (see CLAUDE.md). <c>now</c> and the billed day count are passed in rather
/// than read from a clock or a calendar, so the rule stays pure and testable — the same shape as
/// <c>RenterAgePolicy</c>, which takes today's date, and <c>OperatingHours</c>, which takes a local
/// time.
///
/// This exists as ONE place rather than a check per surface because search, quote and create must
/// agree exactly. A search that lists cars for a span the quote then refuses is a customer choosing
/// dates, being shown results, and being told no at the last step; a quote that prices a rental the
/// create endpoint rejects is a total beside a button that cannot work. Both have happened here.
///
/// The two bounds on the START are elapsed time and carry no time zone: "at least two hours from now"
/// and "no more than 180 days from now" mean the same thing anywhere. The bound on the LENGTH is
/// different — it is counted in the local calendar days the rental is BILLED in, which is why the
/// caller computes it through <c>IReportingCalendar</c> and hands it over. Judging it on the billed
/// count also means a customer is refused on the same number they were quoted.
/// </remarks>
public static class BookingWindowPolicy
{
    public static UnitResult<Error> Validate(
        DateRange period,
        DateTimeOffset now,
        TimeSpan minimumLeadTime,
        int maxAdvanceDays,
        int billedDays,
        int maxRentalDays)
    {
        ArgumentNullException.ThrowIfNull(period);

        if (period.Start <= now)
            return UnitResult.Failure(BookingErrors.PeriodInThePast);

        // The floor. Every window a booking carries -- the dealer's answer, the customer's payment,
        // free cancellation -- is capped at the rental start, so without this they all collapse
        // together on a booking made minutes before pickup, while the platform is still publishing
        // a 24-hour payment window on /app-config.
        if (period.Start < now.Add(minimumLeadTime))
            return UnitResult.Failure(BookingErrors.TooSoon(minimumLeadTime));

        // The ceiling. A booking FREEZES the price it was made under, so a horizon is a promise to
        // honour today's rate that far out, not a UI convenience.
        if (period.Start > now.AddDays(maxAdvanceDays))
            return UnitResult.Failure(BookingErrors.BeyondBookingHorizon(maxAdvanceDays));

        // And the far end. Without it a five-year request is accepted, holds the car for the whole
        // answer window, and lands in a gallery's queue. Long hires are a real business and this is
        // not a judgement about them: it is a bound the owner moves in configuration.
        if (billedDays > maxRentalDays)
            return UnitResult.Failure(BookingErrors.RentalTooLong(billedDays, maxRentalDays));

        return UnitResult.Success<Error>();
    }
}
