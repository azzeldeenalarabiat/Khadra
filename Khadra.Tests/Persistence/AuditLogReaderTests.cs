using Khadra.Application.Auditing.ReadModels;
using Khadra.Application.Common;
using Khadra.Domain.Auditing;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
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
        string subject = "Aqaba Coast Cars",
        Id? entityId = null) =>
        AuditEntry.By(
            AdminId,
            "Rania Haddad",
            UserRole.Admin,
            action ?? AuditAction.DealerApproved,
            entityType ?? AuditEntityType.Dealer,
            entityId ?? Id.New(),
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

    /// <summary>
    /// A LIKE wildcard typed into the search box is a literal, not a wildcard.
    ///
    /// `%` and `_` are LIKE metacharacters. Unescaped, an admin searching for a literal underscore
    /// was handed every row — and told, by the count beside it, that they had all matched their term.
    /// On the one screen where "this is all of it" has to be true, a search that silently means
    /// "everything" is worse than one that finds nothing.
    /// </summary>
    [Theory]
    [InlineData("_")]
    [InlineData("%")]
    [InlineData("%%")]
    public async Task A_like_wildcard_in_the_search_term_matches_nothing_rather_than_everything(string term)
    {
        await GivenAsync(
            Entry(Noon, subject: "Aqaba Coast Cars"),
            Entry(Noon.AddMinutes(-1), subject: "Petra Wheels"));

        await using var context = new KhadraDbContext(_options);

        var result = await Reader(context)
            .ListAsync(new AuditLogFilter(Search: term), new PageRequest(1, 10));

        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task A_subject_containing_an_underscore_is_still_found_by_searching_for_it()
    {
        await GivenAsync(
            Entry(Noon, entityType: AuditEntityType.Setting, subject: "commission_rate"),
            Entry(Noon.AddMinutes(-1), subject: "Aqaba Coast Cars"));

        await using var context = new KhadraDbContext(_options);

        var result = await Reader(context)
            .ListAsync(new AuditLogFilter(Search: "commission_rate"), new PageRequest(1, 10));

        var only = Assert.Single(result.Items);
        Assert.Equal("commission_rate", only.SubjectLabel);
    }

    [Fact]
    public async Task Search_matches_the_subject_or_the_actor_and_ignores_case()
    {
        await GivenAsync(Entry(Noon, subject: "Aqaba Coast Cars"));

        await using var context = new KhadraDbContext(_options);
        var reader = Reader(context);

        Assert.Equal(1, (await reader.ListAsync(new AuditLogFilter(Search: "aqaba"), new PageRequest(1, 10))).TotalCount);
        Assert.Equal(1, (await reader.ListAsync(new AuditLogFilter(Search: "RANIA"), new PageRequest(1, 10))).TotalCount);
        Assert.Equal(0, (await reader.ListAsync(new AuditLogFilter(Search: "petra"), new PageRequest(1, 10))).TotalCount);
    }

    /// <summary>
    /// A city's entry carries its Arabic name beside the English one (pre-launch item 176): both readers send it, and an
    /// administrator finds the entry by either name. An entry written without one reads back without one.
    /// </summary>
    [Fact]
    public async Task A_city_entry_carries_its_arabic_name_and_is_found_by_it()
    {
        var madaba = AuditEntry.By(
            AdminId, "Rania Haddad", UserRole.Admin, AuditAction.LookupCreated, AuditEntityType.City, Id.New(), "Madaba", Noon,
            newValue: """{"en":"Madaba","ar":"مادبا","offered":true}""", subjectLabelAr: "مادبا");
        await GivenAsync(madaba, Entry(Noon.AddMinutes(1), subject: "Aqaba Coast Cars"));

        await using var context = new KhadraDbContext(_options);
        var page = await Reader(context).ListAsync(new AuditLogFilter(Search: "مادبا"), new PageRequest(1, 10));

        var entry = Assert.Single(page.Items);
        Assert.Equal("Madaba", entry.SubjectLabel);
        Assert.Equal("مادبا", entry.SubjectLabelAr);
        var all = await Reader(context).ListAsync(new AuditLogFilter(), new PageRequest(1, 10));
        Assert.Null(all.Items.Single(item => item.SubjectLabel == "Aqaba Coast Cars").SubjectLabelAr);
        var feed = await new AuditFeedReader(context).RecentAsync(10);
        Assert.Equal("مادبا", feed.Single(item => item.SubjectLabel == "Madaba").SubjectLabelAr);
    }

    /// <summary>
    /// The System is the absence of an actor, so it needs its own filter to be reachable at all.
    ///
    /// Without it, unattended actions are the single class of entry an auditor cannot isolate — which
    /// is backwards, since an action nobody was present for is the one most worth reviewing.
    /// </summary>
    [Fact]
    public async Task Unattended_actions_can_be_isolated_from_the_ones_a_person_took()
    {
        await GivenAsync(
            Entry(Noon),
            AuditEntry.BySystem(
                AuditAction.BookingExpired,
                AuditEntityType.Booking,
                Id.New(),
                "KH-20411",
                Noon.AddMinutes(-1)));

        await using var context = new KhadraDbContext(_options);

        var result = await Reader(context)
            .ListAsync(new AuditLogFilter(SystemOnly: true), new PageRequest(1, 10));

        var only = Assert.Single(result.Items);
        Assert.Null(only.ActorUserId);
        Assert.Equal("KH-20411", only.SubjectLabel);
    }

    [Fact]
    public async Task Everything_recorded_against_one_record_can_be_read_together()
    {
        var dealerId = Id.New();
        var mine = AuditEntry.By(
            AdminId, "Rania Haddad", UserRole.Admin, AuditAction.DealerApproved,
            AuditEntityType.Dealer, dealerId, "Aqaba Coast Cars", Noon);

        await GivenAsync(mine, Entry(Noon.AddMinutes(-1), subject: "Someone else"));

        await using var context = new KhadraDbContext(_options);

        var result = await Reader(context)
            .ListAsync(new AuditLogFilter(EntityId: dealerId), new PageRequest(1, 10));

        var only = Assert.Single(result.Items);
        Assert.Equal("Aqaba Coast Cars", only.SubjectLabel);
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

    // ── The booking an entry is about ────────────────────────────────────────────────────────────

    /// <summary>
    /// A dispute entry names the booking it disputes, whichever way its label was written.
    ///
    /// Until 2026-09-27 a dispute was labelled with the English sentence "Dispute on KH-…"; since then
    /// with the bare reference. Neither kind of row can be rewritten and the console has to word both
    /// in Arabic, so the reference comes from the ticket's own booking — never out of the label.
    /// </summary>
    [Fact]
    public async Task A_dispute_entry_names_the_booking_it_disputes_whichever_way_its_label_was_written()
    {
        var (booking, ticket) = await GivenDisputedBookingAsync();
        var reference = booking.Reference.Value;
        await GivenAsync(
            Entry(Noon, AuditAction.DisputeResolved, AuditEntityType.Dispute, $"Dispute on {reference}", ticket.Id),
            Entry(Noon.AddMinutes(-1), AuditAction.DisputeAssigned, AuditEntityType.Dispute, reference, ticket.Id));

        await using var context = new KhadraDbContext(_options);

        var result = await Reader(context).ListAsync(new AuditLogFilter(), new PageRequest(1, 10));

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal(reference, item.BookingReference));
    }

    [Fact]
    public async Task A_booking_entry_names_its_own_booking()
    {
        var (booking, _) = await GivenDisputedBookingAsync();
        await GivenAsync(Entry(
            Noon, AuditAction.BookingCancelledByAdmin, AuditEntityType.Booking, booking.Reference.Value, booking.Id));

        await using var context = new KhadraDbContext(_options);

        var result = await Reader(context).ListAsync(new AuditLogFilter(), new PageRequest(1, 10));

        Assert.Equal(booking.Reference.Value, Assert.Single(result.Items).BookingReference);
    }

    /// <summary>
    /// Only a booking or a dispute has a booking, and the entry's TYPE decides which lookup runs.
    ///
    /// A dealer entry whose id happens to equal a booking's must not borrow that booking's reference,
    /// and an id that resolves to nothing — or no id at all — reads as no reference rather than as a
    /// failed page: the console then words the entry from its label, as it always did.
    /// </summary>
    [Fact]
    public async Task Other_kinds_and_unresolved_records_carry_no_booking_reference()
    {
        var (booking, ticket) = await GivenDisputedBookingAsync();
        await GivenAsync(
            Entry(Noon, AuditAction.DealerApproved, AuditEntityType.Dealer, "Aqaba Coast Cars", booking.Id),
            Entry(Noon.AddMinutes(-1), AuditAction.CustomerSuspended, AuditEntityType.Customer, "Customer 0198abcd", ticket.Id),
            Entry(Noon.AddMinutes(-2), AuditAction.DisputeResolved, AuditEntityType.Dispute, "Dispute on KH-NOTICKET"),
            Entry(Noon.AddMinutes(-3), AuditAction.BookingExpired, AuditEntityType.Booking, "KH-NOBOOKNG"),
            AuditEntry.BySystem(
                AuditAction.DisputeResolved, AuditEntityType.Dispute, null, "Dispute on KH-NORECORD", Noon.AddMinutes(-4)));

        await using var context = new KhadraDbContext(_options);

        var result = await Reader(context).ListAsync(new AuditLogFilter(), new PageRequest(1, 10));

        Assert.Equal(5, result.Items.Count);
        Assert.All(result.Items, item => Assert.Null(item.BookingReference));
    }

    /// <summary>
    /// The booking lookup translates for the engine production runs.
    ///
    /// The tests above prove what it MEANS, on SQLite; this proves Npgsql can say it. It is a CASE on
    /// the entry's type with a correlated subquery through a nullable converted id in each branch —
    /// the shape that compiles and then fails at runtime — and it sits under both the dashboard's
    /// activity strip and the whole audit log. No connection is opened.
    /// </summary>
    [Fact]
    public void The_booking_lookup_translates_for_postgresql()
    {
        var options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseNpgsql(TestHostConfiguration.UnreachableConnection)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new KhadraDbContext(options);

        var sql = context.AuditEntries
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Skip(25)
            .Take(25)
            .SelectRows(context)
            .ToQueryString();

        Assert.Contains("dispute_tickets", sql, StringComparison.Ordinal);
        Assert.Contains("bookings", sql, StringComparison.Ordinal);
        // One primary-key lookup per row, decided in SQL. Selecting Reference.Value — a property stored
        // through a converter — made EF evaluate the lookup on the client instead, as a join over a
        // ROW_NUMBER() window across every booking, on every read of every page.
        Assert.Contains("CASE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ROW_NUMBER", sql, StringComparison.Ordinal);
        // Still the TOTAL order the page boundary depends on, not lost to the projection.
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
    }

    private async Task<(Booking Booking, DisputeTicket Ticket)> GivenDisputedBookingAsync()
    {
        var booking = Build.Booking();
        var ticket = DisputeTicket.Open(
            booking.Id,
            booking.CustomerId,
            BookingParty.Customer,
            "The car was never delivered.",
            TimeSpan.FromHours(48),
            Build.Now).Value;

        await using var context = new KhadraDbContext(_options);
        context.Bookings.Add(booking);
        context.DisputeTickets.Add(ticket);
        await context.SaveChangesAsync();
        return (booking, ticket);
    }
}
