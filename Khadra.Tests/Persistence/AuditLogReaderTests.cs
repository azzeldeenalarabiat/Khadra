using Khadra.Application.Auditing.ReadModels;
using Khadra.Application.Common;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Reporting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

// The audit log is the record that settles a later argument, so the property that matters most is
// not speed or shape: it is that reading it returns EVERY entry, exactly once. These pin that.
//
// Free-text search is not exercised here. It uses ILIKE, which is Postgres, and SQLite would throw
// on a query that works in production — a test that fails for the wrong reason is worse than none.
public sealed class AuditLogReaderTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    private static readonly DateTimeOffset Noon = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly Id AdminId = Id.New();

    public AuditLogReaderTests()
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

    private static AuditEntry Entry(
        DateTimeOffset occurredAt,
        AuditAction? action = null,
        AuditEntityType? entityType = null,
        string subject = "Aqaba Coast Cars") =>
        AuditEntry.By(
            AdminId,
            "Rania Haddad",
            UserRole.Admin,
            action ?? AuditAction.DealerApproved,
            entityType ?? AuditEntityType.Dealer,
            Id.New(),
            subject,
            occurredAt,
            previousValue: "PendingReview",
            newValue: "Approved",
            reason: "Documents check out.");

    private async Task GivenAsync(params AuditEntry[] entries)
    {
        await using var context = new KhadraDbContext(_options);
        context.AuditEntries.AddRange(entries);
        await context.SaveChangesAsync();
    }

    private static AuditLogReader Reader(KhadraDbContext context) => new(context);

    /// <summary>
    /// Twenty entries recorded at the SAME instant, read a page at a time.
    ///
    /// This is the case the ordering exists for. A handler records its action and its audit line in
    /// one transaction off one clock read, so ties are not hypothetical — and ordering by
    /// occurred_at alone leaves the database free to break them differently between two queries,
    /// which drops a row at a page boundary or repeats it. On most screens that is cosmetic. Here it
    /// means an auditor is shown a complete-looking log that is missing an entry.
    /// </summary>
    [Fact]
    public async Task Every_entry_is_returned_exactly_once_even_when_they_share_an_instant()
    {
        await GivenAsync([.. Enumerable.Range(0, 20).Select(_ => Entry(Noon))]);

        await using var context = new KhadraDbContext(_options);
        var reader = Reader(context);

        var seen = new List<Guid>();
        for (var page = 1; page <= 4; page++)
        {
            var result = await reader.ListAsync(new AuditLogFilter(), new PageRequest(page, 5));
            Assert.Equal(20, result.TotalCount);
            seen.AddRange(result.Items.Select(item => item.Id));
        }

        Assert.Equal(20, seen.Count);
        Assert.Equal(20, seen.Distinct().Count());
    }

    [Fact]
    public async Task Entries_come_back_newest_first()
    {
        await GivenAsync(
            Entry(Noon.AddHours(-2), subject: "Oldest"),
            Entry(Noon, subject: "Newest"),
            Entry(Noon.AddHours(-1), subject: "Middle"));

        await using var context = new KhadraDbContext(_options);

        var result = await Reader(context).ListAsync(new AuditLogFilter(), new PageRequest(1, 10));

        Assert.Equal(
            ["Newest", "Middle", "Oldest"],
            result.Items.Select(item => item.SubjectLabel));
    }

    [Fact]
    public async Task Filters_compose_rather_than_replacing_one_another()
    {
        await GivenAsync(
            Entry(Noon, AuditAction.DealerApproved, AuditEntityType.Dealer),
            Entry(Noon.AddMinutes(-1), AuditAction.DealerSuspended, AuditEntityType.Dealer),
            Entry(Noon.AddMinutes(-2), AuditAction.DisputeResolved, AuditEntityType.Dispute));

        await using var context = new KhadraDbContext(_options);
        var reader = Reader(context);

        var byAction = await reader.ListAsync(
            new AuditLogFilter(Action: nameof(AuditAction.DealerApproved)), new PageRequest(1, 10));
        Assert.Equal(1, byAction.TotalCount);

        var byEntity = await reader.ListAsync(
            new AuditLogFilter(EntityType: nameof(AuditEntityType.Dealer)), new PageRequest(1, 10));
        Assert.Equal(2, byEntity.TotalCount);

        // Both at once, and the one that satisfies only the second is excluded.
        var both = await reader.ListAsync(
            new AuditLogFilter(
                Action: nameof(AuditAction.DisputeResolved),
                EntityType: nameof(AuditEntityType.Dealer)),
            new PageRequest(1, 10));
        Assert.Equal(0, both.TotalCount);
    }

    /// <summary>
    /// An unrecognised filter value returns nothing, never everything.
    ///
    /// The validator rejects one before it reaches here, so this is the second line: if a filter ever
    /// arrives that the reader cannot honour, an empty result says "nothing matched" while an
    /// unfiltered one tells an auditor they have seen the whole log when they have seen it unfiltered.
    /// </summary>
    [Fact]
    public async Task An_unknown_filter_value_matches_nothing_rather_than_everything()
    {
        await GivenAsync(Entry(Noon), Entry(Noon.AddMinutes(-1)));

        await using var context = new KhadraDbContext(_options);

        var result = await Reader(context)
            .ListAsync(new AuditLogFilter(Action: "NoSuchAction"), new PageRequest(1, 10));

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    /// <summary>The window is half-open: from is included, before is not.</summary>
    [Fact]
    public async Task The_date_window_includes_its_start_and_excludes_its_end()
    {
        await GivenAsync(
            Entry(Noon.AddDays(-1), subject: "Before the window"),
            Entry(Noon, subject: "On the boundary"),
            Entry(Noon.AddDays(1), subject: "After the window"));

        await using var context = new KhadraDbContext(_options);

        var result = await Reader(context).ListAsync(
            new AuditLogFilter(OccurredFrom: Noon, OccurredBefore: Noon.AddDays(1)),
            new PageRequest(1, 10));

        var only = Assert.Single(result.Items);
        Assert.Equal("On the boundary", only.SubjectLabel);
    }

    /// <summary>
    /// A system entry keeps its distinction all the way to the client.
    ///
    /// A background job that expired a booking is not an administrator who cancelled one, and an
    /// accountability screen that renders both as a name has lost the difference.
    /// </summary>
    [Fact]
    public async Task A_background_job_is_reported_with_no_actor_rather_than_an_invented_one()
    {
        await GivenAsync(AuditEntry.BySystem(
            AuditAction.BookingExpired,
            AuditEntityType.Booking,
            Id.New(),
            "KH-20411",
            Noon));

        await using var context = new KhadraDbContext(_options);

        var result = await Reader(context).ListAsync(new AuditLogFilter(), new PageRequest(1, 10));

        var entry = Assert.Single(result.Items);
        Assert.Null(entry.ActorUserId);
        Assert.Null(entry.ActorRole);
        Assert.Equal(AuditEntry.SystemActorName, entry.ActorName);
    }

    [Fact]
    public async Task The_reason_and_the_change_survive_the_read()
    {
        await GivenAsync(Entry(Noon));

        await using var context = new KhadraDbContext(_options);

        var result = await Reader(context).ListAsync(new AuditLogFilter(), new PageRequest(1, 10));

        var entry = Assert.Single(result.Items);
        // The three fields the dashboard's feed leaves out, and the reason this screen exists.
        Assert.Equal("Documents check out.", entry.Reason);
        Assert.Equal("PendingReview", entry.PreviousValue);
        Assert.Equal("Approved", entry.NewValue);
        Assert.Equal(nameof(UserRole.Admin), entry.ActorRole);
    }
}
