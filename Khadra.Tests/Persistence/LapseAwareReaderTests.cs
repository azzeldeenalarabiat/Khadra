using Khadra.Application.Bookings.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The readers that decide what is still live, held to the domain's own verdict.
/// </summary>
/// <remarks>
/// <para>
/// Four readers counted <c>Status == Requested</c> and <c>Status == Approved</c> with no clock. A
/// request past its decision deadline and an approval past its payment deadline are over the instant
/// the deadline passes — the catalogue has already released the car — and the stored row only
/// catches up when the settlement sweep runs. On Render's free tier the API sleeps after fifteen
/// minutes idle, so that can be hours, and for the whole of it a dealer's queue showed work that did
/// not exist and a customer was told to pay for a rental the platform had let go.
/// </para>
/// <para>
/// These run the REAL queries against a real provider, because a handler test substitutes the reader
/// and cannot see any of this. And they do not assert a hand-written list of expectations: each one
/// asks <c>Booking.HasLapsed</c> what should be true and holds the SQL to that answer, the same way
/// <c>CatalogueReaderTests</c> holds the catalogue to <c>IsBookable</c>. A second statement of a rule
/// is only safe while something forces the two to agree.
/// </para>
/// </remarks>
public sealed class LapseAwareReaderTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;
    private readonly Id _dealerId = Id.New();
    private readonly Id _customerId = Id.New();

    public LapseAwareReaderTests()
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

    /// <summary>
    /// A booking of this dealer's and this customer's, advanced into some state.
    /// </summary>
    /// <param name="createdAt">
    /// When it was requested, which is what its decision deadline is measured from. Separate from the
    /// rental's start so a test can put two requests on the same far-off dates and still have one of
    /// their answer windows close before the other's.
    /// </param>
    private Booking Mine(DateTimeOffset start, Action<Booking>? advance = null, DateTimeOffset? createdAt = null)
    {
        var booking = Build.Booking(
            now: createdAt ?? start.AddDays(-1),
            period: Build.Period(start),
            dealerId: _dealerId,
            customerId: _customerId);
        advance?.Invoke(booking);
        booking.ClearDomainEvents();
        return booking;
    }

    private async Task GivenAsync(params Booking[] bookings)
    {
        await using var write = NewContext();
        write.Bookings.AddRange(bookings);
        await write.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- dealer counts

    /// <summary>
    /// The dealer's queue counts what is still waiting on them, not what the row still says.
    /// </summary>
    [Fact]
    public async Task Dealer_counts_exclude_a_request_whose_answer_window_closed()
    {
        var live = Mine(Build.Now.AddDays(30));
        var stale = Mine(Build.Now.AddDays(20));
        // Past the stale one's decision deadline, comfortably inside the live one's.
        var now = stale.DecisionDeadline.AddMinutes(5);

        await GivenAsync(live, stale);

        await using var read = NewContext();
        var counts = await new DealerBookingReader(read).CountsAsync(_dealerId, now);

        // The domain decides; the reader is held to it.
        Assert.True(stale.HasLapsed(now), "fixture: the stale request should have lapsed");
        Assert.False(live.HasLapsed(now), "fixture: the live request should not have lapsed");
        Assert.Equal(1, counts.Requested);
        // And the "oldest waiting" stamp is the live one's, not the dead one's.
        Assert.Equal(live.RequestedAt, counts.OldestRequestedAt);
    }

    /// <summary>The same rule through the other door: an approval nobody paid for.</summary>
    [Fact]
    public async Task Dealer_counts_exclude_an_approval_whose_payment_window_closed()
    {
        var paidUp = Mine(Build.Now.AddDays(30), booking =>
        {
            booking.Approve(Id.New(), Build.Now);
        });
        var unpaid = Mine(Build.Now.AddDays(25), booking =>
        {
            booking.Approve(Id.New(), Build.Now);
        });
        var now = unpaid.PaymentDeadline!.Value.AddMinutes(5);

        await GivenAsync(paidUp, unpaid);

        await using var read = NewContext();
        var counts = await new DealerBookingReader(read).CountsAsync(_dealerId, now);

        // Both approvals share a window, so at `now` BOTH have lapsed -- which is the point: the
        // figure follows the clock rather than the status, whatever the status says.
        var expected = new[] { paidUp, unpaid }.Count(booking => !booking.HasLapsed(now));
        Assert.Equal(expected, counts.AwaitingDeposit);
    }

    // ---------------------------------------------------------------- dealer pickups

    /// <summary>
    /// Staff prepare cars from this list. An approval whose window closed is a car nobody is coming
    /// for, and the platform has already put the dates back on the market.
    /// </summary>
    [Fact]
    public async Task Upcoming_pickups_exclude_an_approval_whose_payment_window_closed()
    {
        var start = Build.Now.AddDays(10);
        var unpaid = Mine(start, booking => booking.Approve(Id.New(), Build.Now));
        var now = unpaid.PaymentDeadline!.Value.AddHours(1);

        await GivenAsync(unpaid);

        await using var read = NewContext();
        var pickups = await new DealerBookingReader(read)
            .UpcomingPickupsAsync(_dealerId, now, start.AddDays(1));

        Assert.True(unpaid.HasLapsed(now), "fixture: the approval should have lapsed");
        Assert.Empty(pickups);
    }

    // ---------------------------------------------------------------- admin dashboard

    /// <summary>
    /// Active and pending follow the clock; total and today do not.
    /// </summary>
    /// <remarks>
    /// The split is deliberate and worth pinning: a booking that expired still HAPPENED, so the
    /// historical figures must not be rewritten by the effective-state rule. Only the two figures
    /// that claim something is live are allowed to move.
    /// </remarks>
    [Fact]
    public async Task Admin_counts_drop_lapsed_from_live_figures_and_keep_them_in_historical_ones()
    {
        var stale = Mine(Build.Now.AddDays(20));
        var now = stale.DecisionDeadline.AddMinutes(5);

        await GivenAsync(stale);

        await using var read = NewContext();
        var counts = await new BookingDashboardReader(read)
            .CountsAsync(createdSince: stale.CreatedAt.AddMinutes(-1), now: now);

        Assert.True(stale.HasLapsed(now), "fixture: the request should have lapsed");
        // Live figures: gone.
        Assert.Equal(0, counts.Active);
        Assert.Equal(0, counts.PendingApproval);
        // Historical figures: untouched. It still happened, and it was still created today.
        Assert.Equal(1, counts.Total);
        Assert.Equal(1, counts.Today);
    }

    // ---------------------------------------------------------------- customer next booking

    /// <summary>
    /// The most consequential of the four: this is what becomes "your deposit is due" in the app.
    /// </summary>
    [Fact]
    public async Task A_customers_next_booking_is_never_an_expired_approval()
    {
        var unpaid = Mine(Build.Now.AddDays(15), booking => booking.Approve(Id.New(), Build.Now));
        var now = unpaid.PaymentDeadline!.Value.AddMinutes(1);

        await GivenAsync(unpaid);

        await using var read = NewContext();
        var next = await new BookingReader(read).NextForCustomerAsync(_customerId, now);

        Assert.True(unpaid.HasLapsed(now), "fixture: the approval should have lapsed");
        Assert.Null(next);
    }

    /// <summary>And the same for a request the dealer never answered.</summary>
    [Fact]
    public async Task A_customers_next_booking_is_never_an_expired_request()
    {
        var stale = Mine(Build.Now.AddDays(15));
        var now = stale.DecisionDeadline.AddMinutes(1);

        await GivenAsync(stale);

        await using var read = NewContext();
        var next = await new BookingReader(read).NextForCustomerAsync(_customerId, now);

        Assert.Null(next);
    }

    /// <summary>A live approval still IS the next booking — the filter is not simply eating them.</summary>
    [Fact]
    public async Task A_live_approval_is_still_the_customers_next_booking()
    {
        var live = Mine(Build.Now.AddDays(15), booking => booking.Approve(Id.New(), Build.Now));
        var now = Build.Now.AddMinutes(1);

        await GivenAsync(live);

        await using var read = NewContext();
        var next = await new BookingReader(read).NextForCustomerAsync(_customerId, now);

        Assert.False(live.HasLapsed(now), "fixture: the approval should still be live");
        Assert.NotNull(next);
        Assert.Equal(NextBookingReason.AwaitingPayment, next!.Reason);
    }

    // ---------------------------------------------------------------- the two predicates

    /// <summary>
    /// What <c>BookingHolds.Live</c> and <c>BookingLapse.HasNotLapsedAt</c> mean to each other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// They are NOT the same question and must not be merged. <c>Live</c> asks "does this booking
    /// hold a vehicle right now" — it is the availability rule, and it answers false for a cancelled
    /// booking. <c>HasNotLapsedAt</c> asks "has a deadline passed" — it answers TRUE for a cancelled
    /// booking, because nothing lapsed; the customer ended it.
    /// </para>
    /// <para>
    /// The relationship that must hold is one-directional: <b>everything live has not lapsed.</b> If
    /// that ever breaks, a car is being held by a booking whose window has closed, which is the
    /// double-booking this platform's exclusion constraint exists to prevent. The converse is
    /// deliberately false and this test asserts that too, so nobody later "simplifies" the two into
    /// one predicate that would quietly mean neither.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Everything_live_has_not_lapsed_but_not_everything_unlapsed_is_live()
    {
        // All four requested at the same instant, on far-off dates, so the only thing separating
        // them is what happened afterwards. `now` is then placed just past the answer window they
        // all share — which makes the untouched request lapsed and leaves the rest to their own
        // rules.
        var requestedAt = Build.Now;

        var staleRequest = Mine(Build.Now.AddDays(30), createdAt: requestedAt);
        var now = staleRequest.DecisionDeadline.AddMinutes(1);

        var liveRequest = Mine(Build.Now.AddDays(40), createdAt: now.AddMinutes(1));
        var liveApproval = Mine(
            Build.Now.AddDays(45),
            booking => booking.Approve(Id.New(), now),
            createdAt: requestedAt);
        var cancelled = Mine(
            Build.Now.AddDays(35),
            booking => booking.Cancel(BookingParty.Customer, Id.New(), "Plans changed.", requestedAt.AddHours(1)),
            createdAt: requestedAt);

        await GivenAsync(liveRequest, staleRequest, liveApproval, cancelled);

        await using var read = NewContext();
        var all = read.Bookings.Where(booking => booking.DealerId == _dealerId);

        var live = await BookingHolds.Live(all, now).Select(booking => booking.Id).ToListAsync();
        var unlapsed = await all.Where(BookingLapse.HasNotLapsedAt(now))
            .Select(booking => booking.Id).ToListAsync();

        // The implication, asserted over the real query results.
        Assert.All(live, id => Assert.Contains(id, unlapsed));

        // And the converse is false: the cancelled booking never lapsed, and is not live.
        Assert.Contains(cancelled.Id, unlapsed);
        Assert.DoesNotContain(cancelled.Id, live);

        // Sanity on the fixture, so a vacuous pass is impossible.
        Assert.Contains(liveRequest.Id, live);
        Assert.DoesNotContain(staleRequest.Id, live);
        Assert.DoesNotContain(staleRequest.Id, unlapsed);
    }
}
