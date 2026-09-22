using Khadra.Domain.Bookings;
using Khadra.Domain.Disputes;
using Khadra.Application.Dealers.Console;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The change marker the dealer console polls, against the real EF model.
/// </summary>
/// <remarks>
/// A pulse is only worth having if it is WRONG IN ONE DIRECTION ONLY. A marker that moves when
/// nothing happened costs a wasted re-read; a marker that stays still when something happened leaves
/// a dealer looking at a queue that is out of date while the console is certain it is not — and that
/// is the failure the whole feature exists to prevent, so it is the one these tests are built around.
///
/// The lapse case is the one no row-watching scheme catches: a request dies when its decision
/// deadline passes, and NOTHING is written. `QueueSignatureAsync` counts the live ones for exactly
/// this reason, and the test below is what says so.
///
/// These run the real query against a real provider, because the question — does this GroupBy
/// translate — cannot be answered by substituting the reader.
/// </remarks>
public sealed class DealerPulseTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;
    private readonly Id _dealerId = Id.New();

    public DealerPulseTests()
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

    private static readonly DateTimeOffset Now = Build.Now;

    private async Task<string> TokenAsync(DateTimeOffset now)
    {
        await using var read = new KhadraDbContext(_options);
        var reader = new DealerBookingReader(read);
        return DealerPulse.TokenFor(await reader.QueueSignatureAsync(_dealerId, now));
    }

    private async Task SaveAsync(params Khadra.Domain.Bookings.Booking[] bookings)
    {
        await using var write = new KhadraDbContext(_options);
        foreach (var booking in bookings)
            booking.ClearDomainEvents();
        write.Bookings.AddRange(bookings);
        await write.SaveChangesAsync();
    }

    [Fact]
    public async Task An_empty_queue_has_a_token_and_asking_twice_gives_the_same_one()
    {
        // Stability is not a nicety here. A grouped query promises no row order, so an unordered
        // join would hand back a different token for an unchanged queue — which reads as "something
        // moved", re-reads everything, and does it again on the next poll, for ever.
        var first = await TokenAsync(Now);
        var second = await TokenAsync(Now);

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task A_new_booking_moves_the_token()
    {
        // The headline case: a customer books, and the dealer must find out without reloading.
        var before = await TokenAsync(Now);

        await SaveAsync(Build.Booking(now: Now, dealerId: _dealerId));

        Assert.NotEqual(before, await TokenAsync(Now));
    }

    [Fact]
    public async Task A_status_change_moves_the_token()
    {
        var booking = Build.Booking(now: Now, dealerId: _dealerId);
        await SaveAsync(booking);
        var before = await TokenAsync(Now);

        await using (var write = new KhadraDbContext(_options))
        {
            var stored = await write.Bookings.SingleAsync();
            Assert.True(stored.Approve(Id.New(), Now).IsSuccess);
            stored.ClearDomainEvents();
            await write.SaveChangesAsync();
        }

        // Requested:1 became Approved:1. The COUNT of bookings never changed, which is why the
        // signature groups by status rather than counting rows.
        Assert.NotEqual(before, await TokenAsync(Now));
    }

    [Fact]
    public async Task A_lapse_moves_the_token_although_nothing_was_written()
    {
        // The case that makes this more than a row-watcher. A request past its decision deadline is
        // dead the instant the clock says so — the car is already back on the market — while its row
        // still reads Requested until the settlement sweep gets to it. Nothing is written, and a
        // marker built on `updated_at` or on a row version would not move at all.
        var booking = Build.Booking(now: Now, dealerId: _dealerId);
        await SaveAsync(booking);

        var whileLive = await TokenAsync(Now);
        var afterTheDeadline = await TokenAsync(booking.DecisionDeadline.AddSeconds(1));

        Assert.NotEqual(whileLive, afterTheDeadline);
    }

    [Fact]
    public async Task A_dispute_opening_moves_the_token_although_the_status_did_not()
    {
        // The gap a signature built on status alone would have left. A customer opens a ticket and
        // the row gains its marker while reading Confirmed throughout — so grouping by status sees
        // nothing, the token holds still, and the console is stale AND certain it is not.
        var booking = Build.Booking(now: Now, dealerId: _dealerId);
        await SaveAsync(booking);
        var before = await TokenAsync(Now);

        await using (var write = new KhadraDbContext(_options))
        {
            var ticket = DisputeTicket.Open(
                booking.Id,
                booking.CustomerId,
                BookingParty.Customer,
                "The car was not as described.",
                TimeSpan.FromHours(48),
                Now).Value;
            ticket.ClearDomainEvents();
            write.DisputeTickets.Add(ticket);
            await write.SaveChangesAsync();
        }

        Assert.NotEqual(before, await TokenAsync(Now));
    }

    [Fact]
    public async Task Another_dealers_booking_does_not_move_this_dealers_token()
    {
        await SaveAsync(Build.Booking(now: Now, dealerId: _dealerId));
        var before = await TokenAsync(Now);

        await SaveAsync(Build.Booking(now: Now, dealerId: Id.New()));

        Assert.Equal(before, await TokenAsync(Now));
    }

    [Fact]
    public void The_token_is_stable_across_processes()
    {
        // SHA-256 rather than GetHashCode, which is randomised per process on strings. Behind two
        // API instances that would flip the token on every other poll and the console would re-read
        // for ever, having been told the queue changed every thirty seconds.
        var signature = new Khadra.Application.Bookings.ReadModels.DealerQueueSignature(
            [new("Requested", 2), new("Confirmed", 1)],
            3,
            0);

        // The same content in the other order is the same queue, so it is the same token.
        var reordered = new Khadra.Application.Bookings.ReadModels.DealerQueueSignature(
            [new("Confirmed", 1), new("Requested", 2)],
            3,
            0);

        Assert.Equal(DealerPulse.TokenFor(signature), DealerPulse.TokenFor(reordered));

        // Pinned. Changing how the token is built is allowed — it is opaque — but it makes every
        // open console re-read once on deploy, so it should be a decision rather than a surprise.
        Assert.Equal("de14e705f233383a", DealerPulse.TokenFor(signature));
    }
}
