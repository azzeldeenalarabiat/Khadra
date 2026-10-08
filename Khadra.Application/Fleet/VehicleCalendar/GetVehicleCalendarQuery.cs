using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Fleet;
using Khadra.Domain.Fleet.Repositories;
using MediatR;

namespace Khadra.Application.Fleet.VehicleCalendar;

/// <summary>
/// One car's month, day by day, as the fleet screen shows it (pre-launch item 54).
/// </summary>
/// <remarks>
/// <para>
/// The screen used to cut the car's bookings into days itself, in the BROWSER's calendar, from every booking whose row
/// read Requested, Approved, Confirmed or PickedUp. Three things were wrong with that, and all three are the server's
/// facts: which day an instant falls on is the platform's (Amman) calendar, not the reader's; a request or an approval
/// whose window has closed holds nothing, whatever its row says, which the catalogue already knew; and the turnaround a
/// booking froze claims the car before the customer collects it, so the car was shown free while it was being cleaned.
/// </para>
/// <para>
/// Now the days come from here: <c>BookingHolds.Live</c> through <see cref="IDealerBookingReader.VehicleHoldsAsync"/>,
/// the same predicate the customer's search uses, cut into the platform's calendar days.
/// </para>
/// </remarks>
public sealed record GetVehicleCalendarQuery(Id ActorUserId, Id VehicleId, int Year, int Month)
    : IQuery<Result<VehicleCalendarDto, Error>>;

/// <param name="Days">Every day of the month, first to last, in the platform's calendar.</param>
public sealed record VehicleCalendarDto(int Year, int Month, IReadOnlyList<VehicleCalendarDay> Days);

/// <summary>One calendar day of one car.</summary>
/// <param name="Status">
/// The status of the booking holding the car that day, or null when no booking does. A day only the turnaround before
/// a rental reaches has the status of that rental and <paramref name="Turnaround"/> set.
/// </param>
/// <param name="Turnaround">
/// The car is being prepared for a rental that starts later: inside the buffer the booking froze, before its period.
/// </param>
public sealed record VehicleCalendarDay(
    DateOnly Date,
    string? Status,
    Guid? BookingId,
    string? Reference,
    bool Turnaround);

public sealed class GetVehicleCalendarQueryValidator : AbstractValidator<GetVehicleCalendarQuery>
{
    public GetVehicleCalendarQueryValidator()
    {
        RuleFor(query => query.Year).InclusiveBetween(2000, 2100);
        RuleFor(query => query.Month).InclusiveBetween(1, 12);
    }
}

public sealed class GetVehicleCalendarHandler(
    DealerMembershipResolver membership,
    IVehicleRepository vehicles,
    IDealerBookingReader bookings,
    IReportingCalendar calendar,
    IClock clock)
    : IRequestHandler<GetVehicleCalendarQuery, Result<VehicleCalendarDto, Error>>
{
    public async Task<Result<VehicleCalendarDto, Error>> Handle(
        GetVehicleCalendarQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Staff may read their dealership's cars, as on the car's own page.
        var member = await membership.ResolveAsync(request.ActorUserId, cancellationToken);
        if (member.IsFailure)
            return member.Error;

        var vehicle = await vehicles.GetByIdAsync(request.VehicleId, cancellationToken);
        if (vehicle is null || vehicle.DealerId != member.Value.Dealer.Id)
            return FleetErrors.NotYours;

        var first = new DateOnly(request.Year, request.Month, 1);
        var next = first.AddMonths(1);
        var now = clock.UtcNow;
        var holds = await bookings.VehicleHoldsAsync(
            member.Value.Dealer.Id, vehicle.Id, calendar.StartOfDay(first), calendar.StartOfDay(next), now, cancellationToken);

        var days = new List<VehicleCalendarDay>(next.DayNumber - first.DayNumber);
        for (var day = first; day < next; day = day.AddDays(1))
            days.Add(Day(day, calendar.StartOfDay(day), calendar.StartOfDay(day.AddDays(1)), holds, now));

        return new VehicleCalendarDto(request.Year, request.Month, days);
    }

    private static VehicleCalendarDay Day(
        DateOnly date,
        DateTimeOffset start,
        DateTimeOffset end,
        IReadOnlyList<VehicleHold> holds,
        DateTimeOffset now)
    {
        // The rental itself first: a day a rental covers is that rental's, even when another's turnaround starts on it.
        var rental = holds.FirstOrDefault(hold => hold.PeriodStart < end && Until(hold, now) > start);
        if (rental is not null)
            return new VehicleCalendarDay(date, rental.Status, rental.BookingId, rental.Reference, Turnaround: false);

        var preparing = holds.FirstOrDefault(hold => hold.HoldStart < end && hold.PeriodStart > start);
        return preparing is not null
            ? new VehicleCalendarDay(date, preparing.Status, preparing.BookingId, preparing.Reference, Turnaround: true)
            : new VehicleCalendarDay(date, null, null, null, Turnaround: false);
    }

    // A collected car is out until it comes back: an overdue return still holds it today.
    private static DateTimeOffset Until(VehicleHold hold, DateTimeOffset now) =>
        hold.Status == BookingStatus.PickedUp.Name && hold.PeriodEnd < now ? now : hold.PeriodEnd;
}
