using Khadra.Application.Bookings.ReadModels;
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
        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), at).IsSuccess);
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

    /// <summary>A pickup may be recorded from the booking's HoldStart (Wave 3 D4), so a car can leave before its period starts.</summary>
    [Fact]
    public async Task A_car_collected_early_is_held_from_the_moment_it_leaves()
    {
        var start = Build.Now.AddDays(7);
        var early = Theirs(start, booking => Collect(booking, start.AddHours(-1)));

        var held = await HeldAsync(start.AddMinutes(-30), early);

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

        // The office's own changes: each booking's request is the customer's, and is listed too (F27).
        bool Office(DealerActivityEntry entry) => entry.ActorParty == BookingParty.Dealer.Name;

        var byFormer = Assert.Single(entries, entry => entry.BookingId == approvedByFormer.Id.Value && Office(entry));
        Assert.Equal(former.Id.Value, byFormer.ActorUserId);
        Assert.Null(byFormer.ActorName);

        var byCurrent = Assert.Single(entries, entry => entry.BookingId == approvedByCurrent.Id.Value && Office(entry));
        Assert.Equal(current.Name.Value, byCurrent.ActorName);

        var byOffice = Assert.Single(entries, entry => entry.BookingId == cancelledByOffice.Id.Value && Office(entry));
        Assert.Null(byOffice.ActorUserId);
        Assert.Null(byOffice.ActorName);
    }

    // ── Every change, whoever made it (E2E F27, Fix & Polish Wave 3) ─────────────────────────────
    //
    // Activity promised "every change on your bookings" and listed only the office's own: a
    // customer's cancellation, a payment and an expiry never appeared. It lists them all now, and
    // names a person only on the office's own changes -- the customer is "the customer" and the
    // platform "Khadra" to an office, and an administrator's identity is not the office's to read.

    [Fact]
    public async Task Activity_lists_every_change_and_names_a_person_only_on_the_offices_own()
    {
        var staff = Build.Customer(email: "staff@example.jo", phone: "0794443324");
        var renter = Build.Customer(email: "renter@example.jo", phone: "0794443325");
        // A real account with a name, so a lookup that should not happen would be seen to.
        var administrator = Build.Customer(email: "administrator@example.jo", phone: "0794443326");
        var start = Build.Now.AddDays(3);
        var cancelledByRenter = BookedBy(
            renter.Id,
            start,
            booking => booking.Cancel(BookingParty.Customer, renter.Id, "Plans changed.", start.AddDays(-1).AddMinutes(5)));
        var lapsed = BookedBy(renter.Id, start.AddDays(1), booking => booking.ExpireUnanswered(booking.DecisionDeadline));
        var cancelledByKhadra = BookedBy(
            renter.Id,
            start.AddDays(2),
            booking => booking.Cancel(BookingParty.Admin, administrator.Id, "The listing was withdrawn.", start.AddDays(1).AddMinutes(5)));
        var approved = BookedBy(renter.Id, start.AddDays(3), booking => booking.Approve(staff.Id, start.AddDays(2).AddMinutes(5)));

        await SaveAsync([staff, renter, administrator], cancelledByRenter, lapsed, cancelledByKhadra, approved);

        await using var read = new KhadraDbContext(_options);
        var entries = (await new DealerBookingReader(read).ActivityAsync(_dealerId, PageRequest.From(1, 50))).Items;

        // Four requests and four answers to them.
        Assert.Equal(8, entries.Count);
        Assert.All(
            entries.Where(entry => entry.ToStatus == BookingStatus.Requested.Name),
            entry =>
            {
                Assert.Equal(BookingParty.Customer.Name, entry.ActorParty);
                Assert.Null(entry.ActorUserId);
                Assert.Null(entry.ActorName);
            });

        var byRenter = Assert.Single(entries, entry => entry.BookingId == cancelledByRenter.Id.Value && entry.ToStatus == BookingStatus.Cancelled.Name);
        Assert.Equal(BookingParty.Customer.Name, byRenter.ActorParty);
        Assert.Null(byRenter.ActorUserId);
        Assert.Null(byRenter.ActorName);
        Assert.Equal("Plans changed.", byRenter.Reason);

        var expiry = Assert.Single(entries, entry => entry.BookingId == lapsed.Id.Value && entry.ToStatus == BookingStatus.Expired.Name);
        Assert.Equal(BookingParty.System.Name, expiry.ActorParty);
        Assert.Equal(BookingStatus.Requested.Name, expiry.FromStatus);

        var byKhadra = Assert.Single(entries, entry => entry.BookingId == cancelledByKhadra.Id.Value && entry.ToStatus == BookingStatus.Cancelled.Name);
        Assert.Equal(BookingParty.Admin.Name, byKhadra.ActorParty);
        Assert.Null(byKhadra.ActorUserId);
        Assert.Null(byKhadra.ActorName);
        // A reason Khadra gives on a cancellation is shown to both parties.
        Assert.Equal("The listing was withdrawn.", byKhadra.Reason);

        var byStaff = Assert.Single(entries, entry => entry.BookingId == approved.Id.Value && entry.ToStatus == BookingStatus.Approved.Name);
        Assert.Equal(BookingParty.Dealer.Name, byStaff.ActorParty);
        Assert.Equal(staff.Id.Value, byStaff.ActorUserId);
        Assert.Equal(staff.Name.Value, byStaff.ActorName);
    }

    [Fact]
    public async Task A_member_of_staffs_own_record_holds_only_what_they_did_for_the_office()
    {
        var staff = Build.Customer(email: "staffer@example.jo", phone: "0794443327");
        var start = Build.Now.AddDays(3);
        // The same account as a booking's customer: its request is not office work, though the id matches.
        var requestedByThem = BookedBy(staff.Id, start, null);
        var approvedByThem = Theirs(start.AddDays(1), booking => booking.Approve(staff.Id, start));

        await SaveAsync([staff], requestedByThem, approvedByThem);

        await using var read = new KhadraDbContext(_options);
        var mine = (await new DealerBookingReader(read).ActivityAsync(_dealerId, PageRequest.From(1, 50), staff.Id)).Items;

        var only = Assert.Single(mine);
        Assert.Equal(approvedByThem.Id.Value, only.BookingId);
        Assert.Equal(BookingStatus.Approved.Name, only.ToStatus);
        Assert.Equal(staff.Name.Value, only.ActorName);
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

    /// <summary>
    /// The fleet calendar's holds on one car (pre-launch item 54): the catalogue's predicate, from each hold's own start,
    /// and nothing of another car's or of a request whose answer window has closed.
    /// </summary>
    [Fact]
    public async Task The_calendar_reads_the_live_holds_on_one_car_from_their_hold_start()
    {
        var car = Id.New();
        var start = Build.Now.AddDays(5);
        var reserved = Theirs(start, Reserve, vehicleId: car);
        // Asked for and never answered: its window closes before the rental would have started.
        var unanswered = Theirs(start.AddDays(10), vehicleId: car);
        var otherCar = Theirs(start, Reserve);
        var now = unanswered.DecisionDeadline.AddMinutes(1);

        await using (var write = new KhadraDbContext(_options))
        {
            write.Bookings.AddRange(reserved, unanswered, otherCar);
            await write.SaveChangesAsync();
        }

        await using var read = new KhadraDbContext(_options);
        var holds = await new DealerBookingReader(read).VehicleHoldsAsync(
            _dealerId, car, Build.Now, start.AddDays(30), now);

        var hold = Assert.Single(holds);
        Assert.Equal(reserved.Id.Value, hold.BookingId);
        Assert.Equal("Confirmed", hold.Status);
        Assert.Equal(reserved.Period.Start - Build.TurnaroundBuffer, hold.HoldStart);
        Assert.Equal(reserved.Period.End, hold.PeriodEnd);
    }
}
