using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Domain.Disputes;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Phase 3 (owner, 2026-09-26) against the real EF model: the queries that find a deposit whose window
/// closed cleanly, the safety net under the ending refunds, and the refunds every screen reads.
/// </summary>
/// <remarks>
/// The candidate query reads the booking's PENALTY out of its JSON document and joins two other
/// contexts by id; both are the kind of translation that compiles and then quietly means something
/// else, so every exclusion is pinned against a row that should, and one that should not, be found.
/// </remarks>
public sealed class EndingRefundPersistenceTests : IDisposable
{
    private static readonly DateTimeOffset Now = Build.Now;

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public EndingRefundPersistenceTests()
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

    private async Task SaveAsync(IEnumerable<Booking> bookings, IEnumerable<Payment> payments, IEnumerable<DisputeTicket>? tickets = null)
    {
        await using var context = NewContext();
        context.Bookings.AddRange(bookings);
        context.Payments.AddRange(payments);
        if (tickets is not null)
            context.DisputeTickets.AddRange(tickets);
        await context.SaveChangesAsync();
    }

    private static (Booking Booking, Payment Payment) CancelledBy(BookingParty party, bool inFull = false, BookingTerms? terms = null)
    {
        var (booking, payment) = Build.PaidBooking(inFull, terms: terms);
        var actor = party == BookingParty.Customer ? booking.CustomerId : Id.New();
        Assert.True(booking.Cancel(party, actor, "Ended.", AfterFreeWindow(booking)).IsSuccess);
        return (booking, payment);
    }

    private static DisputeTicket TicketOn(Booking booking) =>
        DisputeTicket.Open(booking.Id, booking.CustomerId, BookingParty.Customer, "The car never came.", TimeSpan.FromHours(48), AfterFreeWindow(booking)).Value;

    [Fact]
    public async Task The_release_candidates_are_the_paid_endings_whose_deposit_nothing_has_decided()
    {
        // Found: the gallery cancelled (the penalty is on the office).
        var (gallery, galleryPayment) = CancelledBy(BookingParty.Dealer);
        // Found: a ticket was opened and WITHDRAWN — as if no dispute was raised.
        var (withdrawn, withdrawnPayment) = CancelledBy(BookingParty.Dealer);
        var withdrawnTicket = TicketOn(withdrawn);
        Assert.True(withdrawnTicket.Withdraw(withdrawn.CustomerId, AfterFreeWindow(withdrawn).AddHours(1)).IsSuccess);
        // Found: a late customer cancellation under terms that make it cost nothing.
        var (lenient, lenientPayment) = CancelledBy(BookingParty.Customer, terms: Build.Terms(customerPenaltyPercent: 0m));

        // Left out: a penalty against the customer.
        var (customer, customerPayment) = CancelledBy(BookingParty.Customer);
        // Left out: a ticket that is still open.
        var (disputed, disputedPayment) = CancelledBy(BookingParty.Dealer);
        var openTicket = TicketOn(disputed);
        // Left out: a ticket that was RESOLVED — the resolution decided the deposit.
        var (resolved, resolvedPayment) = CancelledBy(BookingParty.Dealer);
        var resolvedTicket = TicketOn(resolved);
        Assert.True(resolvedTicket.Resolve(DisputeResolution.Create(
            DepositDisposition.RefundEverything(Money.Jod(18m)).Value, null, null, "Refunded.", Id.New(), Now.AddDays(1)).Value).IsSuccess);
        // Left out: the deposit was already released.
        var (released, releasedPayment) = CancelledBy(BookingParty.Dealer);
        Assert.NotNull(releasedPayment.RefundHeldDeposit(Money.Jod(18m), Now.AddDays(3)).Value);
        // Left out: the whole payment already went back (a free cancellation, an administrator's).
        var (free, freePayment) = Build.PaidBooking();
        Assert.True(free.Cancel(BookingParty.Customer, free.CustomerId, null, Now).IsSuccess);
        freePayment.RefundForFreeCancellation(Now);
        var (byAdmin, adminPayment) = CancelledBy(BookingParty.Admin);
        adminPayment.RefundWholePayment(RefundReason.PlatformCancellation, AfterFreeWindow(byAdmin));
        // Left out: never paid.
        var unpaid = Build.ApprovedBooking();
        Assert.True(unpaid.Cancel(BookingParty.Dealer, Id.New(), "No car.", Now).IsSuccess);
        // Left out: still running.
        var (running, runningPayment) = Build.PaidBooking();

        await SaveAsync(
            [gallery, withdrawn, lenient, customer, disputed, resolved, released, free, byAdmin, unpaid, running],
            [galleryPayment, withdrawnPayment, lenientPayment, customerPayment, disputedPayment, resolvedPayment, releasedPayment, freePayment, adminPayment, runningPayment],
            [withdrawnTicket, openTicket, resolvedTicket]);

        await using var read = NewContext();
        var found = await new BookingRepository(read).ListDueForDepositReleaseAsync(Now.AddDays(30));

        Assert.Equal(
            new[] { gallery.Id, withdrawn.Id, lenient.Id }.OrderBy(id => id.Value),
            found.Select(booking => booking.Id).OrderBy(id => id.Value));
    }

