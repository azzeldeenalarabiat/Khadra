using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

// The dealer's tabs are resolved in the database, and the list and its counts share one mapping.
// These pin that mapping to real rows: a tab and its count can never disagree, an unpaid request is
// exactly what the dealer is shown, and "Disputed" cuts across status rather than being one.
public sealed class BookingReaderTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;
    private readonly Id _dealerId = Id.New();

    public BookingReaderTests()
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

    private Booking Mine(Action<Booking>? advance = null)
    {
        var booking = Build.Booking(dealerId: _dealerId);
        advance?.Invoke(booking);
        return booking;
    }

    [Fact]
    public async Task Tabs_and_their_counts_agree_and_an_unpaid_request_is_what_the_dealer_must_answer()
    {
        var requested = Mine();
        // Answered but not paid for, and answered and paid for. Both are ahead of their pickup, so
        // both are the dealer's week: one tab, two statuses on the rows.
        var approved = Mine(b => b.Approve(Id.New(), Build.Now));
        var confirmed = Mine(b => { b.Approve(Id.New(), Build.Now); b.ConfirmDepositPaid(Id.New(), Build.Now); });
        var cancelled = Mine(b =>
        {
            b.Approve(Id.New(), Build.Now);
            b.ConfirmDepositPaid(Id.New(), Build.Now);
            b.Cancel(BookingParty.Customer, Id.New(), "Plans changed.", Build.Now.AddHours(3));
        });
        var returned = Mine(b =>
        {
            b.Approve(Id.New(), Build.Now);
            b.ConfirmDepositPaid(Id.New(), Build.Now);
            b.RecordPickup(BookingParty.Dealer, Id.New(), b.Period.Start);
            b.RecordReturn(BookingParty.Dealer, Id.New(), b.Period.End);
        });
        var ticket = DisputeTicket.Open(returned.Id, returned.CustomerId, BookingParty.Customer, "Overcharged.", TimeSpan.FromHours(48), Build.Now.AddDays(4)).Value;

        await using (var context = NewContext())
        {
            context.Bookings.AddRange(requested, approved, confirmed, cancelled, returned);
            context.DisputeTickets.Add(ticket);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var bookings = new BookingReader(reader);
        var scope = new BookingListFilter(null, _dealerId, null);

        var counts = await bookings.TabCountsAsync(scope);
        Assert.Equal(5, counts[BookingTabs.All]);          // including the request nobody has paid for
        Assert.Equal(1, counts[BookingTabs.Pending]);
        // Two statuses, one tab. This is also the regression that mattered: a tab naming more than
        // one status used to fall through to the whole scope and count 5.
        Assert.Equal(2, counts[BookingTabs.Upcoming]);
        Assert.Equal(0, counts[BookingTabs.Active]);
        Assert.Equal(1, counts[BookingTabs.Returned]);
        Assert.Equal(1, counts[BookingTabs.Closed]);
        Assert.Equal(1, counts[BookingTabs.Disputed]);

        foreach (var tab in BookingTabs.Names)
        {
            var page = await bookings.ListAsync(scope with { Tab = tab }, PageRequest.From(1, 50));
            Assert.Equal(counts[tab], page.TotalCount);
        }

        var disputed = await bookings.ListAsync(scope with { Tab = BookingTabs.Disputed }, PageRequest.From(1, 50));
        Assert.Equal(returned.Id.Value, disputed.Items.Single().BookingId);
        Assert.True(disputed.Items.Single().HasLiveDispute);
        Assert.Equal("Returned", disputed.Items.Single().Status);
    }

    [Fact]
    public async Task A_customer_still_sees_their_own_unpaid_booking()
    {
        var customerId = Id.New();
        var unpaid = Build.Booking(customerId: customerId);

        await using (var context = NewContext())
        {
            context.Bookings.Add(unpaid);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var counts = await new BookingReader(reader).TabCountsAsync(new BookingListFilter(customerId, null, null));

        Assert.Equal(1, counts[BookingTabs.All]);
    }

    [Fact]
    public async Task A_vehicle_filter_narrows_the_dealer_scope_to_that_car_only()
    {
        var carA = Id.New();
        var carB = Id.New();
        var onA = Build.Booking(dealerId: _dealerId, vehicleId: carA);
        onA.Approve(Id.New(), Build.Now);
        var onB = Build.Booking(dealerId: _dealerId, vehicleId: carB);
        onB.Approve(Id.New(), Build.Now);
        // Same car, another dealer: the vehicle filter never widens the caller's scope.
        var elsewhere = Build.Booking(dealerId: Id.New(), vehicleId: carA);
        elsewhere.Approve(Id.New(), Build.Now);

        await using (var context = NewContext())
        {
            context.Bookings.AddRange(onA, onB, elsewhere);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var page = await new BookingReader(reader).ListAsync(
            new BookingListFilter(null, _dealerId, null, VehicleId: carA.Value), PageRequest.From(1, 20));

        var only = Assert.Single(page.Items);
        Assert.Equal(onA.Id.Value, only.BookingId);
    }

    private KhadraDbContext NewContext() => new(_options);

    // ── The landing surface's one booking ────────────────────────────────────

    private readonly Id _customerId = Id.New();

    private Task<NextBooking?> NextAsync()
    {
        var context = NewContext();
        return new BookingReader(context).NextForCustomerAsync(_customerId);
    }

    private async Task SaveMineAsync(params Booking[] bookings)
    {
        await using var context = NewContext();
        context.Bookings.AddRange(bookings);
        await context.SaveChangesAsync();
    }

    /// <summary>Most people, most of the time. The screen renders nothing, never a placeholder.</summary>
    [Fact]
    public async Task A_customer_with_nothing_live_has_no_next_booking()
    {
        // Somebody else's live booking must not surface on this customer's landing.
        await SaveMineAsync(Build.ApprovedBooking(customerId: Id.New(), dealerId: _dealerId));

        Assert.Null(await NextAsync());
    }

    /// <summary>
    /// The ranking, and the reason it is the server's to make.
    /// </summary>
    /// <remarks>
    /// With no push channel (pre-launch checklist item 73) a customer learns their booking was
    /// approved by opening the app, which is exactly why the payment window is 24 hours. An approval
    /// owing a deposit therefore outranks a rental starting tomorrow -- and an app sorting by pickup
    /// date would have shown the rental and let the deposit expire unread.
    /// </remarks>
    [Fact]
    public async Task A_deposit_that_is_due_outranks_everything_else()
    {
        var awaitingPayment = Build.ApprovedBooking(customerId: _customerId, dealerId: _dealerId);
        var startingSooner = Build.ConfirmedBooking(customerId: _customerId, dealerId: _dealerId);
        var unanswered = Build.Booking(customerId: _customerId, dealerId: _dealerId);

        await SaveMineAsync(startingSooner, unanswered, awaitingPayment);

        var next = await NextAsync();

        Assert.NotNull(next);
        Assert.Equal(awaitingPayment.Id.Value, next.Booking.BookingId);
        Assert.Equal(NextBookingReason.AwaitingPayment, next.Reason);
    }

    [Fact]
    public async Task A_car_already_out_outranks_one_not_yet_collected()
    {
        var collected = Build.ConfirmedBooking(customerId: _customerId, dealerId: _dealerId);
        collected.RecordPickup(BookingParty.Dealer, Id.New(), collected.Period.Start);
        var later = Build.ConfirmedBooking(customerId: _customerId, dealerId: _dealerId);

        await SaveMineAsync(later, collected);

        var next = await NextAsync();

        Assert.Equal(collected.Id.Value, next!.Booking.BookingId);
        Assert.Equal(NextBookingReason.InProgress, next.Reason);
    }

    [Fact]
    public async Task An_unanswered_request_is_the_last_thing_shown_and_still_shown()
    {
        var unanswered = Build.Booking(customerId: _customerId, dealerId: _dealerId);
        await SaveMineAsync(unanswered);

        var next = await NextAsync();

        Assert.Equal(unanswered.Id.Value, next!.Booking.BookingId);
        Assert.Equal(NextBookingReason.AwaitingDecision, next.Reason);
    }

    /// <summary>Of two of the same kind, the one that needs attention SOONEST.</summary>
    [Fact]
    public async Task The_soonest_of_two_upcoming_rentals_wins_whatever_order_they_were_booked_in()
    {
        var later = Build.ConfirmedBooking(
            now: Build.Now.AddDays(30), customerId: _customerId, dealerId: _dealerId);
        var sooner = Build.ConfirmedBooking(customerId: _customerId, dealerId: _dealerId);

        // Saved with the LATER one first, so a fallback to insertion order would pick it.
        await SaveMineAsync(later, sooner);

        var next = await NextAsync();

        Assert.Equal(sooner.Id.Value, next!.Booking.BookingId);
        Assert.True(sooner.Period.Start < later.Period.Start);
    }

    /// <summary>A booking that has ended is not "next" by any reading.</summary>
    [Fact]
    public async Task A_finished_or_cancelled_booking_is_never_next()
    {
        var cancelled = Build.ConfirmedBooking(customerId: _customerId, dealerId: _dealerId);
        cancelled.Cancel(BookingParty.Customer, _customerId, "changed plans", Build.Now);

        var expired = Build.ApprovedBooking(customerId: _customerId, dealerId: _dealerId);
        expired.ExpireUnpaid(expired.PaymentDeadline!.Value.AddMinutes(1));

        await SaveMineAsync(cancelled, expired);

        Assert.Null(await NextAsync());
    }

}
