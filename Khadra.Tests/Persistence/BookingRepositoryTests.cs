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
        var released = Build.Booking(vehicleId: vehicleId, period: Build.Period(start, days: 3), pricing: Build.Pricing(days: 3));
        released.Cancel(BookingParty.Customer, Id.New(), "Changed plans.", Build.Now.AddMinutes(5));
        var elsewhere = Build.ApprovedBooking();

        await using (var context = NewContext())
        {
            var held = Build.Booking(vehicleId: vehicleId, period: Build.Period(start, days: 3), pricing: Build.Pricing(days: 3));
            held.ConfirmDepositPaid(Id.New(), Build.Now);
            held.Approve(Id.New(), Build.Now);
            context.Bookings.AddRange(held, released, elsewhere);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var repository = new BookingRepository(reader);

        // Same dates, same car: blocked.
        Assert.True(await repository.HasOverlappingBookingAsync(vehicleId, period, null));
        // Same dates, a different car: free.
        Assert.False(await repository.HasOverlappingBookingAsync(Id.New(), period, null));
        // Back-to-back: a car returned at 10:00 can be collected at 10:00 (half-open intervals).
        var following = DateRange.Create(period.End, period.End.AddDays(2)).Value;
        Assert.False(await repository.HasOverlappingBookingAsync(vehicleId, following, null));
        // Partly overlapping: still blocked.
        var straddling = DateRange.Create(period.End.AddHours(-1), period.End.AddDays(1)).Value;
        Assert.True(await repository.HasOverlappingBookingAsync(vehicleId, straddling, null));
    }

    [Fact]
    public async Task A_cancelled_booking_does_not_block_the_car()
    {
        var vehicleId = Id.New();
        var period = Build.Period(Build.Now.AddDays(10), days: 3);
        var cancelled = Build.Booking(vehicleId: vehicleId, period: period, pricing: Build.Pricing(days: 3));
        cancelled.Cancel(BookingParty.Customer, Id.New(), "Changed plans.", Build.Now.AddMinutes(5));

        await using (var context = NewContext())
        {
            context.Bookings.Add(cancelled);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        Assert.False(await new BookingRepository(reader).HasOverlappingBookingAsync(vehicleId, period, null));
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
