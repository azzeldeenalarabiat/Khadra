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

    // ── Names that no longer resolve ─────────────────────────────────────────────────────────────
    //
    // The queue is read by the Admin console alone, so a party that is gone arrives as null and the
    // console words it. It used to arrive as an English sentence, which an Arabic queue printed as it
    // came. An assigned ticket stays visibly assigned: its id is sent even when its holder's name is not.

    [Fact]
    public async Task A_row_whose_parties_are_gone_sends_null_names_and_keeps_its_holder_id()
    {
        var (booking, dealer, customer, admin) = Parties();
        Assert.True(dealer.Delete(Khadra.Tests.Support.Build.Now).IsSuccess);
        Assert.True(customer.Delete(Khadra.Tests.Support.Build.Now).IsSuccess);
        Assert.True(admin.Delete(Khadra.Tests.Support.Build.Now).IsSuccess);
        var ticket = Ticket(booking, assignTo: admin);
        await SaveAsync(booking, dealer, customer, admin, ticket);

        var row = Assert.Single((await List()).Items);

        Assert.Null(row.DealerName);
        Assert.Null(row.CustomerName);
        Assert.Equal(admin.Id.Value, row.AssignedAdminId);
        Assert.Null(row.AssignedAdminName);
        Assert.Equal(booking.Reference.Value, row.BookingReference);
    }

    [Fact]
    public async Task A_row_whose_parties_resolve_names_them_and_an_unassigned_one_has_no_holder()
    {
        var (booking, dealer, customer, admin) = Parties();
        var ticket = Ticket(booking, assignTo: null);
        await SaveAsync(booking, dealer, customer, admin, ticket);

        var row = Assert.Single((await List()).Items);

        Assert.Equal(dealer.BusinessName.Value, row.DealerName);
        Assert.Equal(customer.Name.Value, row.CustomerName);
        Assert.Null(row.AssignedAdminId);
        Assert.Null(row.AssignedAdminName);
    }

    private static (Khadra.Domain.Bookings.Booking Booking, Khadra.Domain.Dealers.Dealer Dealer, Khadra.Domain.IdentityAccess.User Customer, Khadra.Domain.IdentityAccess.User Admin) Parties()
    {
        var dealer = Khadra.Tests.Support.Build.ApprovedDealer(businessName: "Jerash Car Hire", commercialRegistration: "445566");
        var customer = Khadra.Tests.Support.Build.Customer(email: "queue@example.jo", phone: "0791112233");
        var admin = Khadra.Domain.IdentityAccess.User.CreateAdmin(
            Khadra.Domain.IdentityAccess.EmailAddress.Create("holder@khadra.jo").Value,
            Khadra.Domain.IdentityAccess.PhoneNumber.Create("0790000009").Value,
            Khadra.Domain.IdentityAccess.PersonName.Create("Sami Nasser").Value,
            Khadra.Domain.IdentityAccess.PasswordHash.FromHash("hash"),
            Khadra.Tests.Support.Build.Now.AddYears(-1));
        var booking = Khadra.Tests.Support.Build.Booking(customerId: customer.Id, dealerId: dealer.Id);
        return (booking, dealer, customer, admin);
    }

    private static DisputeTicket Ticket(Khadra.Domain.Bookings.Booking booking, Khadra.Domain.IdentityAccess.User? assignTo)
    {
        var ticket = DisputeTicket.Open(
            booking.Id,
            booking.CustomerId,
            Khadra.Domain.Bookings.BookingParty.Customer,
            "The car was not there.",
            TimeSpan.FromHours(48),
            Khadra.Tests.Support.Build.Now).Value;
        if (assignTo is not null)
            Assert.True(ticket.AssignToAdmin(assignTo.Id).IsSuccess);
        return ticket;
    }

    private async Task SaveAsync(
        Khadra.Domain.Bookings.Booking booking,
        Khadra.Domain.Dealers.Dealer dealer,
        Khadra.Domain.IdentityAccess.User customer,
        Khadra.Domain.IdentityAccess.User admin,
        DisputeTicket ticket)
    {
        customer.ClearDomainEvents();
        admin.ClearDomainEvents();
        _context.Dealers.Add(dealer);
        _context.Users.AddRange(customer, admin);
        _context.Bookings.Add(booking);
        _context.DisputeTickets.Add(ticket);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    private Task<Khadra.Application.Common.PagedResult<Khadra.Application.Disputes.ReadModels.DisputeListItem>> List() =>
        new DisputeAdminReader(_context).ListAsync(
            new Khadra.Application.Disputes.ReadModels.DisputeListFilter(null, OverdueOnly: false),
            Khadra.Application.Common.PageRequest.From(1, 50),
            Khadra.Tests.Support.Build.Now);
}
