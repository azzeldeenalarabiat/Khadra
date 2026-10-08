using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Dealers;
using Khadra.Application.Fleet.VehicleCalendar;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Fleet;
using Khadra.Domain.Fleet.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Fleet;

/// <summary>
/// One car's month, cut into the PLATFORM's calendar days on the server (pre-launch item 54): which booking holds the
/// car each day, the turnaround before a rental, and nothing a browser's own time zone decides.
/// </summary>
public sealed class VehicleCalendarTests
{
    private static readonly Id OwnerId = Id.New();
    private readonly Dealer _dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
    private readonly Vehicle _vehicle;
    private readonly IDealerRepository _dealers = Substitute.For<IDealerRepository>();
    private readonly IVehicleRepository _vehicles = Substitute.For<IVehicleRepository>();
    private readonly IDealerBookingReader _bookings = Substitute.For<IDealerBookingReader>();
    private readonly DateTimeOffset _now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    public VehicleCalendarTests()
    {
        _vehicle = Build.Vehicle(dealerId: _dealer.Id);
        _dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(_dealer);
        _vehicles.GetByIdAsync(_vehicle.Id, Arg.Any<CancellationToken>()).Returns(_vehicle);
    }

    private Task<CSharpFunctionalExtensions.Result<VehicleCalendarDto, Error>> OctoberAsync(params VehicleHold[] holds)
    {
        _bookings.VehicleHoldsAsync(_dealer.Id, _vehicle.Id, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), _now, Arg.Any<CancellationToken>())
            .Returns(holds);
        return new GetVehicleCalendarHandler(
                new DealerMembershipResolver(_dealers), _vehicles, _bookings, DocumentFixtures.Amman, new TestClock(_now))
            .Handle(new GetVehicleCalendarQuery(OwnerId, _vehicle.Id, 2026, 10), CancellationToken.None);
    }

    private static VehicleHold Hold(string status, DateTimeOffset start, DateTimeOffset end, string reference = "KH-AAAA1111") =>
        new(Guid.NewGuid(), reference, status, start.AddHours(-2), start, end);

    [Fact]
    public async Task The_month_is_asked_for_and_cut_in_ammans_calendar()
    {
        var result = await OctoberAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(31, result.Value.Days.Count);
        Assert.Equal(new DateOnly(2026, 10, 1), result.Value.Days[0].Date);
        // Midnight in Amman (UTC+3), not in UTC and not in a browser's zone.
        await _bookings.Received(1).VehicleHoldsAsync(
            _dealer.Id, _vehicle.Id,
            new DateTimeOffset(2026, 9, 30, 21, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 31, 21, 0, 0, TimeSpan.Zero),
            _now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_rental_covers_its_amman_days_and_its_turnaround_the_day_before()
    {
        // 22:00 UTC on the 5th is 01:00 on the 6th in Amman; it ends 08:00 on the 7th in Amman. In UTC this booking
        // would have started on the 5th. Its turnaround, two hours earlier, is 23:00 on the 5th in Amman.
        var hold = Hold("Confirmed", new DateTimeOffset(2026, 10, 5, 22, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 7, 5, 0, 0, TimeSpan.Zero));

        var days = (await OctoberAsync(hold)).Value.Days;

        Assert.True(days[4].Turnaround);
        Assert.Equal(hold.BookingId, days[4].BookingId);
        Assert.False(days[5].Turnaround);
        Assert.Equal("Confirmed", days[5].Status);
        Assert.Equal("KH-AAAA1111", days[6].Reference);
        Assert.Null(days[7].Status);
        Assert.Null(days[3].BookingId);
    }

    [Fact]
    public async Task A_day_a_rental_covers_is_that_rentals_even_when_the_next_ones_turnaround_starts_on_it()
    {
        var first = Hold("Confirmed", new DateTimeOffset(2026, 10, 10, 6, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 12, 6, 0, 0, TimeSpan.Zero), "KH-FIRST111");
        // Starts 00:30 on the 13th in Amman, so its turnaround begins 22:30 on the 12th — a day the first rental covers.
        var next = Hold("Approved", new DateTimeOffset(2026, 10, 12, 21, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero), "KH-NEXT2222");

        var days = (await OctoberAsync(first, next)).Value.Days;

        Assert.Equal("KH-FIRST111", days[11].Reference);
        Assert.False(days[11].Turnaround);
        Assert.Equal("KH-NEXT2222", days[12].Reference);
    }

    [Fact]
    public async Task A_car_collected_and_not_yet_back_is_out_until_today()
    {
        // Due back on 25 September; today is 1 October in Amman.
        var overdue = Hold("PickedUp", new DateTimeOffset(2026, 9, 20, 6, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 25, 6, 0, 0, TimeSpan.Zero));

        var days = (await OctoberAsync(overdue)).Value.Days;

        Assert.Equal("PickedUp", days[0].Status);
        Assert.Null(days[1].Status);
    }

    [Fact]
    public async Task Another_dealerships_car_is_not_found()
    {
        var theirs = Build.Vehicle();
        _vehicles.GetByIdAsync(theirs.Id, Arg.Any<CancellationToken>()).Returns(theirs);

        var result = await new GetVehicleCalendarHandler(
                new DealerMembershipResolver(_dealers), _vehicles, _bookings, DocumentFixtures.Amman, new TestClock(_now))
            .Handle(new GetVehicleCalendarQuery(OwnerId, theirs.Id, 2026, 10), CancellationToken.None);

        Assert.Equal(FleetErrors.NotYours, result.Error);
        await _bookings.DidNotReceiveWithAnyArgs().VehicleHoldsAsync(default, default, default, default, default, default);
    }
}
