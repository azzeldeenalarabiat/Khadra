using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers;
using Khadra.Application.Dealers.Console;
using Khadra.Application.Fleet.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Dealers;

// The dealer's numbers (spec 4.5). What is pinned: revenue counts only cars that came back, commission
// is taken at the rate FROZEN on each booking, occupancy is rented days over listed cars, the week
// starts on Sunday, and nobody without the grant sees money.
public sealed class DealerConsoleTests
{
    private static readonly Id OwnerId = Id.New();
    private static readonly Id EmployeeId = Id.New();
    private static readonly Guid CarA = Guid.NewGuid();
    private static readonly Guid CarB = Guid.NewGuid();

    private sealed class Context
    {
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public IDealerBookingReader Bookings { get; } = Substitute.For<IDealerBookingReader>();
        public IDealerFleetReader Fleet { get; } = Substitute.For<IDealerFleetReader>();
        public IDealerConsoleSettings Settings { get; } = Substitute.For<IDealerConsoleSettings>();
        public TestClock Clock { get; } = new(Build.Now);
        public Dealer Dealer { get; }

        public Context()
        {
            Dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
            Dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(Dealer);
            Settings.UpcomingWindow.Returns(TimeSpan.FromHours(48));
            Settings.ReportingWeekStart.Returns(DayOfWeek.Sunday);

            Bookings.CountsAsync(Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                // 3 waiting on the dealer, 2 waiting on a deposit, 1 confirmed, 1 out, none overdue.
                .Returns(new DealerBookingCounts(3, Build.Now.AddHours(-14), 2, 1, 1, 0));
            Bookings.UpcomingPickupsAsync(Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns([new UpcomingHandover(Guid.NewGuid(), "KH-1", "Confirmed", Build.Now.AddHours(4), "Delivery", CarA, "Layla Odeh", false)]);
            Bookings.UpcomingReturnsAsync(Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns([]);
            Bookings.HeldVehicleIdsAsync(Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns([CarA]);
            Bookings.ActivityAsync(Arg.Any<Id>(), Arg.Any<PageRequest>(), Arg.Any<Id?>(), Arg.Any<CancellationToken>())
                .Returns(PagedResult.Empty<DealerActivityEntry>(1, 6));
            // Two returned bookings at different frozen rates, and one still out.
            Bookings.RevenueAsync(Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(
                [
                    new RevenueFact(Guid.NewGuid(), "Returned", Build.Now.AddDays(-1), 100m, "JOD", 20m),
                    new RevenueFact(Guid.NewGuid(), "Completed", Build.Now.AddDays(-2), 333.333m, "JOD", 15m),
                    new RevenueFact(Guid.NewGuid(), "PickedUp", Build.Now, 500m, "JOD", 20m),
                ]);
            Bookings.OccupancyAsync(Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var from = call.ArgAt<DateTimeOffset>(1);
                    // Car A rented for the first 3 days of the window; car B for 1 day, half of it before the window.
                    return
                    [
                        new OccupancyFact(CarA, from, from.AddDays(3), "Completed"),
                        new OccupancyFact(CarB, from.AddDays(-0.5), from.AddDays(0.5), "Returned"),
                    ];
                });
            Fleet.SummaryAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>())
                .Returns(
                [
                    new FleetVehicleSummary(CarA, "Toyota", "Corolla", 2023, "1111", "Active", true, 30m, "JOD"),
                    new FleetVehicleSummary(CarB, "Kia", "Rio", 2022, "2222", "Active", false, 25m, "JOD"),
                    new FleetVehicleSummary(Guid.NewGuid(), "Hyundai", "Staria", 2021, "3333", "Draft", false, 90m, "JOD"),
                ]);
        }

        public DealerConsoleHandlers Handlers() => new(
            new DealerMembershipResolver(Dealers), Bookings, Fleet, TestBusinessRules.Calendar(), Settings, Clock);
    }

    [Fact]
    public async Task The_dashboard_derives_availability_from_bookings_not_from_a_stored_status()
    {
        var context = new Context();

        var result = await context.Handlers().Handle(new GetDealerDashboardQuery(OwnerId), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var dashboard = result.Value;
        Assert.Equal(2, dashboard.PublishedVehicles);
        // Car A is held right now, so one of the two published cars is available.
        Assert.Equal(1, dashboard.AvailableVehicles);
        Assert.Equal(3, dashboard.TotalVehicles);
        Assert.Equal(3, dashboard.Bookings.Requested);
        Assert.Equal("Toyota Corolla 2023", dashboard.UpcomingPickups.Single().VehicleLabel);
        Assert.Equal(48, dashboard.UpcomingWindowHours);
        // The owner sees money.
        Assert.NotNull(dashboard.RevenueThisMonth);
    }

    [Fact]
    public async Task Revenue_counts_only_cars_that_came_back_and_commission_uses_each_bookings_frozen_rate()
    {
        var context = new Context();

        var result = await context.Handlers().Handle(new GetDealerReportQuery(OwnerId, "monthly"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var report = result.Value;
        Assert.Equal(2, report.Bookings);
        Assert.Equal(433.333m, report.Revenue.Amount);
        // 20% of 100 = 20.000; 15% of 333.333 = 50.000 (rounded per booking as Money rounds), not 17.5% of the total.
        Assert.Equal(70m, report.Commission.Amount);
        Assert.Equal("JOD", report.Commission.Currency);
        Assert.Equal(500m, report.InProgress.Amount);
        Assert.Equal(1, report.InProgressCount);
    }

    [Fact]
    public async Task Occupancy_is_rented_days_over_listed_cars_clipped_to_the_window()
    {
        var context = new Context();
        // A second before the day ends in Amman (UTC+3), so the whole window has elapsed.
        context.Clock.UtcNow = new DateTimeOffset(2026, 9, 3, 20, 59, 59, TimeSpan.Zero);

        var daily = await context.Handlers().Handle(new GetDealerReportQuery(OwnerId, "daily"), CancellationToken.None);

        // One day window, two listed cars (the draft does not count): car A 1 day (clipped from 3),
        // car B 0.5 day (half fell before the window) = 1.5 of 2 car-days = 75%.
        Assert.Equal(75m, daily.Value.OccupancyPercent);
        var byCar = daily.Value.OccupancyByVehicle;
        Assert.Equal(2, byCar.Count);
        Assert.Equal(100m, byCar.Single(row => row.VehicleId == CarA).OccupancyPercent);
        Assert.Equal(50m, byCar.Single(row => row.VehicleId == CarB).OccupancyPercent);
    }

    [Fact]
    public async Task Occupancy_counts_only_the_elapsed_part_of_a_period_still_running()
    {
        // 10:00 UTC is 13:00 in Amman: 13 of the day's 24 hours have passed. Car A has been out the
        // whole time (100%); car B for half a day out of the 13 hours elapsed (92%); together
        // (13/24 + 0.5) over 2 x 13/24 car-days = 96%. Dividing by the full day would say 75% / 50%.
        var context = new Context();

        var daily = await context.Handlers().Handle(new GetDealerReportQuery(OwnerId, "daily"), CancellationToken.None);

        var elapsed = 13m / 24m;
        Assert.Equal(decimal.Round((elapsed + 0.5m) / (2 * elapsed) * 100m, 0), daily.Value.OccupancyPercent);
        var byCar = daily.Value.OccupancyByVehicle;
        Assert.Equal(100m, byCar.Single(row => row.VehicleId == CarA).OccupancyPercent);
        Assert.Equal(decimal.Round(0.5m / elapsed * 100m, 0), byCar.Single(row => row.VehicleId == CarB).OccupancyPercent);
    }

    [Fact]
    public async Task Net_after_commission_is_computed_on_the_server()
    {
        var context = new Context();

        var report = await context.Handlers().Handle(new GetDealerReportQuery(OwnerId, "monthly"), CancellationToken.None);

        Assert.Equal(report.Value.Revenue.Amount - report.Value.Commission.Amount, report.Value.NetAfterCommission.Amount);
        Assert.Equal(report.Value.Revenue.Currency, report.Value.NetAfterCommission.Currency);
    }

    // ── Commission at the edges ──────────────────────────────────────────────────────────────────
    //
    // The report screen once printed "JOD 0−" for a commission of nothing: a sign typed in front of
    // the amount, in a month with no revenue. These pin the numbers the screen formats at the edges
    // where that happened -- an empty period, a 0% rate and a 100% rate -- and that a commission is
    // never negative and never more than the revenue it was taken from.

    private static readonly System.Text.Json.JsonSerializerOptions WireOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    /// <summary>Every money amount in the report, as written into the JSON, carries no sign.</summary>
    private static void AssertUnsignedOnTheWire(DealerReportDto report)
    {
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(report, WireOptions));
        foreach (var name in new[] { "revenue", "commission", "netAfterCommission", "inProgress" })
        {
            var amount = json.RootElement.GetProperty(name).GetProperty("amount").GetRawText();
            Assert.False(amount.StartsWith('-'), $"{name} was written as {amount}");
        }
    }

    private static Context WithRevenue(params RevenueFact[] facts)
    {
        var context = new Context();
        context.Bookings.RevenueAsync(Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(facts);
        return context;
    }

    [Fact]
    public async Task An_empty_period_reports_zero_everywhere_in_the_platform_currency()
    {
        var context = WithRevenue();

        var report = (await context.Handlers().Handle(new GetDealerReportQuery(OwnerId, "monthly"), CancellationToken.None)).Value;

        Assert.Equal(0, report.Bookings);
        foreach (var money in new[] { report.Revenue, report.Commission, report.NetAfterCommission, report.InProgress })
        {
            Assert.Equal(0m, money.Amount);
            Assert.Equal(Khadra.Domain.Common.Money.JordanianDinar, money.Currency);
        }

        // No sign reaches the wire for nothing earned: every amount is written 0, never -0.
        AssertUnsignedOnTheWire(report);
    }

    [Fact]
    public async Task A_zero_percent_rate_takes_no_commission_and_the_net_is_the_revenue()
    {
        var context = WithRevenue(new RevenueFact(Guid.NewGuid(), "Returned", Build.Now.AddDays(-1), 120m, "JOD", 0m));

        var report = (await context.Handlers().Handle(new GetDealerReportQuery(OwnerId, "monthly"), CancellationToken.None)).Value;

        Assert.Equal(120m, report.Revenue.Amount);
        Assert.Equal(0m, report.Commission.Amount);
        Assert.Equal(120m, report.NetAfterCommission.Amount);
        AssertUnsignedOnTheWire(report);
    }

    [Fact]
    public async Task A_hundred_percent_rate_takes_everything_and_the_net_is_exactly_zero()
    {
        var context = WithRevenue(new RevenueFact(Guid.NewGuid(), "Completed", Build.Now.AddDays(-1), 87.125m, "JOD", 100m));

        var report = (await context.Handlers().Handle(new GetDealerReportQuery(OwnerId, "monthly"), CancellationToken.None)).Value;

        Assert.Equal(87.125m, report.Commission.Amount);
        Assert.Equal(0m, report.NetAfterCommission.Amount);
        AssertUnsignedOnTheWire(report);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7.5)]
    [InlineData(12.3456)]
    [InlineData(33.3333)]
    [InlineData(99.9999)]
    [InlineData(100)]
    public async Task Commission_is_never_negative_and_never_more_than_the_revenue(double rate)
    {
        var percent = (decimal)rate;
        var context = WithRevenue(
            new RevenueFact(Guid.NewGuid(), "Returned", Build.Now.AddDays(-1), 0.001m, "JOD", percent),
            new RevenueFact(Guid.NewGuid(), "Returned", Build.Now.AddDays(-2), 19.999m, "JOD", percent),
            new RevenueFact(Guid.NewGuid(), "Completed", Build.Now.AddDays(-3), 333.333m, "JOD", percent));

        var report = (await context.Handlers().Handle(new GetDealerReportQuery(OwnerId, "monthly"), CancellationToken.None)).Value;

        Assert.True(report.Commission.Amount >= 0m, $"commission {report.Commission.Amount} at {percent}%");
        Assert.True(report.Commission.Amount <= report.Revenue.Amount, $"commission {report.Commission.Amount} over revenue {report.Revenue.Amount}");
        Assert.True(report.NetAfterCommission.Amount >= 0m);
        Assert.Equal(report.Revenue.Amount - report.Commission.Amount, report.NetAfterCommission.Amount);
    }

    /// <summary>
    /// A handover whose car has left the fleet carries no label, and no English one either: the
    /// console says "no longer listed" in its reader's language.
    /// </summary>
    [Fact]
    public async Task A_handover_for_a_car_no_longer_in_the_fleet_has_no_label()
    {
        var context = new Context();
        context.Bookings.UpcomingPickupsAsync(Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new UpcomingHandover(Guid.NewGuid(), "KH-2", "Confirmed", Build.Now.AddHours(4), "SelfPickup", Guid.NewGuid(), null, false)]);

        var dashboard = (await context.Handlers().Handle(new GetDealerDashboardQuery(OwnerId), CancellationToken.None)).Value;

        var pickup = Assert.Single(dashboard.UpcomingPickups);
        Assert.Null(pickup.VehicleLabel);
        Assert.Null(pickup.CustomerName);
    }

    [Fact]
    public async Task The_week_starts_on_Sunday_and_the_month_on_the_first_in_the_Amman_calendar()
    {
        var context = new Context();

        var weekly = await context.Handlers().Handle(new GetDealerReportQuery(OwnerId, "weekly"), CancellationToken.None);
        var monthly = await context.Handlers().Handle(new GetDealerReportQuery(OwnerId, "monthly"), CancellationToken.None);

        // Build.Now is Thursday 3 September 2026 (13:00 in Amman).
        Assert.Equal(new DateOnly(2026, 8, 30), weekly.Value.From);
        Assert.Equal(new DateOnly(2026, 9, 5), weekly.Value.To);
        Assert.Equal(new DateOnly(2026, 9, 1), monthly.Value.From);
        Assert.Equal(new DateOnly(2026, 9, 30), monthly.Value.To);
    }

    [Fact]
    public async Task An_employee_without_the_grant_gets_no_money_anywhere()
    {
        var context = new Context();
        context.Dealer.HireEmployee(EmployeeId, canViewReports: false, Build.Now);
        context.Dealers.GetByStaffUserIdAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(context.Dealer);

        var report = await context.Handlers().Handle(new GetDealerReportQuery(EmployeeId, "monthly"), CancellationToken.None);
        var dashboard = await context.Handlers().Handle(new GetDealerDashboardQuery(EmployeeId), CancellationToken.None);

        Assert.Equal("dealer.reports_not_granted", report.Error.Code);
        Assert.Equal(ErrorKind.Forbidden, report.Error.Kind);
        Assert.True(dashboard.IsSuccess);
        Assert.Null(dashboard.Value.RevenueThisMonth);
        Assert.Null(dashboard.Value.OccupancyPercentLast30Days);
        // But the operational half of the dashboard is theirs.
        Assert.Equal(3, dashboard.Value.Bookings.Requested);
    }

    [Fact]
    public async Task The_grant_opens_the_reports_to_an_employee()
    {
        var context = new Context();
        var employee = context.Dealer.HireEmployee(EmployeeId, canViewReports: false, Build.Now).Value;
        context.Dealer.SetEmployeeReportAccess(employee.Id, true);
        context.Dealers.GetByStaffUserIdAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(context.Dealer);

        var report = await context.Handlers().Handle(new GetDealerReportQuery(EmployeeId, "monthly"), CancellationToken.None);

        Assert.True(report.IsSuccess, report.IsFailure ? report.Error.Code : null);
    }
}