    [Fact]
    public async Task A_booking_is_not_a_candidate_before_it_has_ended()
    {
        var (booking, payment) = CancelledBy(BookingParty.Dealer);
        await SaveAsync([booking], [payment]);

        await using var read = NewContext();
        var found = await new BookingRepository(read).ListDueForDepositReleaseAsync(booking.FinishedAt!.Value.AddTicks(-1));

        Assert.Empty(found);
    }

    [Fact]
    public async Task Only_a_withdrawn_ticket_leaves_the_deposit_unclaimed()
    {
        var (booking, payment) = CancelledBy(BookingParty.Dealer);
        var ticket = TicketOn(booking);
        await SaveAsync([booking], [payment], [ticket]);

        await using (var read = NewContext())
            Assert.True(await new DisputeTicketRepository(read).HasClaimOnDepositAsync(booking.Id));

        await using (var write = NewContext())
        {
            var stored = await write.DisputeTickets.SingleAsync(t => t.Id == ticket.Id);
            Assert.True(stored.Withdraw(booking.CustomerId, Now.AddDays(1)).IsSuccess);
            await write.SaveChangesAsync();
        }

        await using (var read = NewContext())
            Assert.False(await new DisputeTicketRepository(read).HasClaimOnDepositAsync(booking.Id));
    }

    [Fact]
    public async Task The_safety_net_finds_a_payment_in_full_whose_ending_recorded_no_refund_and_nothing_else()
    {
        var (missing, missingPayment) = CancelledBy(BookingParty.Customer, inFull: true);
        var (recorded, recordedPayment) = CancelledBy(BookingParty.Customer, inFull: true);
        Assert.NotNull(Khadra.Application.Payments.BookingEndingRefunds.Record(recorded, recordedPayment, AfterFreeWindow(recorded)));
        var (depositOnly, depositOnlyPayment) = CancelledBy(BookingParty.Customer);
        var (running, runningPayment) = Build.PaidBooking(inFull: true);
        // An administrator's cancellation returns the whole payment, a deposit's included.
        var (byAdmin, byAdminPayment) = CancelledBy(BookingParty.Admin);
        var (byAdminRefunded, byAdminRefundedPayment) = CancelledBy(BookingParty.Admin);
        byAdminRefundedPayment.RefundWholePayment(RefundReason.PlatformCancellation, AfterFreeWindow(byAdminRefunded));
        await SaveAsync(
            [missing, recorded, depositOnly, running, byAdmin, byAdminRefunded],
            [missingPayment, recordedPayment, depositOnlyPayment, runningPayment, byAdminPayment, byAdminRefundedPayment]);

        await using var read = NewContext();
        var found = await new PaymentRepository(read).ListEndedWithoutEndingRefundAsync();

        Assert.Equal(
            new[] { missingPayment.Id, byAdminPayment.Id }.OrderBy(id => id.Value),
            found.Select(payment => payment.Id).OrderBy(id => id.Value));
    }

