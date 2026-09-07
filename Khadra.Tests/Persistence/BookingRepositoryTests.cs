using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

// The two write-side repositories, against the real EF model on SQLite. The overlap guard in
// particular is a query that is easy to get subtly wrong (closed vs half-open intervals, which
// statuses count), and a booking module will one day trust it to keep two customers out of one car.
public sealed class BookingRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public BookingRepositoryTests()
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

    private KhadraDbContext NewContext() => new(_options);

    [Fact]
    public async Task A_booking_round_trips_with_its_handovers_and_history()
    {
        var booking = Build.ApprovedBooking();
        var start = booking.Period.Start;
        booking.RecordPickup(BookingParty.Dealer, Id.New(), start, odometerKm: 41_200, fuelLevel: 1m, notes: "Clean.");

        await using (var context = NewContext())
        {
            context.Bookings.Add(booking);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var stored = await new BookingRepository(reader).GetByIdAsync(booking.Id);

        Assert.NotNull(stored);
        Assert.Same(BookingStatus.PickedUp, stored.Status);
        Assert.Single(stored.Handovers);
        Assert.Equal(41_200, stored.Handovers.Single().OdometerKm);
        // Created -> Requested -> Approved -> PickedUp.
        Assert.Equal(4, stored.StatusHistory.Count);
        Assert.NotNull(await new BookingRepository(reader).GetByReferenceAsync(booking.Reference));
    }

    [Fact]
    public async Task Overlap_is_detected_only_against_bookings_that_hold_the_vehicle()
    {
        var vehicleId = Id.New();
        var start = Build.Now.AddDays(10);
        var period = Build.Period(start, days: 3);

        // Each booking gets its OWN DateRange. A value object handed to two aggregates is tracked by
        // EF as one entity in two places, and the second booking's period arrives at the database
        // as null -- the same trap the seeder fell into with GeoPoint.
        var released = Build.Booking(vehicleId: vehicleId, period: Build.Period(start, days: 3));
        released.Cancel(BookingParty.Customer, Id.New(), "Changed plans.", Build.Now.AddMinutes(5));
        var elsewhere = Build.ApprovedBooking();

        await using (var context = NewContext())
        {
            var held = Build.Booking(vehicleId: vehicleId, period: Build.Period(start, days: 3));
            held.ConfirmDepositPaid(Id.New(), Build.Now);
            held.Approve(Id.New(), Build.Now);
            context.Bookings.AddRange(held, released, elsewhere);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var repository = new BookingRepository(reader);
        var gap = Build.TurnaroundBuffer;

        // Same dates, same car: blocked.
        Assert.True(await repository.HasOverlappingBookingAsync(vehicleId, period, gap, Build.Now, null));
        // Same dates, a different car: free.
        Assert.False(await repository.HasOverlappingBookingAsync(Id.New(), period, gap, Build.Now, null));
        // Partly overlapping: blocked.
        var straddling = DateRange.Create(period.End.AddHours(-1), period.End.AddDays(1)).Value;
        Assert.True(await repository.HasOverlappingBookingAsync(vehicleId, straddling, gap, Build.Now, null));
    }

    /// <summary>
    /// Back-to-back is no longer free. The owner settled a two-hour turnaround gap on 2026-09-07, so
    /// a car returned at 10:00 cannot go out again until 12:00 — and the boundary is exact.
    /// </summary>
    [Fact]
    public async Task A_car_cannot_go_straight_back_out_without_its_turnaround_gap()
    {
        var vehicleId = Id.New();
        var start = Build.Now.AddDays(10);
        var period = Build.Period(start, days: 3);
        var gap = Build.TurnaroundBuffer;

        await using (var context = NewContext())
        {
            var held = Build.Booking(vehicleId: vehicleId, period: Build.Period(start, days: 3));
            held.ConfirmDepositPaid(Id.New(), Build.Now);
            held.Approve(Id.New(), Build.Now);
            context.Bookings.Add(held);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var repository = new BookingRepository(reader);

        // Collected the moment it comes back: refused.
        var immediate = DateRange.Create(period.End, period.End.AddDays(2)).Value;
        Assert.True(await repository.HasOverlappingBookingAsync(vehicleId, immediate, gap, Build.Now, null));

        // One minute short of the gap: still refused.
        var justShort = DateRange.Create(period.End.Add(gap).AddMinutes(-1), period.End.AddDays(2)).Value;
        Assert.True(await repository.HasOverlappingBookingAsync(vehicleId, justShort, gap, Build.Now, null));

        // Exactly the gap: allowed. A gap equal to the buffer satisfies the buffer, which is why the
        // padding is on one edge only — padding both would double-count it and refuse this.
        var exactlyTheGap = DateRange.Create(period.End.Add(gap), period.End.AddDays(2)).Value;
        Assert.False(await repository.HasOverlappingBookingAsync(vehicleId, exactlyTheGap, gap, Build.Now, null));
    }

    /// <summary>
    /// An unpaid booking holds the car only until its payment deadline. Nothing expires those
    /// bookings yet (pre-launch checklist item 4), so without this term one abandoned checkout would
    /// keep a car off the market for good.
    /// </summary>
    [Fact]
    public async Task An_abandoned_checkout_stops_holding_the_car_once_its_deadline_passes()
    {
        var vehicleId = Id.New();
        var start = Build.Now.AddDays(10);
        var period = Build.Period(start, days: 3);
        var gap = Build.TurnaroundBuffer;

        await using (var context = NewContext())
        {
            // Left in PendingPayment: never paid, never expired.
            context.Bookings.Add(Build.Booking(vehicleId: vehicleId, period: Build.Period(start, days: 3)));
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var repository = new BookingRepository(reader);

        // Inside the payment window it is a real hold.
        Assert.True(await repository.HasOverlappingBookingAsync(vehicleId, period, gap, Build.Now, null));
        // Once the window has passed, the car is free again whether or not a job has said so.
        var afterTheDeadline = Build.Now.AddHours(1);
        Assert.False(await repository.HasOverlappingBookingAsync(vehicleId, period, gap, afterTheDeadline, null));
    }

    [Fact]
    public async Task A_cancelled_booking_does_not_block_the_car()
    {
        var vehicleId = Id.New();
        var period = Build.Period(Build.Now.AddDays(10), days: 3);
        var cancelled = Build.Booking(vehicleId: vehicleId, period: period);
        cancelled.Cancel(BookingParty.Customer, Id.New(), "Changed plans.", Build.Now.AddMinutes(5));

        await using (var context = NewContext())
        {
            context.Bookings.Add(cancelled);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        Assert.False(await new BookingRepository(reader).HasOverlappingBookingAsync(vehicleId, period, Build.TurnaroundBuffer, Build.Now, null));
    }

    [Fact]
    public async Task Only_a_live_ticket_counts_as_the_bookings_dispute()
    {
        var bookingId = Id.New();
        var resolved = DisputeTicket.Open(bookingId, Id.New(), BookingParty.Customer, "Old complaint.", TimeSpan.FromHours(48), Build.Now).Value;
        resolved.Resolve(DisputeResolution.Create(
            DepositDisposition.RefundEverything(Money.Jod(20m)).Value, null, null, "Settled.", Id.New(), Build.Now.AddHours(2)).Value);
        var live = DisputeTicket.Open(bookingId, Id.New(), BookingParty.Dealer, "New complaint.", TimeSpan.FromHours(48), Build.Now.AddDays(1)).Value;

        await using (var context = NewContext())
        {
            context.DisputeTickets.AddRange(resolved, live);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var repository = new DisputeTicketRepository(reader);

        var found = await repository.GetLiveByBookingAsync(bookingId);
        Assert.NotNull(found);
        Assert.Equal(live.Id, found.Id);
        Assert.Single(found.Statements);
        Assert.True(await repository.HasLiveTicketAsync(bookingId));
        Assert.False(await repository.HasLiveTicketAsync(Id.New()));
        Assert.Single(await repository.ListLiveAsync());
        Assert.Empty(await repository.ListBreachingSlaAsync(Build.Now));
        Assert.Single(await repository.ListBreachingSlaAsync(Build.Now.AddDays(4)));
    }
}
