using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Configurations.Bookings;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The stored end of a booking's dispute window (Wave 4, B5; checklist 210): written when an ending opens the window,
/// never cleared, and read only by the queries that pick candidates.
/// </summary>
/// <remarks>
/// <para>
/// The window is frozen per booking inside its terms, where SQL cannot reach it, so the payables pass bounded every
/// cancellation by TODAY's window: a booking frozen under a shorter one waited for the longer, and the sweep re-read
/// every returned and every cancelled booking each minute whatever its own window said. The column is the frozen end,
/// stored once — read here by the column itself, because the domain keeps it private: <c>DisputeWindowEndsAt</c>
/// stays the one statement of the window for every reader.
/// </para>
/// <para>
/// A booking that ended before the column existed has no stored end, and every query reads it exactly as before.
/// </para>
/// </remarks>
public sealed class DisputeWindowEndPersistenceTests : IDisposable
{
    private static readonly DateTimeOffset Now = Build.Now;

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public DisputeWindowEndPersistenceTests()
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

    private async Task SaveAsync(params Booking[] bookings)
    {
        await using var context = NewContext();
        context.Bookings.AddRange(bookings);
        await context.SaveChangesAsync();
    }

    private async Task<DateTimeOffset?> StoredEndAsync(Id bookingId)
    {
        await using var context = NewContext();
        return await context.Bookings.AsNoTracking()
            .Where(booking => booking.Id == bookingId)
            .Select(booking => EF.Property<DateTimeOffset?>(booking, BookingConfiguration.DisputeWindowEndsAtField))
            .SingleAsync();
    }

    /// <summary>A booking that ended before the column existed: its end was never stored.</summary>
    private async Task ForgetStoredEndAsync(Id bookingId)
    {
        await using var context = NewContext();
        var booking = await context.Bookings.SingleAsync(candidate => candidate.Id == bookingId);
        context.Entry(booking).Property(BookingConfiguration.DisputeWindowEndsAtField).CurrentValue = null;
        await context.SaveChangesAsync();
    }

    private static Booking Returned(DateTimeOffset? now = null, BookingTerms? terms = null)
    {
        var booking = Build.ConfirmedBooking(now, terms: terms);
        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start).IsSuccess);
        Assert.True(booking.RecordReturn(BookingParty.Dealer, Id.New(), booking.Period.End).IsSuccess);
        booking.ClearDomainEvents();
        return booking;
    }

    // ── Written once, by the four endings that open a window ─────────────────────────────────────────

    [Fact]
    public async Task Every_ending_that_opens_a_window_stores_its_end_and_completion_keeps_it()
    {
        var cancelled = Build.ConfirmedBooking();
        Assert.True(cancelled.Cancel(BookingParty.Customer, cancelled.CustomerId, "Changed plans.", Now.AddHours(1)).IsSuccess);

        var undelivered = Build.ConfirmedBooking();
        var dueAt = undelivered.Period.Start.Add(undelivered.Terms.NonDeliveryGrace);
        Assert.True(undelivered.ReportDealerNonDelivery(undelivered.CustomerId, "Nobody came.", dueAt).IsSuccess);

        var noShow = Build.ConfirmedBooking();
        Assert.True(noShow.MarkNoShow(noShow.Period.Start.Add(noShow.Terms.NoShowTimeout)).IsSuccess);

        var returned = Returned();
        var completed = Returned();
        var completedWindow = completed.DisputeWindowEndsAt!.Value;
        Assert.True(completed.Settle(completedWindow, hasOpenDispute: false).IsSuccess);
        Assert.Same(BookingStatus.Completed, completed.Status);

        await SaveAsync(cancelled, undelivered, noShow, returned, completed);

        // Exactly the end the booking itself states, for each way a window opens.
        foreach (var booking in new[] { cancelled, undelivered, noShow, returned })
            Assert.Equal(booking.DisputeWindowEndsAt, await StoredEndAsync(booking.Id));
        // At completion the property reads null — no window is running — and the column keeps the end it had.
        Assert.Null(completed.DisputeWindowEndsAt);
        Assert.Equal(completedWindow, await StoredEndAsync(completed.Id));
    }

    [Fact]
    public async Task An_ending_that_opens_no_window_stores_none()
    {
        var expired = Build.ApprovedBooking(Now);
        Assert.True(expired.ExpireUnpaid(expired.PaymentDeadline!.Value).IsSuccess);
        var rejected = Build.RequestedBooking(Now);
        Assert.True(rejected.Reject(Id.New(), BookingRejectionReason.VehicleUnavailable, "No car.", Now.AddMinutes(5)).IsSuccess);
        var live = Build.ConfirmedBooking();

        await SaveAsync(expired, rejected, live);

        Assert.Null(await StoredEndAsync(expired.Id));
        Assert.Null(await StoredEndAsync(rejected.Id));
        Assert.Null(await StoredEndAsync(live.Id));
    }

    // ── Read by the sweep's two queries ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A returned booking still inside its own window is no longer re-read every minute; it is listed the moment its
    /// window closes. One with no stored end is listed as it always was.
    /// </summary>
    [Fact]
    public async Task The_settlement_candidates_wait_for_their_own_window_and_unknown_ones_are_read_as_before()
    {
        var inside = Returned();
        var unknown = Returned();
        await SaveAsync(inside, unknown);
        await ForgetStoredEndAsync(unknown.Id);
        var windowEnds = inside.DisputeWindowEndsAt!.Value;

        await using var read = NewContext();
        var repository = new BookingRepository(read);
        var before = await repository.ListIdsDueForSettlementAsync(windowEnds.AddSeconds(-1));
        var after = await repository.ListIdsDueForSettlementAsync(windowEnds);

        Assert.DoesNotContain(inside.Id, before);
        Assert.Contains(unknown.Id, before);
        Assert.Contains(inside.Id, after);
        Assert.Contains(unknown.Id, after);
    }

    [Fact]
    public async Task The_deposit_release_candidates_wait_for_their_own_window_and_unknown_ones_are_read_as_before()
    {
        static (Booking Booking, Payment Payment) CancelledByTheOffice()
        {
            var (booking, payment) = Build.PaidBooking();
            Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "The car failed its inspection.", Now.AddHours(3)).IsSuccess);
            booking.ClearDomainEvents();
            return (booking, payment);
        }

        var (inside, insidePayment) = CancelledByTheOffice();
        var (unknown, unknownPayment) = CancelledByTheOffice();
        await using (var context = NewContext())
        {
            context.Bookings.AddRange(inside, unknown);
            context.Payments.AddRange(insidePayment, unknownPayment);
            await context.SaveChangesAsync();
        }

        await ForgetStoredEndAsync(unknown.Id);
        var windowEnds = inside.DisputeWindowEndsAt!.Value;

        await using var read = NewContext();
        var repository = new BookingRepository(read);
        var before = await repository.ListIdsDueForDepositReleaseAsync(windowEnds.AddSeconds(-1));
        var after = await repository.ListIdsDueForDepositReleaseAsync(windowEnds);

        Assert.DoesNotContain(inside.Id, before);
        Assert.Contains(unknown.Id, before);
        Assert.Contains(inside.Id, after);
        Assert.Contains(unknown.Id, after);
    }
}
