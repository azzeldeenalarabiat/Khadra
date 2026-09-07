using Khadra.Domain.Disputes;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Reporting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

// The dispute queue is paged, and its sort keys are not unique: tickets opened in the same second
// share a deadline, and tickets closed in the same second share ClosedAt and OpenedAt too. Without a
// tiebreak the database is free to order a tie differently on each query, so a page boundary falling
// inside one drops a ticket from every page or shows it on two — and a dropped dispute is one nobody
// is working.
//
// These read the SQL rather than paging rows on purpose. Paging cannot prove it: SQLite returns a
// tied scan in a stable order, so a row-based test passes with the tiebreak deleted, which is worse
// than no test. PostgreSQL, which serves this in production, promises no such thing.
public sealed class DisputeAdminReaderTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly KhadraDbContext _context;

    public DisputeAdminReaderTests()
    {
        _connection.Open();
        _context = new KhadraDbContext(
            new DbContextOptionsBuilder<KhadraDbContext>()
                .UseSqlite(_connection)
                .UseSnakeCaseNamingConvention()
                .Options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private string SqlFor(bool live) =>
        DisputeAdminReader.OrderForQueue(_context.DisputeTickets, live).ToQueryString();

    [Fact]
    public void The_live_queue_orders_by_deadline_and_breaks_the_tie_on_the_id()
    {
        var sql = SqlFor(live: true);
        var orderBy = sql[sql.LastIndexOf("ORDER BY", StringComparison.Ordinal)..];

        Assert.Contains("sla_deadline", orderBy, StringComparison.Ordinal);
        Assert.Contains("id", orderBy, StringComparison.Ordinal);
        // The id must come after the deadline, or it is the sort rather than the tiebreak.
        Assert.True(
            orderBy.LastIndexOf("id", StringComparison.Ordinal)
                > orderBy.IndexOf("sla_deadline", StringComparison.Ordinal),
            $"the id must break the tie, not lead the sort: {orderBy}");
    }

    [Fact]
    public void The_closed_list_orders_by_both_dates_and_breaks_the_tie_on_the_id()
    {
        var sql = SqlFor(live: false);
        var orderBy = sql[sql.LastIndexOf("ORDER BY", StringComparison.Ordinal)..];

        Assert.Contains("closed_at", orderBy, StringComparison.Ordinal);
        Assert.Contains("opened_at", orderBy, StringComparison.Ordinal);
        Assert.True(
            orderBy.LastIndexOf("id", StringComparison.Ordinal)
                > orderBy.IndexOf("opened_at", StringComparison.Ordinal),
            $"the id must break the tie, not lead the sort: {orderBy}");
    }

    [Fact]
    public void Neither_order_leaves_a_tie_for_the_database_to_settle()
    {
        // The id is the primary key, so an ORDER BY that ends with it is total by construction. This
        // is the property that makes paging safe; the two tests above pin how each list reaches it.
        foreach (var live in new[] { true, false })
        {
            var sql = SqlFor(live);
            var orderBy = sql[sql.LastIndexOf("ORDER BY", StringComparison.Ordinal)..].TrimEnd();
            Assert.EndsWith("id", orderBy.Split('.')[^1].Trim().TrimEnd('"'), StringComparison.Ordinal);
        }
    }
}
