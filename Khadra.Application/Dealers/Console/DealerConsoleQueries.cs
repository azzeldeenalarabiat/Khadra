using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Dtos;
using Khadra.Application.Common.Ports;
using Khadra.Application.Fleet.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Fleet;
using MediatR;

namespace Khadra.Application.Dealers.Console;

// The dealer's dashboard, reports and activity (design: Dealer Console, spec 4.5). Composed from the
// per-context readers, called one after another -- never concurrently, they share the scoped
// DbContext -- and bucketed here with the Amman calendar and the domain's own money arithmetic.

public sealed record GetDealerDashboardQuery(Id UserId) : IQuery<Result<DealerDashboardDto, Error>>;

/// <summary>Period: daily, weekly or monthly, always the CURRENT one in the reporting calendar.</summary>
public sealed record GetDealerReportQuery(Id UserId, string Period) : IQuery<Result<DealerReportDto, Error>>;

/// <summary>Has this dealer's queue changed? Nothing else. See <see cref="DealerPulseDto"/>.</summary>
public sealed record GetDealerPulseQuery(Id UserId) : IQuery<Result<DealerPulseDto, Error>>;

/// <summary>`MineOnly` narrows the dealership's trail to the caller's own actions (`?actor=me`).</summary>
public sealed record ListDealerActivityQuery(Id UserId, int? Page, int? PageSize, bool MineOnly = false)
    : IQuery<Result<PagedResult<DealerActivityEntry>, Error>>;

public sealed class GetDealerReportQueryValidator : AbstractValidator<GetDealerReportQuery>
{
    public GetDealerReportQueryValidator() =>
        RuleFor(query => query.Period)
            .Must(period => period is "daily" or "weekly" or "monthly")
            .WithMessage("Period must be daily, weekly or monthly.");
}

public sealed record DealerDashboardDto(
    string BusinessName,
    bool CanTrade,
    DealerBookingCounts Bookings,
    // Vehicles not currently held by a live booking, out of the published ones.
    int AvailableVehicles,
    int PublishedVehicles,
    int TotalVehicles,
    IReadOnlyList<FleetStatusCountDto> FleetStatus,
    IReadOnlyList<UpcomingHandoverDto> UpcomingPickups,
    IReadOnlyList<UpcomingHandoverDto> UpcomingReturns,
    int UpcomingWindowHours,
    MoneyDto? RevenueThisMonth,
    decimal? OccupancyPercentLast30Days,
    IReadOnlyList<DealerActivityEntry> RecentActivity);

/// <summary>
/// One opaque string, and deliberately nothing else.
/// </summary>
/// <remarks>
/// It is an INVALIDATION SIGNAL, not data. The console compares it with the one it last saw and
/// re-reads the real endpoints when it differs; no screen renders it, and it carries no figure any
/// screen could render. That is the point. A count arriving here as well as from
/// `GET /bookings/tab-counts` would be two answers to one question, and the day they disagreed the
/// dealer would have no way to tell which was true — so this is a token, not a number.
///
/// Opaque also means the server can change what it watches without the console knowing. Today it is
/// the per-status counts plus how many have not lapsed; if a dealer's queue grows a new way to change
/// tomorrow, only <see cref="IDealerBookingReader.QueueSignatureAsync"/> moves.
///
/// It is NOT a security boundary and NOT a cache validator: it says "something moved", never what.
/// </remarks>
public sealed record DealerPulseDto(string Bookings);

public sealed record FleetStatusCountDto(string Status, int Count);

/// <param name="VehicleLabel">Make, model and year; null when the car is no longer in this dealer's fleet.</param>
/// <param name="CustomerName">Null when the customer's account no longer resolves.</param>
public sealed record UpcomingHandoverDto(
    Guid BookingId,
    string Reference,
    string Status,
    DateTimeOffset When,
    string PickupMethod,
    string? VehicleLabel,
    string? CustomerName,
    bool IsOverdue);

public sealed record DealerReportDto(
    string Period,
    DateOnly From,
    DateOnly To,
    int Bookings,
    MoneyDto Revenue,
    // At the rate FROZEN on each booking -- never today's setting.
    MoneyDto Commission,
    // Revenue minus commission. The console formats it; it never subtracts money itself.
    MoneyDto NetAfterCommission,
    MoneyDto InProgress,
    int InProgressCount,
    decimal OccupancyPercent,
    IReadOnlyList<VehicleOccupancyDto> OccupancyByVehicle);

public sealed record VehicleOccupancyDto(Guid VehicleId, string Label, decimal RentedDays, decimal OccupancyPercent);

