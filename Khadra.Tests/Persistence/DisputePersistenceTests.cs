using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Infrastructure.Persistence;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

// Round-trips a resolved dispute and a priced booking through the real EF model.
//
// These exist because the Disputes and Bookings aggregates had never been saved by anything except
// the development seeder, and the seeder happens to avoid every path that breaks. Domain tests alone
// cannot see an EF mapping fault: they never touch a DbContext.
public sealed class DisputePersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public DisputePersistenceTests()
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

    private static DisputeTicket OpenTicket(Id bookingId) =>
        DisputeTicket.Open(
            bookingId,
            Id.New(),
            BookingParty.Customer,
            "The car was never delivered.",
            TimeSpan.FromHours(48),
            Build.Now).Value;

    [Fact]
    public async Task Resolved_ticket_round_trips_with_a_full_refund()
    {
        // RefundEverything used to hand ONE Money instance to two mapped properties, which EF tracks
        // by reference and rejects as a severed association. Nothing had ever saved one, so the bug
        // was invisible: the first admin to click "refund in full" would have been the first to know.
        var ticket = OpenTicket(Id.New());
        var resolution = DisputeResolution.Create(
            DepositDisposition.RefundEverything(Money.Jod(18m)).Value,
            dealerCharge: null,
            assessedPenalty: null,
            "Refunded in full; neither side was at fault.",
            Id.New(),
            Build.Now.AddHours(6)).Value;
        ticket.Resolve(resolution);

        await using (var context = NewContext())
        {
            context.DisputeTickets.Add(ticket);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var stored = await reader.DisputeTickets.SingleAsync(t => t.Id == ticket.Id);

        Assert.Same(DisputeStatus.Resolved, stored.Status);
        // The written reason and the moment are part of the record, not decoration: both are get-only
        // and were silently dropped until mapped by hand.
        Assert.Equal("Refunded in full; neither side was at fault.", stored.Resolution!.Note);
        Assert.Equal(Build.Now.AddHours(6), stored.Resolution.ResolvedAt);
        Assert.Equal(18m, stored.Resolution.Deposit.RefundToCustomer.Amount);
        Assert.True(stored.Resolution.Deposit.RetainedByPlatform.IsZero);
        Assert.True(stored.Resolution.Deposit.TransferredToDealer.IsZero);
        // The basis is stored, not re-derived: Payments must be able to check the split on its own.
        Assert.Equal(18m, stored.Resolution.Deposit.DepositHeld.Amount);
        Assert.Equal("JOD", stored.Resolution.Deposit.DepositHeld.CurrencyCode);
    }

    [Fact]
    public async Task A_booking_can_have_only_one_live_ticket()
    {
        var bookingId = Id.New();
        await using (var context = NewContext())
        {
            context.DisputeTickets.Add(OpenTicket(bookingId));
            await context.SaveChangesAsync();
        }

        await using var second = NewContext();
        second.DisputeTickets.Add(OpenTicket(bookingId));

        // The partial unique index is what lets GetLiveByBookingAsync use SingleOrDefault at all.
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task A_resolved_ticket_does_not_block_a_new_one()
    {
        var bookingId = Id.New();
        var first = OpenTicket(bookingId);
        first.Resolve(DisputeResolution.Create(
            DepositDisposition.RefundEverything(Money.Jod(20m)).Value,
            dealerCharge: null,
            assessedPenalty: null,
            "Settled.",
            Id.New(),
            Build.Now.AddHours(2)).Value);

        await using (var context = NewContext())
        {
            context.DisputeTickets.Add(first);
            await context.SaveChangesAsync();
        }

        await using var second = NewContext();
        second.DisputeTickets.Add(OpenTicket(bookingId));
        await second.SaveChangesAsync();

        Assert.Equal(2, await second.DisputeTickets.CountAsync(t => t.BookingId == bookingId));
    }

    [Fact]
    public async Task Booking_round_trips_its_frozen_terms_and_pricing()
    {
        // Every Percentage persisted as an empty JSON object, because Percentage.Value is get-only and
        // EF does not pick it up by convention inside a ToJson type. BookingTerms exists solely to
        // freeze the rules a booking was made under, so it was freezing nothing at all.
        var booking = Build.Booking();

        await using (var context = NewContext())
        {
            context.Bookings.Add(booking);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var stored = await reader.Bookings.SingleAsync(b => b.Id == booking.Id);

        Assert.Equal(booking.Terms.CommissionPercent.Value, stored.Terms.CommissionPercent.Value);
        Assert.Equal(booking.Terms.DepositPercent.Value, stored.Terms.DepositPercent.Value);
        Assert.Equal(
            booking.Terms.DealerPenaltyMaxPercent.Value,
            stored.Terms.DealerPenaltyMaxPercent.Value);
        Assert.NotEqual(0m, stored.Terms.CommissionPercent.Value);
        Assert.Equal(booking.Pricing.DepositPercent.Value, stored.Pricing.DepositPercent.Value);
        Assert.Equal(booking.Pricing.DepositAmount.Amount, stored.Pricing.DepositAmount.Amount);

        // The get-only primitives, every one of which convention leaves out of a JSON document. The
        // settlement window is the one CanBeDisputed reads: lost, it read as zero and no booking on
        // the platform could ever be disputed after a reload.
        Assert.Equal(booking.Terms.PostReturnSettlementWindow, stored.Terms.PostReturnSettlementWindow);
        Assert.Equal(booking.Terms.FreeCancellationWindow, stored.Terms.FreeCancellationWindow);
        Assert.Equal(booking.Terms.NoShowTimeout, stored.Terms.NoShowTimeout);
        Assert.Equal(booking.Terms.PaymentWindow, stored.Terms.PaymentWindow);
        // The answer window is the only thing that ever releases a car nobody answered for. Lost in
        // the JSON it would read as zero, and every request would expire the instant it was made.
        Assert.Equal(booking.Terms.AnswerWindow, stored.Terms.AnswerWindow);
        Assert.NotEqual(TimeSpan.Zero, stored.Terms.AnswerWindow);
        Assert.Equal(booking.Terms.TurnaroundBuffer, stored.Terms.TurnaroundBuffer);
        Assert.Equal(booking.Terms.RulesVersion, stored.Terms.RulesVersion);
        // Both deadlines are real columns, and a booking still waiting for an answer has no payment
        // one at all.
        Assert.Equal(booking.DecisionDeadline, stored.DecisionDeadline);
        Assert.Null(stored.PaymentDeadline);
        Assert.Equal(booking.Pricing.Days, stored.Pricing.Days);
        Assert.NotEqual(TimeSpan.Zero, stored.Terms.PostReturnSettlementWindow);
        Assert.Equal(booking.Pricing.Mileage.IsUnlimited, stored.Pricing.Mileage.IsUnlimited);
        Assert.Equal(booking.Pricing.Mileage.DailyLimitKm, stored.Pricing.Mileage.DailyLimitKm);
    }

    [Fact]
    public async Task A_penalty_assessment_keeps_its_reason_and_moment()
    {
        var booking = Build.ConfirmedBooking();
        booking.Cancel(BookingParty.Customer, Id.New(), "Changed plans.", Build.Now.AddHours(3));

        await using (var context = NewContext())
        {
            context.Bookings.Add(booking);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var stored = await reader.Bookings.SingleAsync(b => b.Id == booking.Id);

        Assert.NotNull(stored.Penalty);
        Assert.Equal(booking.Penalty!.Reason, stored.Penalty.Reason);
        Assert.False(string.IsNullOrWhiteSpace(stored.Penalty.Reason));
        Assert.Equal(booking.Penalty.AssessedAt, stored.Penalty.AssessedAt);
        Assert.Same(BookingParty.Customer, stored.Penalty.AttributedTo);
    }
}
