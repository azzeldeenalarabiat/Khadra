using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Which cars are spoken for right now.
///
/// One list decides the dealer dashboard's "available vehicles" tile, so a car wrongly left out of
/// it is advertised as free while a customer is driving it. The query used to require `now` to fall
/// inside the booking's period for EVERY holding status, which is right for a reservation and wrong
/// for a car that has already been collected: an overdue return fell out on the day it went overdue,
/// and the same payload reported that booking under "1 overdue" while counting its car as available.
///
/// A handler test cannot see any of this — <c>DealerConsoleTests</c> substitutes the reader — so
/// these run the real query against a real provider.
/// </summary>
public sealed class DealerBookingReaderTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;
    private readonly Id _dealerId = Id.New();

    public DealerBookingReaderTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new KhadraDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>
    /// A booking of this dealer's, for dates starting at <paramref name="start"/>.
    ///
    /// `Booking.Create` refuses a period that starts in the past, so every one of these is made a
    /// day before its own start — the past-dated bookings here are ones that were booked ahead and
    /// have since been overtaken by the clock, which is exactly the case under test.
    /// </summary>
    private Booking Theirs(DateTimeOffset start, Action<Booking>? advance = null, Id? vehicleId = null)
    {
        var booking = Build.Booking(
            now: start.AddDays(-1),
            period: Build.Period(start),
            dealerId: _dealerId,
            vehicleId: vehicleId);
        advance?.Invoke(booking);
        booking.ClearDomainEvents();
        return booking;
    }

    private async Task<IReadOnlyList<Guid>> HeldAsync(DateTimeOffset now, params Booking[] bookings)
    {
        await using (var write = new KhadraDbContext(_options))
        {
            write.Bookings.AddRange(bookings);
            await write.SaveChangesAsync();
        }

        await using var read = new KhadraDbContext(_options);
        return await new DealerBookingReader(read).HeldVehicleIdsAsync(_dealerId, now);
    }

    /// <summary>Paid, approved and handed over — the states before a car is physically gone.</summary>
    private static void Collect(Booking booking, DateTimeOffset at)
    {
        Reserve(booking);
        booking.RecordPickup(BookingParty.Dealer, Id.New(), at);
    }

    /// <summary>Paid and approved, but not collected: a claim on the dates, car still on the lot.</summary>
    private static void Reserve(Booking booking)
    {
        var beforeStart = booking.Period.Start.AddDays(-1);
        booking.ConfirmDepositPaid(Id.New(), beforeStart);
        booking.Approve(Id.New(), beforeStart);
    }

    /// <summary>The failure this was written for: the car is late back, and it was called available.</summary>
    [Fact]
    public async Task A_car_that_is_overdue_back_is_still_held()
    {
        var start = Build.Now.AddDays(-10);
        var late = Theirs(start, booking => Collect(booking, start));
        // Three days after a period that ended a week ago.
        var now = late.Period.End.AddDays(3);

        var held = await HeldAsync(now, late);

        Assert.Contains(late.VehicleId.Value, held);
    }

    /// <summary>RecordPickup has no time guard, so a car can leave before its period starts.</summary>
    [Fact]
    public async Task A_car_collected_early_is_held_from_the_moment_it_leaves()
    {
        var start = Build.Now.AddDays(7);
        var early = Theirs(start, booking => Collect(booking, start.AddDays(-1)));

        var held = await HeldAsync(start.AddHours(-12), early);

        Assert.Contains(early.VehicleId.Value, held);
    }

    /// <summary>A reservation is a claim on dates, so the dates still decide it.</summary>
    [Fact]
    public async Task A_reservation_holds_the_car_only_while_its_dates_are_running()
    {
        var soon = Theirs(Build.Now.AddDays(2), Reserve);
        var running = Theirs(Build.Now.AddDays(-1), Reserve);

        var held = await HeldAsync(Build.Now, soon, running);

        Assert.DoesNotContain(soon.VehicleId.Value, held);
        Assert.Contains(running.VehicleId.Value, held);
    }

    /// <summary>Once the car is back it is free, whatever the period says.</summary>
    [Fact]
    public async Task A_returned_car_is_not_held()
    {
        var start = Build.Now.AddDays(-5);
        var back = Theirs(start, booking =>
        {
            Collect(booking, start);
            booking.RecordReturn(BookingParty.Dealer, Id.New(), start.AddDays(1));
        });

        var held = await HeldAsync(start.AddDays(2), back);

        Assert.DoesNotContain(back.VehicleId.Value, held);
    }

    /// <summary>One car, two live bookings, one entry: the tile counts cars, not bookings.</summary>
    [Fact]
    public async Task A_car_spoken_for_twice_is_listed_once()
    {
        var vehicleId = Id.New();
        var start = Build.Now.AddDays(-2);
        var first = Theirs(start, booking => Collect(booking, start), vehicleId);
        var second = Theirs(Build.Now.AddDays(-1), Reserve, vehicleId);

        var held = await HeldAsync(Build.Now, first, second);

        Assert.Single(held);
        Assert.Equal(vehicleId.Value, held[0]);
    }
}
