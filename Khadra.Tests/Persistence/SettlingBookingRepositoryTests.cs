using Khadra.Application.Common;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The seam: every single-booking load arrives settled against the clock, and none of them writes.
/// </summary>
/// <remarks>
/// <para>
/// Seventeen call sites load one booking and act on it. Asking each to remember the lapse rule is
/// asking to be right seventeen times and wrong on the eighteenth, so the decorator is the one place
/// they all pass through.
/// </para>
/// <para>
/// The property that makes it safe is the one hardest to see by reading: it settles the aggregate in
/// memory and NEVER saves. A query gets the truth and writes nothing; a command persists the
/// settlement inside its own transaction. The "does not write" test below is the one that would
/// catch a future change turning every read of a lapsed booking into a database write — which on a
/// list screen would be one write per row, on the read path, under load.
/// </para>
/// <para>
/// Every handler test in the suite substitutes <c>IBookingRepository</c>, so none of them exercises
/// this. It only runs against a real provider, which is why it lives here.
/// </para>
/// </remarks>
public sealed class SettlingBookingRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public SettlingBookingRepositoryTests()
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

    private static SettlingBookingRepository RepositoryOn(KhadraDbContext context, DateTimeOffset now) =>
        new(new BookingRepository(context), new TestClock(now));

    /// <summary>A request nobody answered, stored exactly as the sweep would have left it: Requested.</summary>
    private async Task<Booking> GivenAnUnansweredRequestAsync()
    {
        var booking = Build.Booking(now: Build.Now, period: Build.Period(Build.Now.AddDays(30)));
        booking.ClearDomainEvents();

        await using var write = NewContext();
        write.Bookings.Add(booking);
        await write.SaveChangesAsync();
        return booking;
    }

    [Fact]
    public async Task A_lapsed_booking_arrives_settled()
    {
        var stored = await GivenAnUnansweredRequestAsync();
        var afterTheWindow = stored.DecisionDeadline.AddMinutes(1);

        await using var read = NewContext();
        var loaded = await RepositoryOn(read, afterTheWindow).GetByIdAsync(stored.Id);

        Assert.NotNull(loaded);
        Assert.Same(BookingStatus.Expired, loaded!.Status);
    }

    /// <summary>
    /// The rule the owner set: a read must not correct the stored row.
    /// </summary>
    [Fact]
    public async Task Loading_a_lapsed_booking_writes_nothing()
    {
        var stored = await GivenAnUnansweredRequestAsync();
        var afterTheWindow = stored.DecisionDeadline.AddMinutes(1);

        await using (var read = NewContext())
        {
            var loaded = await RepositoryOn(read, afterTheWindow).GetByIdAsync(stored.Id);
            Assert.Same(BookingStatus.Expired, loaded!.Status);
            // No SaveChangesAsync here, exactly as a query handler would not call one.
        }

        // A fresh context, straight at the row: still Requested, because nothing saved.
        await using var verify = NewContext();
        var row = await verify.Bookings.AsNoTracking().SingleAsync(booking => booking.Id == stored.Id);
        Assert.Same(BookingStatus.Requested, row.Status);
    }

    /// <summary>
    /// ...and a command in the same scope persists it, along with whatever it came to do.
    /// </summary>
    [Fact]
    public async Task A_command_that_saves_persists_the_settlement()
    {
        var stored = await GivenAnUnansweredRequestAsync();
        var afterTheWindow = stored.DecisionDeadline.AddMinutes(1);

        await using (var write = NewContext())
        {
            var loaded = await RepositoryOn(write, afterTheWindow).GetByIdAsync(stored.Id);
            Assert.NotNull(loaded);
            await write.SaveChangesAsync();
        }

        await using var verify = NewContext();
        var row = await verify.Bookings.AsNoTracking().SingleAsync(booking => booking.Id == stored.Id);
        Assert.Same(BookingStatus.Expired, row.Status);
    }

    /// <summary>A booking still inside its window is handed over untouched.</summary>
    [Fact]
    public async Task A_live_booking_is_left_alone()
    {
        var stored = await GivenAnUnansweredRequestAsync();
        var insideTheWindow = stored.DecisionDeadline.AddMinutes(-1);

        await using var read = NewContext();
        var loaded = await RepositoryOn(read, insideTheWindow).GetByIdAsync(stored.Id);

        Assert.Same(BookingStatus.Requested, loaded!.Status);
    }

    /// <summary>
    /// The sweep's own queries are NOT settled on the way out.
    /// </summary>
    /// <remarks>
    /// <c>SettleDueBookingsHandler</c> calls <c>ExpireUnanswered</c> on each candidate itself. If the
    /// decorator had already transitioned them, every one of those calls would refuse — wrong status —
    /// and the sweep would log a failure for work that had in fact been done. So the list queries
    /// pass straight through, and this pins that they do.
    /// </remarks>
    [Fact]
    public async Task The_sweeps_candidate_lists_are_not_settled_on_the_way_out()
    {
        var stored = await GivenAnUnansweredRequestAsync();
        var afterTheWindow = stored.DecisionDeadline.AddMinutes(1);

        await using var read = NewContext();
        var due = await RepositoryOn(read, afterTheWindow).ListDueForDecisionExpiryAsync(afterTheWindow);

        var candidate = Assert.Single(due);
        Assert.Same(BookingStatus.Requested, candidate.Status);
        // And the sweep's own transition still succeeds, which is the point.
        Assert.True(candidate.ExpireUnanswered(afterTheWindow).IsSuccess);
    }
}
