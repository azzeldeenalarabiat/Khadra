using Khadra.Application.Bookings;
using Khadra.Application.Common;
using Khadra.Application.Notifications;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Notifications;
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
        new(new BookingRepository(context), new TestClock(now), AnnouncerOn(context));

    /// <summary>The announcer as the application wires it, staging onto the same context the seam loads into.</summary>
    private static BookingExpiryAnnouncer AnnouncerOn(KhadraDbContext context) =>
        new(new DealerTeamNotifier(new Notifier(context), new UserRepository(context)), new DealerRepository(context));

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

        // A fresh context, straight at the row: still Requested, because nothing saved — and nobody was told of an
        // expiry that never happened (Wave 4, checklist 234).
        await using var verify = NewContext();
        var row = await verify.Bookings.AsNoTracking().SingleAsync(booking => booking.Id == stored.Id);
        Assert.Same(BookingStatus.Requested, row.Status);
        Assert.Empty(await verify.Notifications.AsNoTracking().ToListAsync());
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
    /// The sweep's lists are ids (Wave 4, checklist 233), and pass straight through; the sweep then loads each
    /// booking by id, through this seam, which is what expires it and announces it — one place for every expiry.
    /// </remarks>
    [Fact]
    public async Task The_sweeps_candidate_lists_pass_through_and_the_load_settles()
    {
        var stored = await GivenAnUnansweredRequestAsync();
        var afterTheWindow = stored.DecisionDeadline.AddMinutes(1);

        await using var read = NewContext();
        var repository = RepositoryOn(read, afterTheWindow);
        var due = await repository.ListIdsDueForDecisionExpiryAsync(afterTheWindow);

        Assert.Equal([stored.Id], due);
        var loaded = await repository.GetByIdAsync(Assert.Single(due));
        Assert.Same(BookingStatus.Expired, loaded!.Status);
    }

    // ---------------------------------------------------------------- the expiry is announced with it (Wave 4, 234)

    /// <summary>
    /// A lapse settled on load and saved by ANY command is announced once, in that save: the customer told it
    /// expired, and — for an approval nobody paid — the office told as Khadra. It used to be told to nobody.
    /// </summary>
    [Fact]
    public async Task A_lapse_settled_on_load_is_announced_in_the_save_that_records_it()
    {
        var office = Build.ApprovedDealer();
        var booking = Build.ApprovedBooking(Build.Now, dealerId: office.Id);
        booking.ClearDomainEvents();
        await using (var seed = NewContext())
        {
            seed.Dealers.Add(office);
            seed.Bookings.Add(booking);
            await seed.SaveChangesAsync();
        }

        var afterTheWindow = booking.PaymentDeadline!.Value.AddMinutes(1);
        await using (var write = NewContext())
        {
            var loaded = await RepositoryOn(write, afterTheWindow).GetByIdAsync(booking.Id);
            Assert.Same(BookingStatus.Expired, loaded!.Status);
            await write.SaveChangesAsync();
        }

        await using var verify = NewContext();
        var told = await verify.Notifications.AsNoTracking().Where(row => row.SubjectId == booking.Id).ToListAsync();
        var customer = Assert.Single(told, row => row.Kind == NotificationKind.YourBookingExpired);
        Assert.Equal(booking.CustomerId, customer.RecipientUserId);
        var owner = Assert.Single(told, row => row.Kind == NotificationKind.BookingExpiredUnpaid);
        Assert.Equal(office.OwnerUserId, owner.RecipientUserId);
        Assert.True(owner.IsFromPlatform);
        Assert.Equal(2, told.Count);
    }

    /// <summary>A request the office let lapse tells only its customer, wherever it was settled (Wave 3, C7).</summary>
    [Fact]
    public async Task A_request_settled_on_load_tells_only_its_customer()
    {
        var stored = await GivenAnUnansweredRequestAsync();
        var afterTheWindow = stored.DecisionDeadline.AddMinutes(1);
        await using (var write = NewContext())
        {
            await RepositoryOn(write, afterTheWindow).GetByIdAsync(stored.Id);
            await write.SaveChangesAsync();
        }

        await using var verify = NewContext();
        var only = Assert.Single(await verify.Notifications.AsNoTracking().ToListAsync());
        Assert.Same(NotificationKind.YourBookingExpired, only.Kind);
        Assert.Equal(stored.CustomerId, only.RecipientUserId);
    }

    /// <summary>Loaded twice in one scope, a lapse is settled and announced once.</summary>
    [Fact]
    public async Task A_lapse_loaded_twice_in_one_scope_is_announced_once()
    {
        var stored = await GivenAnUnansweredRequestAsync();
        var afterTheWindow = stored.DecisionDeadline.AddMinutes(1);
        await using (var write = NewContext())
        {
            var repository = RepositoryOn(write, afterTheWindow);
            await repository.GetByIdAsync(stored.Id);
            await repository.GetByReferenceAsync(stored.Reference);
            await write.SaveChangesAsync();
        }

        await using var verify = NewContext();
        Assert.Single(await verify.Notifications.AsNoTracking().ToListAsync());
    }
}