    /// <summary>
    /// Every refund against the booking's payments, oldest first, with its reason, amount and status;
    /// and the deposit's own refund still where installed apps read it.
    /// </summary>
    [Fact]
    public async Task A_booking_reads_every_refund_oldest_first_and_its_deposit_refund_where_installed_apps_look()
    {
        var (booking, payment) = CancelledBy(BookingParty.Dealer, inFull: true);
        var above = Khadra.Application.Payments.BookingEndingRefunds.Record(booking, payment, AfterFreeWindow(booking))!;
        above.MarkSent("rf_above", AfterFreeWindow(booking).AddMinutes(1));
        above.MarkSettled(AfterFreeWindow(booking).AddMinutes(5));
        var deposit = payment.RefundHeldDeposit(Money.Jod(18m), Now.AddDays(3)).Value!;
        // A capture that landed after the booking was paid is this booking's money too.
        var late = Payment.Open(booking.Id, booking.CustomerId, Money.Jod(18m), "TestProvider", Now.AddMinutes(30), Now);
        late.AttachProviderSession("sess_late", "https://provider.test/sess_late");
        Assert.True(late.Orphan(Money.Jod(18m), Now.AddDays(4), "AlreadyPaidByAnotherAttempt", Now.AddDays(4)).IsSuccess);
        await SaveAsync([booking], [payment, late]);

        await using var read = NewContext();
        var context = await new BookingReader(read).ContextAsync(booking.Id);

        Assert.NotNull(context.Refunds);
        Assert.Collection(
            context.Refunds,
            first =>
            {
                Assert.Equal("EndedBeforePickup", first.Reason);
                Assert.Equal("Settled", first.Status);
                Assert.Equal(72m, first.Amount.Amount);
                Assert.Equal("JOD", first.Amount.Currency);
                Assert.Equal(payment.Id.Value, first.PaymentId);
                Assert.Equal(AfterFreeWindow(booking).AddMinutes(5), first.SettledAt);
            },
            second =>
            {
                Assert.Equal("DisputeWindowClosed", second.Reason);
                Assert.Equal("Requested", second.Status);
                Assert.Equal(18m, second.Amount.Amount);
                Assert.Equal(deposit.Id.Value, second.RefundId);
            },
            third =>
            {
                Assert.Equal("OrphanedCapture", third.Reason);
                Assert.Equal(late.Id.Value, third.PaymentId);
            });
        Assert.NotNull(context.DepositRefund);
        Assert.Equal("Requested", context.DepositRefund.Status);
        Assert.Equal(18m, context.DepositRefund.Amount.Amount);
    }

    [Fact]
    public async Task The_new_reasons_and_the_unmatched_outcome_survive_the_database()
    {
        var (booking, payment) = CancelledBy(BookingParty.Admin, inFull: true);
        payment.RefundWholePayment(RefundReason.PlatformCancellation, AfterFreeWindow(booking));
        var receipt = ProviderEventReceipt.Record("TestProvider", "evt_unmatched", "sess_x", "RefundSettled", payment.Id,
            ProviderEventOutcome.Unmatched, Money.Jod(1m), Now);
        await SaveAsync([booking], [payment]);
        await using (var write = NewContext())
        {
            write.ProviderEventReceipts.Add(receipt);
            await write.SaveChangesAsync();
        }

        await using var read = NewContext();
        var stored = await new PaymentRepository(read).GetByIdAsync(payment.Id);
        Assert.Same(RefundReason.PlatformCancellation, Assert.Single(stored!.Refunds).Reason);
        Assert.Same(ProviderEventOutcome.Unmatched, (await read.ProviderEventReceipts.SingleAsync()).Outcome);
    }
}