public sealed class DealerConsoleHandlers(
    DealerMembershipResolver membership,
    IDealerBookingReader bookings,
    IDealerFleetReader fleet,
    IReportingCalendar calendar,
    IDealerConsoleSettings settings,
    IClock clock) :
    IRequestHandler<GetDealerDashboardQuery, Result<DealerDashboardDto, Error>>,
    IRequestHandler<GetDealerPulseQuery, Result<DealerPulseDto, Error>>,
    IRequestHandler<GetDealerReportQuery, Result<DealerReportDto, Error>>,
    IRequestHandler<ListDealerActivityQuery, Result<PagedResult<DealerActivityEntry>, Error>>
{
    public async Task<Result<DealerPulseDto, Error>> Handle(GetDealerPulseQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var member = await membership.ResolveAsync(request.UserId, cancellationToken);
        if (member.IsFailure)
            return member.Error;

        var signature = await bookings.QueueSignatureAsync(member.Value.Dealer.Id, clock.UtcNow, cancellationToken);
        return new DealerPulseDto(DealerPulse.TokenFor(signature));
    }

    public async Task<Result<DealerDashboardDto, Error>> Handle(GetDealerDashboardQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var member = await membership.ResolveAsync(request.UserId, cancellationToken);
        if (member.IsFailure)
            return member.Error;
        var dealer = member.Value.Dealer;

        var now = clock.UtcNow;
        var counts = await bookings.CountsAsync(dealer.Id, now, cancellationToken);
        var pickups = await bookings.UpcomingPickupsAsync(dealer.Id, now, now.Add(settings.UpcomingWindow), cancellationToken);
        var returns = await bookings.UpcomingReturnsAsync(dealer.Id, now, now.Add(settings.UpcomingWindow), cancellationToken);
        var held = await bookings.HeldVehicleIdsAsync(dealer.Id, now, cancellationToken);
        var cars = await fleet.SummaryAsync(dealer.Id, cancellationToken);
        var activity = await bookings.ActivityAsync(dealer.Id, PageRequest.From(1, 6), cancellationToken: cancellationToken);

        // Money and occupancy only for someone allowed to see reports; the dashboard tile stays null
        // for an employee without the grant rather than leaking a figure the Reports screen refuses.
        MoneyDto? revenue = null;
        decimal? occupancy = null;
        if (member.Value.CanViewReports)
        {
            var (monthFrom, monthTo) = Window("monthly", now);
            var facts = await bookings.RevenueAsync(dealer.Id, monthFrom, monthTo, cancellationToken);
            revenue = MoneyDto.From(Sum(facts.Where(fact => fact.Status != BookingStatus.PickedUp.Name)));
            var thirtyDays = await bookings.OccupancyAsync(dealer.Id, now.AddDays(-30), now, cancellationToken);
            occupancy = OccupancyPercent(thirtyDays, cars.Where(car => car.Status != VehicleStatus.Draft.Name).Count(), now.AddDays(-30), now);
        }

        var labels = cars.ToDictionary(car => car.VehicleId, car => $"{car.Make} {car.Model} {car.Year}");
        var published = cars.Where(car => car.Status == VehicleStatus.Active.Name).ToList();

        return new DealerDashboardDto(
            dealer.BusinessName.Value,
            dealer.CanTrade,
            counts,
            published.Count(car => !held.Contains(car.VehicleId)),
            published.Count,
            cars.Count,
            [.. cars.GroupBy(car => car.Status).Select(group => new FleetStatusCountDto(group.Key, group.Count()))],
            [.. pickups.Select(handover => Label(handover, labels))],
            [.. returns.Select(handover => Label(handover, labels))],
            (int)settings.UpcomingWindow.TotalHours,
            revenue,
            occupancy,
            activity.Items);
    }

    public async Task<Result<DealerReportDto, Error>> Handle(GetDealerReportQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var member = await membership.ResolveAsync(request.UserId, cancellationToken);
        if (member.IsFailure)
            return member.Error;
        // Spec 4.2 / 4.5: financial reports are the owner's, and an employee's only with the grant.
        if (!member.Value.CanViewReports)
            return DealerErrors.ReportsNotGranted;
        var dealer = member.Value.Dealer;

        var now = clock.UtcNow;
        var (from, to) = Window(request.Period, now);
        var facts = await bookings.RevenueAsync(dealer.Id, from, to, cancellationToken);
        var stretches = await bookings.OccupancyAsync(dealer.Id, from, to, cancellationToken);
        var cars = await fleet.SummaryAsync(dealer.Id, cancellationToken);

        var earned = facts.Where(fact => fact.Status != BookingStatus.PickedUp.Name).ToList();
        var inProgress = facts.Where(fact => fact.Status == BookingStatus.PickedUp.Name).ToList();
        var listed = cars.Where(car => car.Status != VehicleStatus.Draft.Name).ToList();
        // Occupancy is judged over the ELAPSED part of the period only. A month that is three days
        // old has three days of car-time to fill, not thirty; dividing by the whole month would report
        // a tenth of the truth until the last day. Revenue keeps the full window: it is a sum, not a rate.
        var elapsedTo = to < now ? to : now;
        var windowDays = (decimal)(elapsedTo - from).TotalDays;

        var byVehicle = listed
            .Select(car =>
            {
                var days = RentedDays(stretches.Where(stretch => stretch.VehicleId == car.VehicleId), from, elapsedTo);
                return new VehicleOccupancyDto(
                    car.VehicleId,
                    $"{car.Make} {car.Model} {car.Year}",
                    decimal.Round(days, 1),
                    windowDays == 0 ? 0 : decimal.Round(Math.Min(100m, days / windowDays * 100m), 0));
            })
            .OrderByDescending(row => row.OccupancyPercent)
            .ToList();

        return new DealerReportDto(
            request.Period,
            calendar.DayOf(from),
            calendar.DayOf(to.AddTicks(-1)),
            earned.Count,
            MoneyDto.From(Sum(earned)),
            MoneyDto.From(Commission(earned)),
            MoneyDto.From(Sum(earned).Subtract(Commission(earned))),
            MoneyDto.From(Sum(inProgress)),
            inProgress.Count,
            OccupancyPercent(stretches, listed.Count, from, elapsedTo),
            byVehicle);
    }

    public async Task<Result<PagedResult<DealerActivityEntry>, Error>> Handle(ListDealerActivityQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var member = await membership.ResolveAsync(request.UserId, cancellationToken);
        if (member.IsFailure)
            return member.Error;

        // The handler substitutes the caller's own id. A user-id parameter would let one member of
        // staff page a colleague's record, and nothing in the spec asks for that.
        return await bookings.ActivityAsync(
            member.Value.Dealer.Id,
            PageRequest.From(request.Page, request.PageSize),
            request.MineOnly ? request.UserId : null,
            cancellationToken);
    }

    /// <summary>The current day, week or month as [start, end) instants in the reporting calendar.</summary>
    private (DateTimeOffset From, DateTimeOffset To) Window(string period, DateTimeOffset now)
    {
        var today = calendar.Today(now);
        switch (period)
        {
            case "daily":
                return (calendar.StartOfDay(today), calendar.StartOfDay(today.AddDays(1)));
            case "weekly":
            {
                var back = ((int)today.DayOfWeek - (int)settings.ReportingWeekStart + 7) % 7;
                var start = today.AddDays(-back);
                return (calendar.StartOfDay(start), calendar.StartOfDay(start.AddDays(7)));
            }
            default:
            {
                var start = new DateOnly(today.Year, today.Month, 1);
                return (calendar.StartOfDay(start), calendar.StartOfDay(start.AddMonths(1)));
            }
        }
    }

    // Summed as Money in the dealer's currency; every booking of one dealer is priced in the same one.
    private static Money Sum(IEnumerable<RevenueFact> facts)
    {
        var currency = facts.FirstOrDefault()?.Currency ?? Money.JordanianDinar;
        var total = Money.ZeroIn(currency);
        foreach (var fact in facts)
            total = total.Add(Money.Create(fact.RentalTotal, fact.Currency));
        return total;
    }

    // Per booking, at that booking's frozen rate, rounded the way a charge would be, then summed.
    private static Money Commission(IEnumerable<RevenueFact> facts)
    {
        var currency = facts.FirstOrDefault()?.Currency ?? Money.JordanianDinar;
        var total = Money.ZeroIn(currency);
        foreach (var fact in facts)
            total = total.Add(Percentage.FromValidated(fact.CommissionPercent).Of(Money.Create(fact.RentalTotal, fact.Currency)));
        return total;
    }

    private static decimal RentedDays(IEnumerable<OccupancyFact> stretches, DateTimeOffset from, DateTimeOffset to)
    {
        decimal days = 0;
        foreach (var stretch in stretches)
        {
            var start = stretch.Start > from ? stretch.Start : from;
            var end = stretch.End < to ? stretch.End : to;
            if (end > start)
                days += (decimal)(end - start).TotalDays;
        }

        return days;
    }

    private static decimal OccupancyPercent(IEnumerable<OccupancyFact> stretches, int vehicles, DateTimeOffset from, DateTimeOffset to)
    {
        var windowDays = (decimal)(to - from).TotalDays;
        if (vehicles == 0 || windowDays <= 0)
            return 0;
        return decimal.Round(Math.Min(100m, RentedDays(stretches, from, to) / (vehicles * windowDays) * 100m), 0);
    }

    private static UpcomingHandoverDto Label(UpcomingHandover handover, Dictionary<Guid, string> labels) =>
        new(
            handover.BookingId,
            handover.Reference,
            handover.Status,
            handover.When,
            handover.PickupMethod,
            // Null when the vehicle is no longer in this dealer's fleet summary; the console words that case.
            labels.TryGetValue(handover.VehicleId, out var label) ? label : null,
            handover.CustomerName,
            handover.IsOverdue);
}
