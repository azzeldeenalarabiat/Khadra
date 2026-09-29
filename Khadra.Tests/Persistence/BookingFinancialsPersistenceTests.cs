using Khadra.Application.Bookings;
using Khadra.Application.Payments;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Disputes;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The booking's financial state (payments Phase 4) read through the REAL repositories: every handler
/// test substitutes them, so only this proves that what the calculator is handed is what was saved —
/// a resolution's deposit legs out of their JSON document, a payment's refunds and a booking's
/// handovers through their Includes, and a withdrawn ticket counted as neither resolved nor live.
/// </summary>
public sealed class BookingFinancialsPersistenceTests : IDisposable
{
    private static readonly DateTimeOffset Now = Build.Now;

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public BookingFinancialsPersistenceTests()
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

    private static DateTimeOffset AfterFreeWindow(Booking booking) => booking.FreeCancellationDeadline!.Value.AddMinutes(1);

    private static void Settle(Refund refund)
    {
        refund.MarkSent("rf_" + refund.Id.Value.ToString("N"), Now);
        refund.MarkSettled(Now);
    }

    private async Task SaveAsync(Booking booking, Payment payment, params DisputeTicket[] tickets)
    {
        await using var context = NewContext();
        context.Bookings.Add(booking);
        context.Payments.Add(payment);
        context.DisputeTickets.AddRange(tickets);
        await context.SaveChangesAsync();
    }

    /// <summary>The administrator's query, on a fresh context, the way a request runs it.</summary>
    private async Task<BookingFinancialsDto> AdminReadsAsync(Id bookingId, DateTimeOffset now)
    {
        await using var context = NewContext();
        var handlers = new BookingFinancialsHandlers(
            new BookingRepository(context),
            new PaymentRepository(context),
            new DisputeTicketRepository(context),
            new Khadra.Infrastructure.Reporting.OfficeLedgerReader(context),
            new BookingPartyResolver(Substitute.For<IDealerRepository>()),
            new TestClock(now),
            new RecordingLogger<BookingFinancialsHandlers>());

        var result = await handlers.Handle(new GetAnyBookingFinancialsQuery(bookingId), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    [Fact]
    public async Task A_resolved_dispute_and_its_refunds_reach_the_calculator_as_they_were_saved()
    {
        // The KH-NY8AHLNK shape: paid in full, cancelled late, the 18 deposit split by a dispute.
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, null, AfterFreeWindow(booking)).IsSuccess);
        Settle(BookingEndingRefunds.Record(booking, payment, Now)!);
        var ticket = DisputeTicket.Open(booking.Id, booking.CustomerId, BookingParty.Customer, "Charged twice.", TimeSpan.FromHours(48), Now.AddHours(5)).Value;
        Assert.True(ticket.Resolve(DisputeResolution.Create(
            DepositDisposition.Create(Money.Jod(18m), Money.Jod(13m), Money.Jod(0m), Money.Jod(5m)).Value,
            null, null, "Split.", Id.New(), Now.AddHours(6)).Value).IsSuccess);
        Assert.True(payment.RequestRefund(Money.Jod(13m), ticket.Id, Now.AddHours(6)).IsSuccess);
        await SaveAsync(booking, payment, ticket);

        var admin = await AdminReadsAsync(booking.Id, Now.AddHours(7));

        Assert.False(admin.NeedsReview, string.Join(", ", admin.Issues ?? []));
        Assert.Equal(DepositStates.DecidedByDispute, admin.Deposit.State);
        var decision = admin.Deposit.Decision!;
        Assert.Equal(new[] { ticket.Id.Value }, decision.TicketIds);
        Assert.Equal(13m, decision.ToCustomer!.Amount);
        Assert.Equal(5m, decision.ToOffice!.Amount);
        Assert.Equal(0m, decision.KeptByPlatform!.Amount);
        Assert.Null(decision.ChargedToOffice);
        Assert.Equal("Requested", decision.ToCustomerRefundStatus);

        var paid = Assert.Single(admin.Payments);
        Assert.Equal(94.5m, paid.AmountCharged!.Amount);
        Assert.Equal(4.5m, paid.ProcessingFee!.Amount);
        Assert.Equal(2, paid.Refunds.Count);
        Assert.Equal("EndedBeforePickup", paid.Refunds[0].Reason);
        Assert.Equal("DisputeResolution", paid.Refunds[1].Reason);
        Assert.Equal(4.5m, paid.Refunds[0].FeePart!.Amount);
        Assert.Equal(ticket.Id.Value, paid.Refunds[1].DisputeTicketId);
        Assert.Equal(76.5m, admin.Summary.Refunded.Amount);
        Assert.Equal(13m, admin.Summary.RefundInProgress.Amount);
        Assert.Equal(CommissionStates.Undecided, admin.Commission!.State);
    }

    [Fact]
    public async Task The_cash_an_office_recorded_at_pickup_reaches_the_calculator()
    {
        var (booking, payment) = Build.PaidBooking();
        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start, cashCollected: Money.Jod(222m)).IsSuccess);
        await SaveAsync(booking, payment);

        var admin = await AdminReadsAsync(booking.Id, booking.Period.Start.AddHours(1));

        Assert.Equal(BalanceStates.CashAtHandover, admin.Balance.State);
        Assert.Equal(72m, admin.Balance.Amount.Amount);
        var cash = Assert.Single(admin.Balance.CashRecorded);
        Assert.Equal("Pickup", cash.Handover);
        Assert.Equal(222m, cash.Amount.Amount);
        Assert.Equal(DepositStates.AppliedToRental, admin.Deposit.State);
    }

    [Fact]
    public async Task A_withdrawn_ticket_is_neither_resolved_nor_live_and_an_open_one_holds_the_deposit()
    {
        var (withdrawnBooking, withdrawnPayment) = Build.PaidBooking();
        Assert.True(withdrawnBooking.Cancel(BookingParty.Dealer, Id.New(), "No car.", Now.AddHours(3)).IsSuccess);
        var withdrawn = DisputeTicket.Open(withdrawnBooking.Id, withdrawnBooking.CustomerId, BookingParty.Customer, "Never came.", TimeSpan.FromHours(48), Now.AddHours(4)).Value;
        Assert.True(withdrawn.Withdraw(withdrawnBooking.CustomerId, Now.AddHours(5)).IsSuccess);
        await SaveAsync(withdrawnBooking, withdrawnPayment, withdrawn);

        var (openBooking, openPayment) = Build.PaidBooking();
        Assert.True(openBooking.Cancel(BookingParty.Dealer, Id.New(), "No car.", Now.AddHours(3)).IsSuccess);
        var open = DisputeTicket.Open(openBooking.Id, openBooking.CustomerId, BookingParty.Customer, "Never came.", TimeSpan.FromHours(48), Now.AddHours(4)).Value;
        await SaveAsync(openBooking, openPayment, open);

        var afterWithdrawal = await AdminReadsAsync(withdrawnBooking.Id, Now.AddHours(6));
        var whileOpen = await AdminReadsAsync(openBooking.Id, Now.AddHours(6));

        Assert.Equal(DepositStates.HeldUntilWindowCloses, afterWithdrawal.Deposit.State);
        Assert.Null(afterWithdrawal.Deposit.Decision);
        Assert.Equal(DepositStates.UnderDispute, whileOpen.Deposit.State);
    }
}
