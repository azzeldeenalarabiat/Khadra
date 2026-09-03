using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

// The dealer's tabs are resolved in the database, and the list and its counts share one mapping.
// These pin that mapping to real rows: a tab and its count can never disagree, a dealer never sees
// an unpaid request, and "Disputed" cuts across status rather than being one.
public sealed class BookingReaderTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;
    private readonly Id _dealerId = Id.New();

    public BookingReaderTests()
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

    private Booking Mine(Action<Booking>? advance = null)
    {
        var booking = Build.Booking(dealerId: _dealerId);
        advance?.Invoke(booking);
        return booking;
    }

    [Fact]
    public async Task Tabs_and_their_counts_agree_and_a_dealer_never_sees_an_unpaid_request()
    {
        var unpaid = Mine();
        var requested = Mine(b => b.ConfirmDepositPaid(Id.New(), Build.Now));
        var approved = Mine(b => { b.ConfirmDepositPaid(Id.New(), Build.Now); b.Approve(Id.New(), Build.Now); });
        var cancelled = Mine(b =>
        {
            b.ConfirmDepositPaid(Id.New(), Build.Now);
            b.Approve(Id.New(), Build.Now);
            b.Cancel(BookingParty.Customer, Id.New(), "Plans changed.", Build.Now.AddHours(3));
        });
        var returned = Mine(b =>
        {
            b.ConfirmDepositPaid(Id.New(), Build.Now);
            b.Approve(Id.New(), Build.Now);
            b.RecordPickup(BookingParty.Dealer, Id.New(), b.Period.Start);
            b.RecordReturn(BookingParty.Dealer, Id.New(), b.Period.End);
        });
        var ticket = DisputeTicket.Open(returned.Id, returned.CustomerId, BookingParty.Customer, "Overcharged.", TimeSpan.FromHours(48), Build.Now.AddDays(4)).Value;

        await using (var context = NewContext())
        {
            context.Bookings.AddRange(unpaid, requested, approved, cancelled, returned);
            context.DisputeTickets.Add(ticket);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var bookings = new BookingReader(reader);
        var scope = new BookingListFilter(null, _dealerId, null);

        var counts = await bookings.TabCountsAsync(scope);
        Assert.Equal(4, counts[BookingTabs.All]);          // the unpaid one is invisible to the dealer
        Assert.Equal(1, counts[BookingTabs.Pending]);
        Assert.Equal(1, counts[BookingTabs.Upcoming]);
        Assert.Equal(0, counts[BookingTabs.Active]);
        Assert.Equal(1, counts[BookingTabs.Returned]);
        Assert.Equal(1, counts[BookingTabs.Closed]);
        Assert.Equal(1, counts[BookingTabs.Disputed]);

        foreach (var tab in BookingTabs.Names)
        {
            var page = await bookings.ListAsync(scope with { Tab = tab }, PageRequest.From(1, 50));
            Assert.Equal(counts[tab], page.TotalCount);
        }

        var disputed = await bookings.ListAsync(scope with { Tab = BookingTabs.Disputed }, PageRequest.From(1, 50));
        Assert.Equal(returned.Id.Value, disputed.Items.Single().BookingId);
        Assert.True(disputed.Items.Single().HasLiveDispute);
        Assert.Equal("Returned", disputed.Items.Single().Status);
    }

    [Fact]
    public async Task A_customer_still_sees_their_own_unpaid_booking()
    {
        var customerId = Id.New();
        var unpaid = Build.Booking(customerId: customerId);

        await using (var context = NewContext())
        {
            context.Bookings.Add(unpaid);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var counts = await new BookingReader(reader).TabCountsAsync(new BookingListFilter(customerId, null, null));

        Assert.Equal(1, counts[BookingTabs.All]);
    }

    [Fact]
    public async Task A_vehicle_filter_narrows_the_dealer_scope_to_that_car_only()
    {
        var carA = Id.New();
        var carB = Id.New();
        var onA = Build.Booking(dealerId: _dealerId, vehicleId: carA);
        onA.ConfirmDepositPaid(Id.New(), Build.Now);
        var onB = Build.Booking(dealerId: _dealerId, vehicleId: carB);
        onB.ConfirmDepositPaid(Id.New(), Build.Now);
        // Same car, another dealer: the vehicle filter never widens the caller's scope.
        var elsewhere = Build.Booking(dealerId: Id.New(), vehicleId: carA);
        elsewhere.ConfirmDepositPaid(Id.New(), Build.Now);

        await using (var context = NewContext())
        {
            context.Bookings.AddRange(onA, onB, elsewhere);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var page = await new BookingReader(reader).ListAsync(
            new BookingListFilter(null, _dealerId, null, VehicleId: carA.Value), PageRequest.From(1, 20));

        var only = Assert.Single(page.Items);
        Assert.Equal(onA.Id.Value, only.BookingId);
    }

    private KhadraDbContext NewContext() => new(_options);
}
