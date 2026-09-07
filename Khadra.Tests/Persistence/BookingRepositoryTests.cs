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
        var booking = Build.ConfirmedBooking();
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
        var elsewhere = Build.ConfirmedBooking();

        await using (var context = NewContext())
        {
            var held = Build.Booking(vehicleId: vehicleId, period: Build.Period(start, days: 3));
            held.Approve(Id.New(), Build.Now);
            held.ConfirmDepositPaid(Id.New(), Build.Now);
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
            held.Approve(Id.New(), Build.Now);
            held.ConfirmDepositPaid(Id.New(), Build.Now);
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
    /// An approved booking holds the car only until its payment deadline. Nothing expires those
    /// bookings yet (pre-launch checklist item 4), so without this term one unpaid approval would
    /// keep a car off the market for good.
    /// </summary>
    [Fact]
    public async Task An_approved_booking_stops_holding_the_car_once_the_payment_deadline_passes()
    {
        var vehicleId = Id.New();
        var start = Build.Now.AddDays(10);
        var period = Build.Period(start, days: 3);
        var gap = Build.TurnaroundBuffer;

        await using (var context = NewContext())
        {
            // Approved and never paid, and nothing has expired it.
            var approved = Build.Booking(vehicleId: vehicleId, period: Build.Period(start, days: 3));
            approved.Approve(Id.New(), Build.Now);
            context.Bookings.Add(approved);
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

    /// <summary>
    /// The other half of the same guarantee, and the newer one: a request costs nothing now, so the
    /// dealer's answer window is the only thing that ever gets the car back if nobody replies.
    /// </summary>
    [Fact]
    public async Task An_unanswered_request_stops_holding_the_car_once_the_answer_window_closes()
    {
        var vehicleId = Id.New();
        var start = Build.Now.AddDays(10);
        var period = Build.Period(start, days: 3);
        var gap = Build.TurnaroundBuffer;
        DateTimeOffset deadline;

        await using (var context = NewContext())
        {
            var requested = Build.Booking(vehicleId: vehicleId, period: Build.Period(start, days: 3));
            deadline = requested.DecisionDeadline;
            context.Bookings.Add(requested);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var repository = new BookingRepository(reader);

        Assert.True(await repository.HasOverlappingBookingAsync(vehicleId, period, gap, deadline.AddMinutes(-1), null));
        Assert.False(await repository.HasOverlappingBookingAsync(vehicleId, period, gap, deadline, null));
    }

    /// <summary>
    /// The query the create handler clears before it inserts: exactly the holds whose clock has run
    /// out, on exactly one car.
    /// </summary>
    /// <remarks>
    /// Everything it must NOT return is the point. A live hold is somebody's booking. A confirmed
    /// one is paid for. An already-expired one is settled. Another car's stale hold is none of this
    /// customer's business. And a stale hold on THIS car in a different week is the case that made
    /// the query take a window at all: expiring it would let one customer end an unrelated booking,
    /// and losing a race over it would refuse dates that were free.
    /// </remarks>
    [Fact]
    public async Task Stale_holds_are_exactly_the_expired_clocks_that_stand_in_this_bookings_way()
    {
        var car = Id.New();
        var otherCar = Id.New();
        var start = Build.Now.AddDays(10);
        var wanted = Build.Period(start, days: 3);
        var gap = Build.TurnaroundBuffer;
        var madeLongAgo = Build.Now.AddDays(-5);

        // Overlapping this candidate, and both its clocks are long gone.
        var staleRequest = Build.Booking(vehicleId: car, now: madeLongAgo, period: Build.Period(start, days: 3));

        // Also overlapping, approved and never paid for.
        var staleApproval = Build.Booking(vehicleId: car, now: madeLongAgo, period: Build.Period(start.AddDays(1), days: 3));
        staleApproval.Approve(Id.New(), madeLongAgo);

        // Stale, same car, a different month. Nothing to do with this booking.
        var staleElsewhen = Build.Booking(vehicleId: car, now: madeLongAgo, period: Build.Period(start.AddDays(40), days: 3));

        // Made now: the dealer still has 48 hours, so this is a live hold on the same dates.
        var liveRequest = Build.Booking(vehicleId: car, period: Build.Period(start, days: 3));

        // Paid for. No clock is running on it at all.
        var confirmed = Build.Booking(vehicleId: car, now: madeLongAgo, period: Build.Period(start, days: 3));
        confirmed.Approve(Id.New(), madeLongAgo);
        confirmed.ConfirmDepositPaid(Id.New(), madeLongAgo);

        // Already settled: nothing left to expire.
        var alreadyExpired = Build.Booking(vehicleId: car, now: madeLongAgo, period: Build.Period(start, days: 3));
        alreadyExpired.ExpireUnanswered(Build.Now.AddDays(-2));

        // Another car's problem entirely.
        var elsewhere = Build.Booking(vehicleId: otherCar, now: madeLongAgo, period: Build.Period(start, days: 3));

        await using (var context = NewContext())
        {
            context.Bookings.AddRange(
                staleRequest, staleApproval, staleElsewhen, liveRequest, confirmed, alreadyExpired, elsewhere);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var stale = await new BookingRepository(reader)
            .ListStaleHoldsForVehicleAsync(car, wanted, gap, Build.Now);

        Assert.Equal(
            new[] { staleApproval.Id.Value, staleRequest.Id.Value }.OrderBy(id => id),
            stale.Select(booking => booking.Id.Value).OrderBy(id => id));
    }

    /// <summary>
    /// The window is the SAME padded window the overlap guard and the database constraint use, so a
    /// stale hold inside the turnaround gap counts: it is exactly the row that would otherwise
    /// refuse the insert after the guard had said the car was free.
    /// </summary>
    [Fact]
    public async Task A_stale_hold_inside_the_turnaround_gap_still_stands_in_the_way()
    {
        var car = Id.New();
        var gap = Build.TurnaroundBuffer;
        var theirs = Build.Period(Build.Now.AddDays(10), days: 3);
        var stale = Build.Booking(vehicleId: car, now: Build.Now.AddDays(-5), period: theirs);

        await using (var context = NewContext())
        {
            context.Bookings.Add(stale);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var repository = new BookingRepository(reader);

        // Starting half an hour after theirs ends: inside the gap, so it collides.
        var insideTheGap = DateRange.Create(theirs.End.AddMinutes(30), theirs.End.AddDays(2)).Value;
        Assert.Single(await repository.ListStaleHoldsForVehicleAsync(car, insideTheGap, gap, Build.Now));

        // Starting exactly at the gap: clear of it, so it does not.
        var atTheGap = DateRange.Create(theirs.End.Add(gap), theirs.End.AddDays(2)).Value;
        Assert.Empty(await repository.ListStaleHoldsForVehicleAsync(car, atTheGap, gap, Build.Now));
    }

    /// <summary>
    /// The boundary. A deadline that has arrived is a deadline that has passed, matching every other
    /// clock on a booking: the expiry methods refuse while <c>now &lt; deadline</c> and the hold
    /// predicate counts it while <c>deadline &gt; now</c>, so both flip at the same instant.
    /// </summary>
    [Fact]
    public async Task A_hold_becomes_stale_at_its_deadline_not_after_it()
    {
        var car = Id.New();
        var booking = Build.Booking(vehicleId: car, period: Build.Period(Build.Now.AddDays(10), days: 3));
        var deadline = booking.DecisionDeadline;

        await using (var context = NewContext())
        {
            context.Bookings.Add(booking);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var repository = new BookingRepository(reader);
        var wanted = booking.Period;
        var gap = Build.TurnaroundBuffer;

        Assert.Empty(await repository.ListStaleHoldsForVehicleAsync(car, wanted, gap, deadline.AddSeconds(-1)));
        Assert.Single(await repository.ListStaleHoldsForVehicleAsync(car, wanted, gap, deadline));
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
