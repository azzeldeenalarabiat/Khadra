using Khadra.Application.Common;
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

    /// <summary>Approved and paid, but not collected: a claim on the dates, car still on the lot.</summary>
    private static void Reserve(Booking booking)
    {
        var beforeStart = booking.Period.Start.AddDays(-1);
        booking.Approve(Id.New(), beforeStart);
        booking.ConfirmDepositPaid(Id.New(), beforeStart);
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

    /// <summary>
    /// A request nobody answered is not a hold, however close its dates are.
    /// </summary>
    /// <remarks>
    /// The deadline this turns on is invisible in the arrangement, which is why it is worth spelling
    /// out: every window on a booking is capped at the rental start, so a request whose period has
    /// begun is necessarily past its own answer deadline. The catalogue released that car at the
    /// deadline. If this screen still counted it, the dealership would be told a car was spoken for
    /// on the same afternoon the customer app was offering it to somebody else.
    /// </remarks>
    [Fact]
    public async Task A_request_nobody_answered_is_not_held_once_its_dates_arrive()
    {
        var start = Build.Now.AddDays(-1);
        var unanswered = Theirs(start);

        var held = await HeldAsync(Build.Now, unanswered);

        Assert.Empty(held);
    }

    /// <summary>And the same for an approval the customer never paid for.</summary>
    [Fact]
    public async Task An_approval_nobody_paid_for_is_not_held_once_its_dates_arrive()
    {
        var start = Build.Now.AddDays(-1);
        // Approved the day it was made, which is the day before its dates begin.
        var unpaid = Theirs(start, booking => booking.Approve(Id.New(), start.AddDays(-1)));

        var held = await HeldAsync(Build.Now, unpaid);

        Assert.Empty(held);
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

    // ── Names that no longer resolve ─────────────────────────────────────────────────────────────
    //
    // The dealer console words these cases in its reader's language, so the reader sends null rather
    // than an English sentence. An actor's id travels beside the name, which is how the console tells
    // "the rental office did this" (no id) from "a former member of staff did" (an id, no name).

    [Fact]
    public async Task A_handover_whose_customer_closed_their_account_names_nobody()
    {
        var gone = Build.Customer(email: "gone@example.jo", phone: "0795556677");
        var here = Build.Customer(email: "here@example.jo", phone: "0795556678");
        Assert.True(gone.Delete(Build.Now).IsSuccess);
        var start = Build.Now.AddDays(1);
        var theirs = BookedBy(gone.Id, start, Reserve);
        var mine = BookedBy(here.Id, start.AddHours(2), Reserve);

        await SaveAsync([gone, here], theirs, mine);

        await using var read = new KhadraDbContext(_options);
        var pickups = await new DealerBookingReader(read).UpcomingPickupsAsync(_dealerId, Build.Now, Build.Now.AddDays(2));

        Assert.Null(pickups.Single(pickup => pickup.BookingId == theirs.Id.Value).CustomerName);
        Assert.Equal(here.Name.Value, pickups.Single(pickup => pickup.BookingId == mine.Id.Value).CustomerName);
    }

    [Fact]
    public async Task Activity_names_nobody_for_the_office_or_for_a_former_member_of_staff()
    {
        var former = Build.Customer(email: "former@example.jo", phone: "0794443322");
        var current = Build.Customer(email: "current@example.jo", phone: "0794443323");
        Assert.True(former.Delete(Build.Now).IsSuccess);
        var start = Build.Now.AddDays(3);
        var approvedByFormer = Theirs(start, booking => booking.Approve(former.Id, start.AddDays(-1)));
        var approvedByCurrent = Theirs(start.AddDays(1), booking => booking.Approve(current.Id, start));
        // Cancelled by the dealer with nobody signing it: the rental office acting as itself.
        var cancelledByOffice = Theirs(
            start.AddDays(2),
            booking => booking.Cancel(BookingParty.Dealer, null, "Car withdrawn from service.", start.AddDays(1)));

        await SaveAsync([former, current], approvedByFormer, approvedByCurrent, cancelledByOffice);

        await using var read = new KhadraDbContext(_options);
        var entries = (await new DealerBookingReader(read).ActivityAsync(_dealerId, PageRequest.From(1, 50))).Items;

        var byFormer = Assert.Single(entries, entry => entry.BookingId == approvedByFormer.Id.Value);
        Assert.Equal(former.Id.Value, byFormer.ActorUserId);
        Assert.Null(byFormer.ActorName);

        var byCurrent = Assert.Single(entries, entry => entry.BookingId == approvedByCurrent.Id.Value);
        Assert.Equal(current.Name.Value, byCurrent.ActorName);

        var byOffice = Assert.Single(entries, entry => entry.BookingId == cancelledByOffice.Id.Value);
        Assert.Null(byOffice.ActorUserId);
        Assert.Null(byOffice.ActorName);
    }

    /// <summary>A booking of this dealer's made by one particular customer.</summary>
    private Booking BookedBy(Id customerId, DateTimeOffset start, Action<Booking>? advance)
    {
        var booking = Build.Booking(
            now: start.AddDays(-1),
            period: Build.Period(start),
            dealerId: _dealerId,
            customerId: customerId);
        advance?.Invoke(booking);
        booking.ClearDomainEvents();
        return booking;
    }

    private async Task SaveAsync(Khadra.Domain.IdentityAccess.User[] users, params Booking[] bookings)
    {
        foreach (var user in users)
            user.ClearDomainEvents();
        await using var write = new KhadraDbContext(_options);
        write.Users.AddRange(users);
        write.Bookings.AddRange(bookings);
        await write.SaveChangesAsync();
    }
}
