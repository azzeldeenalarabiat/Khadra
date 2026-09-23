using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Which bookings are owed a reminder, and the database key that makes a second one impossible.
/// </summary>
public sealed class BookingReminderPersistenceTests : IDisposable
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public BookingReminderPersistenceTests()
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

    private async Task<Booking> Store(Booking booking)
    {
        booking.ClearDomainEvents();
        await using var context = NewContext();
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
        return booking;
    }

    private async Task<IReadOnlyList<Khadra.Application.Bookings.Reminders.ReminderCandidate>> Due(
        ReminderKind kind, DateTimeOffset now, TimeSpan lead)
    {
        await using var context = NewContext();
        return await new ReminderCandidateReader(context).ListDueAsync(kind, now, lead);
    }

    [Fact]
    public async Task A_confirmed_booking_is_due_a_pickup_reminder_inside_the_hour_and_not_before()
    {
        var booking = await Store(Build.ConfirmedBooking());
        var start = booking.Period.Start;

        Assert.Empty(await Due(ReminderKind.Pickup, start.AddMinutes(-61), Hour));

        var due = Assert.Single(await Due(ReminderKind.Pickup, start.AddMinutes(-60), Hour));
        Assert.Equal(booking.Id, due.BookingId);
        Assert.Equal(booking.CustomerId, due.CustomerId);
        Assert.Equal(start, due.AnchorAt);
        Assert.Equal(booking.Reference.Value, due.Reference);
    }

    [Fact]
    public async Task Never_once_the_moment_has_passed()
    {
        var booking = await Store(Build.ConfirmedBooking());

        Assert.Empty(await Due(ReminderKind.Pickup, booking.Period.Start, Hour));
        Assert.Empty(await Due(ReminderKind.Pickup, booking.Period.Start.AddMinutes(5), Hour));
    }

    [Fact]
    public async Task A_process_that_was_down_still_sends_it_late_while_the_moment_is_ahead()
    {
        var booking = await Store(Build.ConfirmedBooking());

        Assert.Single(await Due(ReminderKind.Pickup, booking.Period.Start.AddMinutes(-2), Hour));
    }

    [Fact]
    public async Task Once_recorded_it_is_never_due_again()
    {
        var booking = await Store(Build.ConfirmedBooking());
        var now = booking.Period.Start.AddMinutes(-50);
        await using (var context = NewContext())
        {
            context.BookingReminders.Add(BookingReminder.Record(booking.Id, ReminderKind.Pickup, booking.Period.Start, now));
            await context.SaveChangesAsync();
        }

        Assert.Empty(await Due(ReminderKind.Pickup, now.AddMinutes(1), Hour));
    }

    [Fact]
    public async Task The_database_refuses_a_second_reminder_for_the_same_moment()
    {
        var booking = await Store(Build.ConfirmedBooking());
        var now = booking.Period.Start.AddMinutes(-50);
        await using (var context = NewContext())
        {
            context.BookingReminders.Add(BookingReminder.Record(booking.Id, ReminderKind.Pickup, booking.Period.Start, now));
            await context.SaveChangesAsync();
        }

        await using var racing = NewContext();
        racing.BookingReminders.Add(BookingReminder.Record(booking.Id, ReminderKind.Pickup, booking.Period.Start, now));
        await Assert.ThrowsAnyAsync<Exception>(() => racing.SaveChangesAsync());
    }

    [Fact]
    public async Task An_unpaid_approval_is_due_a_payment_reminder_before_its_deadline()
    {
        var booking = await Store(Build.ApprovedBooking());
        var deadline = booking.PaymentDeadline!.Value;

        var due = Assert.Single(await Due(ReminderKind.Payment, deadline.AddMinutes(-30), TimeSpan.FromMinutes(30)));
        Assert.Equal(deadline, due.AnchorAt);
        Assert.Empty(await Due(ReminderKind.Payment, deadline.AddMinutes(-31), TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public async Task A_paid_booking_is_never_reminded_to_pay()
    {
        var booking = await Store(Build.ConfirmedBooking());

        Assert.Empty(await Due(ReminderKind.Payment, Build.Now.AddMinutes(100), TimeSpan.FromMinutes(30)));
        Assert.Empty(await Due(ReminderKind.Pickup, Build.Now.AddMinutes(100), TimeSpan.FromMinutes(30)));
        _ = booking;
    }

    [Fact]
    public async Task A_car_that_is_out_is_due_a_return_reminder_and_one_that_is_back_is_not()
    {
        var booking = Build.ConfirmedBooking();
        booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start.AddMinutes(5));
        await Store(booking);
        var end = booking.Period.End;

        Assert.Single(await Due(ReminderKind.Return, end.AddMinutes(-45), Hour));
        Assert.Empty(await Due(ReminderKind.Pickup, booking.Period.Start.AddMinutes(-45), Hour));

        await using (var context = NewContext())
        {
            var stored = await context.Bookings.Include(b => b.StatusHistory).SingleAsync(b => b.Id == booking.Id);
            stored.RecordReturn(BookingParty.Dealer, Id.New(), end.AddMinutes(-50));
            await context.SaveChangesAsync();
        }

        Assert.Empty(await Due(ReminderKind.Return, end.AddMinutes(-45), Hour));
    }
}
