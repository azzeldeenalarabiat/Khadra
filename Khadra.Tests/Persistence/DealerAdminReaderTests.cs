using Khadra.Application.Common;
using Khadra.Application.Dealers.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

// The dealer queue is worked from the top, so what sits at the top is the whole point of the screen.
// ReviewDueAt is stamped on EVERY dealer at submission and never cleared, so ordering the list by it
// alone sorted decided dealerships in among the applications and buried the one an admin actually
// owed a decision on.
public sealed class DealerAdminReaderTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public DealerAdminReaderTests()
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

    private static Dealer Registered(string name, string registration, DateTimeOffset submittedAt)
    {
        var dealer = Dealer.Register(
            Id.New(),
            BusinessName.Create(name).Value,
            CommercialRegistrationNumber.Create(registration).Value,
            Build.Amman,
            Build.NineToFive,
            submittedAt,
            Build.ReviewSla);
        Build.AttachAllDocuments(dealer, submittedAt);
        return dealer;
    }

    private async Task<IReadOnlyList<DealerListItem>> ListAsync(params Dealer[] dealers)
    {
        await using (var write = new KhadraDbContext(_options))
        {
            write.Dealers.AddRange(dealers);
            await write.SaveChangesAsync();
        }

        await using var read = new KhadraDbContext(_options);
        var page = await new DealerAdminReader(read).ListAsync(
            new DealerListFilter(null, null, null),
            new PageRequest(1, 20));
        return page.Items;
    }

    [Fact]
    public async Task An_application_awaiting_a_decision_outranks_every_settled_dealership()
    {
        // Approved eight months ago: its ReviewDueAt is the oldest date in the table, which is
        // exactly what used to float it to the top of a queue it has no business being in.
        var settledLongAgo = Registered("Mafraq Motors", "907811", Build.Now.AddMonths(-8));
        settledLongAgo.Approve(Id.New(), Build.Now.AddMonths(-8).AddHours(3));

        // Submitted yesterday and still waiting.
        var waiting = Registered("Madaba Car Hire", "902331", Build.Now.AddDays(-1));

        var rows = await ListAsync(settledLongAgo, waiting);

        Assert.Equal("Madaba Car Hire", rows[0].BusinessName);
        Assert.Equal("Mafraq Motors", rows[1].BusinessName);
    }

    [Fact]
    public async Task Applications_awaiting_a_decision_are_ordered_by_their_own_deadline()
    {
        var nearest = Registered("Salt Vehicle Rental", "903701", Build.Now.AddHours(-40));
        var later = Registered("Wadi Rum Motors", "900961", Build.Now.AddHours(-2));
        var overdue = Registered("Jerash Rentals", "899591", Build.Now.AddHours(-60));

        var rows = await ListAsync(nearest, later, overdue);

        Assert.Equal(
            ["Jerash Rentals", "Salt Vehicle Rental", "Wadi Rum Motors"],
            rows.Select(row => row.BusinessName));
    }

    [Fact]
    public async Task A_dealership_sent_back_for_clarification_is_not_awaiting_the_admin()
    {
        // The ball is with the dealer until they resubmit, so it belongs below the applications an
        // admin can actually act on -- however close its old deadline was.
        var sentBack = Registered("Ajloun Auto", "905071", Build.Now.AddHours(-47));
        sentBack.RequestClarification(Id.New(), "The registration scan is cut off.", Build.Now.AddHours(-1));

        var waiting = Registered("Madaba Car Hire", "902331", Build.Now.AddHours(-1));

        var rows = await ListAsync(sentBack, waiting);

        Assert.Equal("Madaba Car Hire", rows[0].BusinessName);
        Assert.Equal("Ajloun Auto", rows[1].BusinessName);
    }

    [Fact]
    public async Task Paging_cannot_drop_or_repeat_a_dealership_that_ties_on_every_other_key()
    {
        // Same submission instant, same status: without the id tiebreak the order between these is
        // whatever the database feels like, and a row can appear on both pages or neither.
        var moment = Build.Now.AddDays(-1);
        var dealers = Enumerable.Range(0, 6)
            .Select(index => Registered($"Twin {index}", $"90000{index}", moment))
            .ToArray();

        await using (var write = new KhadraDbContext(_options))
        {
            write.Dealers.AddRange(dealers);
            await write.SaveChangesAsync();
        }

        await using var read = new KhadraDbContext(_options);
        var reader = new DealerAdminReader(read);
        var filter = new DealerListFilter(null, null, null);

        var first = await reader.ListAsync(filter, new PageRequest(1, 3));
        var second = await reader.ListAsync(filter, new PageRequest(2, 3));

        var seen = first.Items.Concat(second.Items).Select(row => row.DealerId).ToList();
        Assert.Equal(6, seen.Count);
        Assert.Equal(6, seen.Distinct().Count());
    }

    [Fact]
    public async Task Suspended_only_answers_with_the_suspended_dealerships_and_nothing_else()
    {
        var trading = Registered("Petra Wheels", "892741", Build.Now.AddMonths(-3));
        trading.Approve(Id.New(), Build.Now.AddMonths(-3).AddHours(2));

        var suspended = Registered("Dead Sea Drive", "898221", Build.Now.AddMonths(-5));
        suspended.Approve(Id.New(), Build.Now.AddMonths(-5).AddHours(2));
        suspended.Suspend(Id.New(), "Unresolved disputes on three consecutive bookings.", Build.Now);

        await using (var write = new KhadraDbContext(_options))
        {
            write.Dealers.AddRange(trading, suspended);
            await write.SaveChangesAsync();
        }

        await using var read = new KhadraDbContext(_options);
        var page = await new DealerAdminReader(read).ListAsync(
            new DealerListFilter(null, SuspendedOnly: true, null),
            new PageRequest(1, 20));

        var row = Assert.Single(page.Items);
        Assert.Equal("Dead Sea Drive", row.BusinessName);
        Assert.True(row.IsSuspended);
    }
}
