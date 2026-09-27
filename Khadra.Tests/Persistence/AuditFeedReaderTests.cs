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

// The dashboard's activity strip, against the real EF model on SQLite.
//
// It had no tests until it began reading a second table: each entry now carries the booking it is
// about, looked up through the dispute ticket for a Dispute entry. That is a correlated subquery on a
// nullable converted id, which is exactly the kind of query that compiles and then fails at runtime,
// so it is proven here rather than on the dashboard.
public sealed class AuditFeedReaderTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    private static readonly DateTimeOffset Noon = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly Id AdminId = Id.New();

    public AuditFeedReaderTests()
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
        AuditAction action,
        AuditEntityType entityType,
        string subject,
        Id? entityId = null) =>
        AuditEntry.By(
            AdminId,
            "Azzeldeen Al-Arabiat",
            UserRole.Admin,
            action,
            entityType,
            entityId ?? Id.New(),
            subject,
            occurredAt);

    private async Task GivenAsync(params AuditEntry[] entries)
    {
        await using var context = new KhadraDbContext(_options);
        context.AuditEntries.AddRange(entries);
        await context.SaveChangesAsync();
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

    /// <summary>
    /// The entry the owner found reading "Dispute on KH-NY8AHLNK" inside an Arabic sentence.
    ///
    /// Its label is an English sentence that can never be rewritten, so the feed sends the booking
    /// reference beside it, read through the ticket, and the record id the console words a customer
    /// from. The label itself still arrives exactly as it was stored.
    /// </summary>
    [Fact]
    public async Task Each_entry_carries_its_record_and_the_booking_it_is_about()
    {
        var (booking, ticket) = await GivenDisputedBookingAsync();
        var reference = booking.Reference.Value;
        var dealerId = Id.New();
        await GivenAsync(
            Entry(Noon, AuditAction.DisputeResolved, AuditEntityType.Dispute, $"Dispute on {reference}", ticket.Id),
            Entry(Noon.AddMinutes(-1), AuditAction.BookingCancelledByAdmin, AuditEntityType.Booking, reference, booking.Id),
            Entry(Noon.AddMinutes(-2), AuditAction.DealerApproved, AuditEntityType.Dealer, "Aqaba Coast Cars", dealerId));

        await using var context = new KhadraDbContext(_options);

        var feed = await new AuditFeedReader(context).RecentAsync(10);

        Assert.Equal(3, feed.Count);
        var (dispute, cancellation, approval) = (feed[0], feed[1], feed[2]);

        Assert.Equal(ticket.Id, dispute.EntityId);
        Assert.Equal(reference, dispute.BookingReference);
        Assert.Equal($"Dispute on {reference}", dispute.SubjectLabel);

        Assert.Equal(booking.Id, cancellation.EntityId);
        Assert.Equal(reference, cancellation.BookingReference);

        Assert.Equal(dealerId, approval.EntityId);
        Assert.Null(approval.BookingReference);
        Assert.Equal(nameof(AuditAction.DealerApproved), approval.Action);
        Assert.Equal(nameof(AuditEntityType.Dealer), approval.EntityType);
    }

    [Fact]
    public async Task The_newest_entries_come_first_and_the_size_caps_the_feed()
    {
        await GivenAsync(
            Entry(Noon.AddHours(-2), AuditAction.DealerApproved, AuditEntityType.Dealer, "Oldest"),
            Entry(Noon, AuditAction.DealerApproved, AuditEntityType.Dealer, "Newest"),
            Entry(Noon.AddHours(-1), AuditAction.DealerApproved, AuditEntityType.Dealer, "Middle"));

        await using var context = new KhadraDbContext(_options);

        var feed = await new AuditFeedReader(context).RecentAsync(2);

        Assert.Equal(["Newest", "Middle"], feed.Select(entry => entry.SubjectLabel));
    }

    /// <summary>
    /// Entries that share an instant are cut in the same place on every read.
    ///
    /// A handler writes its action and its audit line off one clock read, so ties are ordinary. The
    /// id is UUIDv7, unique and in time order, so the newest of a tie is always the one kept.
    /// </summary>
    [Fact]
    public async Task Entries_sharing_an_instant_are_cut_in_the_same_place_every_time()
    {
        var entries = Enumerable.Range(0, 6)
            .Select(index => Entry(Noon, AuditAction.DealerApproved, AuditEntityType.Dealer, $"Dealer {index}"))
            .ToArray();
        await GivenAsync(entries);

        await using var context = new KhadraDbContext(_options);

        var feed = await new AuditFeedReader(context).RecentAsync(3);

        Assert.Equal(
            entries.OrderByDescending(entry => entry.Id.Value).Take(3).Select(entry => entry.Id),
            feed.Select(entry => entry.Id));
    }

    [Fact]
    public async Task A_feed_sized_to_nothing_reads_nothing()
    {
        await GivenAsync(Entry(Noon, AuditAction.DealerApproved, AuditEntityType.Dealer, "Aqaba Coast Cars"));

        await using var context = new KhadraDbContext(_options);

        Assert.Empty(await new AuditFeedReader(context).RecentAsync(0));
    }
}
