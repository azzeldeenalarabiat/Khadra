using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

// The customer profile counts a person's bookings by status.
//
// It exists because the first version asked for `!booking.Status.IsTerminal`, which reads perfectly
// and cannot be translated: IsTerminal is a C# property on the smart enum with no column behind it,
// so EF threw and every customer profile answered 500. Domain tests cannot see that — they never
// touch a DbContext — and neither can a handler test with a substituted reader. Only a query against
// a real provider does.
public sealed class CustomerAdminReaderTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;
    private readonly Id _customerId = Id.New();

    public CustomerAdminReaderTests()
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

    private static User Customer() =>
        User.RegisterCustomer(
            EmailAddress.Create("rana@example.jo").Value,
            PhoneNumber.Create("0791234567").Value,
            PersonName.Create("Rana Sharif").Value,
            PasswordHash.FromHash("hash"),
            Build.Now.AddYears(-1));

    private async Task<CustomerProfile?> SeedAndReadAsync(params Booking[] bookings)
    {
        var user = Customer();
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(user, _customerId);

        await using (var write = new KhadraDbContext(_options))
        {
            write.Users.Add(user);
            write.Bookings.AddRange(bookings);
            await write.SaveChangesAsync();
        }

        await using var read = new KhadraDbContext(_options);
        return await new CustomerAdminReader(read).GetAsync(_customerId);
    }

    private Booking Mine(Action<Booking>? advance = null)
    {
        var booking = Build.Booking(customerId: _customerId);
        advance?.Invoke(booking);
        booking.ClearDomainEvents();
        return booking;
    }

    [Fact]
    public async Task The_profile_counts_a_customers_bookings_by_state()
    {
        // One of each shape the screen reports, plus one still running.
        var completed = Mine(booking =>
        {
            booking.Approve(Id.New(), Build.Now);
            booking.ConfirmDepositPaid(Id.New(), Build.Now);
            booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start);
            booking.RecordReturn(BookingParty.Dealer, Id.New(), booking.Period.End);
            booking.Settle(booking.Period.End.Add(booking.Terms.PostReturnSettlementWindow).AddHours(1), hasOpenDispute: false);
        });
        var cancelled = Mine(booking =>
            booking.Cancel(BookingParty.Customer, Id.New(), "Changed my mind.", Build.Now));
        var noShow = Mine(booking =>
        {
            booking.Approve(Id.New(), Build.Now);
            booking.ConfirmDepositPaid(Id.New(), Build.Now);
            booking.MarkNoShow(booking.Period.Start.Add(booking.Terms.NoShowTimeout).AddHours(1));
        });
        var live = Mine(booking =>
        {
            booking.Approve(Id.New(), Build.Now);
            booking.ConfirmDepositPaid(Id.New(), Build.Now);
        });

        var profile = await SeedAndReadAsync(completed, cancelled, noShow, live);

        Assert.NotNull(profile);
        Assert.Equal(4, profile!.Bookings.Total);
        Assert.Equal(1, profile.Bookings.Completed);
        Assert.Equal(1, profile.Bookings.Cancelled);
        Assert.Equal(1, profile.Bookings.NoShow);
        // The one that matters: approved and still ahead of its pickup is the only live booking.
        Assert.Equal(1, profile.Bookings.Live);
    }

    [Fact]
    public async Task A_customer_with_no_bookings_reads_zero_rather_than_failing()
    {
        var profile = await SeedAndReadAsync();

        Assert.NotNull(profile);
        Assert.Equal(0, profile!.Bookings.Total);
        Assert.Equal(0, profile.Bookings.Live);
    }

    [Fact]
    public async Task A_user_who_is_not_a_customer_is_not_found_through_the_customer_reader()
    {
        var admin = User.CreateAdmin(
            EmailAddress.Create("omar@khadra.jo").Value,
            PhoneNumber.Create("0790000002").Value,
            PersonName.Create("Omar Deeb").Value,
            PasswordHash.FromHash("hash"),
            Build.Now.AddYears(-1));

        await using (var write = new KhadraDbContext(_options))
        {
            write.Users.Add(admin);
            await write.SaveChangesAsync();
        }

        await using var read = new KhadraDbContext(_options);
        // Not "forbidden": the route speaks about customers, and this id is not one.
        Assert.Null(await new CustomerAdminReader(read).GetAsync(admin.Id));
    }

    [Fact]
    public async Task The_list_and_its_counts_agree_about_who_is_active()
    {
        var active = Customer();
        var suspended = User.RegisterCustomer(
            EmailAddress.Create("sami@example.jo").Value,
            PhoneNumber.Create("0791234568").Value,
            PersonName.Create("Sami Khoury").Value,
            PasswordHash.FromHash("hash"),
            Build.Now.AddYears(-1));
        suspended.Suspend("Two no-shows.", Build.Now);

        await using (var write = new KhadraDbContext(_options))
        {
            write.Users.AddRange(active, suspended);
            await write.SaveChangesAsync();
        }

        await using var read = new KhadraDbContext(_options);
        var reader = new CustomerAdminReader(read);

        var counts = await reader.CountsAsync();
        var suspendedPage = await reader.ListAsync(
            new CustomerListFilter("Suspended", null, null), new PageRequest(1, 20));

        Assert.Equal(2, counts.Total);
        Assert.Equal(1, counts.Active);
        Assert.Equal(1, counts.Suspended);
        // The filter and the count are two readings of the same fact and must not disagree.
        Assert.Equal(counts.Suspended, suspendedPage.TotalCount);
    }
}
