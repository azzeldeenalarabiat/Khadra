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
                .Returns(new DealerBookingCounts(3, Build.Now.AddHours(-14), 2, 1, 0));
            Bookings.UpcomingPickupsAsync(Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns([new UpcomingHandover(Guid.NewGuid(), "KH-1", "Approved", Build.Now.AddHours(4), "Delivery", CarA, "Layla Odeh", false)]);
            Bookings.UpcomingReturnsAsync(Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns([]);
            Bookings.HeldVehicleIdsAsync(Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns([CarA]);
            Bookings.ActivityAsync(Arg.Any<Id>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
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
