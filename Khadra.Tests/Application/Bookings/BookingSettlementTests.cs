using Khadra.Application.Bookings;
using Khadra.Application.Bookings.SettleBookings;
using Khadra.Application.Common;
using Khadra.Application.Notifications;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Disputes.Repositories;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications;
using Khadra.Domain.Notifications.Repositories;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
using Khadra.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Khadra.Tests.Application.Bookings;

/// <summary>
/// Pre-launch checklist item 4: the four rules nobody asked on a timer.
///
/// No car was ever stranded by the gap — the availability predicate reads the clock, so a lapsed hold
/// stops counting at its own deadline with nothing running. What was stranded is the truth: a booking
/// whose window closed still read "Requested" or "Approved" to both parties, and neither was told.
/// </summary>
public sealed class BookingSettlementTests
{
    private sealed class Context
    {
        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IPaymentRepository Payments { get; } = Substitute.For<IPaymentRepository>();
        public IDisputeTicketRepository Disputes { get; } = Substitute.For<IDisputeTicketRepository>();
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public INotifier Notifier { get; } = Substitute.For<INotifier>();
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public TestClock Clock { get; } = new(Build.Now);
        public Dealer Office { get; } = Build.ApprovedDealer();
        public List<Notification> Told { get; } = [];

        /// <summary>The bookings loaded since the tracker was last discarded, as the real unit of work tracks them.</summary>
        public List<Booking> Tracked { get; } = [];

        public BookingExpiryAnnouncer Announcer => new(new DealerTeamNotifier(Notifier, Users), Dealers);

        public Context()
        {
            // As the real unit of work does: a save hands over the tracked aggregates' events, which clears them.
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                Tracked.ForEach(booking => booking.ClearDomainEvents());
                return 1;
            });
            UnitOfWork.When(unit => unit.DiscardChanges()).Do(_ => Tracked.Clear());
            Dealers.GetByIdAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>()).Returns(Office);
            Notifier.When(n => n.Raise(Arg.Any<Notification>())).Do(call => Told.Add(call.Arg<Notification>()));
            Notifier.When(n => n.RaiseMany(Arg.Any<IEnumerable<Notification>>()))
                .Do(call => Told.AddRange(call.Arg<IEnumerable<Notification>>()));
            Bookings.ListIdsDueForDecisionExpiryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
            Bookings.ListIdsDueForPaymentExpiryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
            Bookings.ListIdsDueForNoShowAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
            Bookings.ListIdsDueForSettlementAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
            Bookings.ListIdsDueForDepositReleaseAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        }

        public void DueForDecisionExpiry(params Booking[] due)
        {
            // Set up before the list: NSubstitute cannot configure one call inside another's Returns.
            var ids = Loadable(due);
            Bookings.ListIdsDueForDecisionExpiryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ids);
        }

        public void DueForPaymentExpiry(params Booking[] due)
        {
            // Set up before the list: NSubstitute cannot configure one call inside another's Returns.
            var ids = Loadable(due);
            Bookings.ListIdsDueForPaymentExpiryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ids);
        }

        public void DueForNoShow(params Booking[] due)
        {
            // Set up before the list: NSubstitute cannot configure one call inside another's Returns.
            var ids = Loadable(due);
            Bookings.ListIdsDueForNoShowAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ids);
        }

        public void DueForSettlement(params Booking[] due)
        {
            // Set up before the list: NSubstitute cannot configure one call inside another's Returns.
            var ids = Loadable(due);
            Bookings.ListIdsDueForSettlementAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ids);
        }

        public void DueForDepositRelease(params Booking[] due)
        {
            // Set up before the list: NSubstitute cannot configure one call inside another's Returns.
            var ids = Loadable(due);
            Bookings.ListIdsDueForDepositReleaseAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ids);
        }

        /// <summary>The ids the sweep is handed, each loadable by id the way the settling seam loads it.</summary>
        private IReadOnlyList<Id> Loadable(Booking[] due)
        {
            foreach (var booking in due)
                Bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(_ => LoadAsync(booking));
            return [.. due.Select(booking => booking.Id)];
        }

        /// <summary>
        /// What <c>SettlingBookingRepository</c> does on every single-booking load (Wave 4, checklist 234): settle a
        /// lapse, and announce it on the same tracker. Its own proof against a real database is in
        /// <c>SettlingBookingRepositoryTests</c>.
        /// </summary>
        private async Task<Booking?> LoadAsync(Booking booking)
        {
            Tracked.Add(booking);
            if (BookingLapse.Settle(booking, Clock.UtcNow) is { } lapse)
                await Announcer.AnnounceAsync(booking, lapse, Clock.UtcNow, CancellationToken.None);
            return booking;
        }

        public SettleDueBookingsHandler Handler() =>
            new(Bookings, Payments, Disputes, Dealers, new DealerTeamNotifier(Notifier, Users), Clock, UnitOfWork,
                NullLogger<SettleDueBookingsHandler>.Instance);

        public Task<CSharpFunctionalExtensions.Result<SettlementReport, Error>> Run() =>
            Handler().Handle(new SettleDueBookingsCommand(), CancellationToken.None);
    }

    [Fact]
    public async Task A_request_nobody_answered_expires_and_costs_nobody_anything()
    {
        var context = new Context();
        var booking = Build.Booking();
        context.Clock.UtcNow = booking.DecisionDeadline;
        context.DueForDecisionExpiry(booking);

        var report = await context.Run();

        Assert.Equal(1, report.Value.ExpiredUnanswered);
        Assert.Same(BookingStatus.Expired, booking.Status);
        Assert.True(booking.Penalty!.IsNothingOwed);
        Assert.False(booking.OccupiesVehicle);
    }

    [Fact]
    public async Task An_approval_nobody_paid_expires()
    {
        var context = new Context();
        var booking = Build.ApprovedBooking();
        context.Clock.UtcNow = booking.PaymentDeadline!.Value;
        context.DueForPaymentExpiry(booking);

        var report = await context.Run();

        Assert.Equal(1, report.Value.ExpiredUnpaid);
        Assert.Same(BookingStatus.Expired, booking.Status);
    }

    /// <summary>
    /// The queries filter on status and deadline, but the aggregate is the rule. A booking answered
    /// between the query and the transition is refused here, and that is the correct outcome.
    /// </summary>
    [Fact]
    public async Task A_booking_that_moved_since_the_query_is_left_alone_rather_than_forced()
    {
        var context = new Context();
        var answered = Build.ApprovedBooking();
        context.DueForDecisionExpiry(answered);

        var report = await context.Run();

        Assert.Equal(0, report.Value.ExpiredUnanswered);
        Assert.Same(BookingStatus.Approved, answered.Status);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_returned_booking_settles_once_its_window_passes_with_no_dispute()
    {
        var context = new Context();
        var booking = Returned(context, out var returnedAt);
        context.Clock.UtcNow = returnedAt.Add(booking.Terms.PostReturnSettlementWindow);
        context.Disputes.HasLiveTicketAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(false);

        var report = await context.Run();

        Assert.Equal(1, report.Value.Completed);
        Assert.Same(BookingStatus.Completed, booking.Status);
        Assert.True(booking.CanBeReviewed);
    }

    /// <summary>A disputed booking stays open: the ticket decides when it closes, not the clock.</summary>
    [Fact]
    public async Task A_disputed_booking_does_not_settle_on_the_timer()
    {
        var context = new Context();
        var booking = Returned(context, out var returnedAt);
        context.Clock.UtcNow = returnedAt.Add(booking.Terms.PostReturnSettlementWindow);
        context.Disputes.HasLiveTicketAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(true);

        var report = await context.Run();

        Assert.Equal(0, report.Value.Completed);
        Assert.Same(BookingStatus.Returned, booking.Status);
    }

    /// <summary>
    /// A second pass over the same booking must do nothing. The service runs on a timer, and a job
    /// that double-applied its own work would rewrite history every minute.
    /// </summary>
    [Fact]
    public async Task Running_twice_settles_nothing_the_second_time()
    {
        var context = new Context();
        var booking = Build.Booking();
        context.Clock.UtcNow = booking.DecisionDeadline;
        context.DueForDecisionExpiry(booking);

        var first = await context.Run();
        var second = await context.Run();

        Assert.Equal(1, first.Value.ExpiredUnanswered);
        Assert.Equal(0, second.Value.ExpiredUnanswered);
    }

    /// <summary>
    /// Both parties are told. Until this shipped a customer learned their booking had expired only by
    /// noticing it had, which for a phone means never.
    /// </summary>
    [Fact]
    public async Task Both_parties_are_told_in_the_transaction_that_settles_the_booking()
    {
        var context = new Context();
        var booking = Build.Booking();
        context.Clock.UtcNow = booking.DecisionDeadline;
        context.DueForDecisionExpiry(booking);

        await context.Run();

        var raised = context.Notifier.ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name is nameof(INotifier.Raise) or nameof(INotifier.RaiseMany));
        Assert.True(raised > 0);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------- what the office is told (Fix & Polish Wave 3, C5)
    //
    // The sweep used to tell the office of a completion only, as BookingReturned by "A customer" — untrue in both
    // languages — and of nothing else. Each pass now tells it as Khadra, except a request it let lapse (C7).

    private static Notification OfficeRow(Context context) =>
        Assert.Single(context.Told, told => told.RecipientUserId == context.Office.OwnerUserId);

    [Fact]
    public async Task A_request_the_office_let_lapse_tells_only_its_customer()
    {
        var context = new Context();
        var booking = Build.Booking();
        context.Clock.UtcNow = booking.DecisionDeadline;
        context.DueForDecisionExpiry(booking);

        await context.Run();

        var only = Assert.Single(context.Told);
        Assert.Same(NotificationKind.YourBookingExpired, only.Kind);
        Assert.Equal(booking.CustomerId, only.RecipientUserId);
    }

    [Fact]
    public async Task An_approval_nobody_paid_for_tells_the_office_as_Khadra()
    {
        var context = new Context();
        var booking = Build.ApprovedBooking();
        context.Clock.UtcNow = booking.PaymentDeadline!.Value;
        context.DueForPaymentExpiry(booking);

        await context.Run();

        var office = OfficeRow(context);
        Assert.Same(NotificationKind.BookingExpiredUnpaid, office.Kind);
        Assert.True(office.IsFromPlatform);
        Assert.Equal(booking.Id, office.SubjectId);
        Assert.Contains(context.Told, told => told.Kind == NotificationKind.YourBookingExpired && told.RecipientUserId == booking.CustomerId);
    }

    [Fact]
    public async Task A_no_show_tells_the_office_as_Khadra()
    {
        var context = new Context();
        var booking = Paid(context, out _);
        DueForNoShow(context, booking);

        await context.Run();

        Assert.Same(NotificationKind.BookingMarkedNoShow, OfficeRow(context).Kind);
        Assert.True(OfficeRow(context).IsFromPlatform);
    }

    [Fact]
    public async Task A_completion_tells_the_office_it_completed_and_never_that_a_customer_returned_the_car()
    {
        var context = new Context();
        var booking = Returned(context, out var returnedAt);
        context.Clock.UtcNow = returnedAt.Add(booking.Terms.PostReturnSettlementWindow);

        await context.Run();

        var office = OfficeRow(context);
        Assert.Same(NotificationKind.BookingCompleted, office.Kind);
        Assert.True(office.IsFromPlatform);
        Assert.Null(office.ActorUserId);
        Assert.DoesNotContain(context.Told, told => told.Kind == NotificationKind.BookingReturned);
    }

    /// <summary>
    /// One failure must not cost the rest of the pass. A single transaction for the whole run would
    /// let one dealer acting at the wrong instant roll back everything the job had done.
    /// </summary>
    [Fact]
    public async Task A_conflict_on_one_booking_leaves_the_others_settled()
    {
        var context = new Context();
        var first = Build.Booking();
        var second = Build.Booking();
        context.Clock.UtcNow = first.DecisionDeadline > second.DecisionDeadline
            ? first.DecisionDeadline
            : second.DecisionDeadline;
        context.DueForDecisionExpiry(first, second);

        var calls = 0;
        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            ++calls == 1 ? throw new ConcurrencyConflictException("Someone got there first.") : 1);

        var report = await context.Run();

        Assert.Equal(1, report.Value.ExpiredUnanswered);
        Assert.Equal(1, report.Value.Failed);
    }

    /// <summary>
    /// ANY failure on one booking — not only a conflict — costs that booking alone, and the tracker is clean again for
    /// the next (Wave 4, checklist 233; the advisor's review). The pass used to end at the first unexpected exception.
    /// </summary>
    [Fact]
    public async Task Any_failure_on_one_booking_leaves_the_others_settled_on_a_clean_tracker()
    {
        var context = new Context();
        var first = Build.Booking();
        var second = Build.Booking();
        context.Clock.UtcNow = first.DecisionDeadline > second.DecisionDeadline ? first.DecisionDeadline : second.DecisionDeadline;
        context.DueForDecisionExpiry(first, second);
        var calls = 0;
        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            ++calls == 1 ? throw new InvalidOperationException("The database went away.") : 1);

        var report = await context.Run();

        Assert.Equal(1, report.Value.ExpiredUnanswered);
        Assert.Equal(1, report.Value.Failed);
        // A clean tracker before each booking, and again after the failure.
        context.UnitOfWork.Received(3).DiscardChanges();
    }

    // ---------------------------------------------------------------- money the timer owes (Phase 3, 2026-09-26)

    /// <summary>A booking paid against a real payment the repository answers for.</summary>
    private static Booking Paid(Context context, out Payment payment, bool inFull = false, decimal fee = 0m)
    {
        var (booking, paid) = Build.PaidBooking(inFull, fee);
        payment = paid;
        context.Payments.GetByIdAsync(paid.Id, Arg.Any<CancellationToken>()).Returns(paid);
        return booking;
    }

    private static void DueForNoShow(Context context, Booking booking)
    {
        context.Clock.UtcNow = booking.Period.Start.Add(booking.Terms.NoShowTimeout).AddMinutes(1);
        context.DueForNoShow(booking);
    }

    /// <summary>A paid no-show returns everything above the deposit, recorded in the save that marks it.</summary>
    [Fact]
    public async Task A_no_show_of_a_booking_paid_in_full_records_the_refund_above_the_deposit_in_the_same_save()
    {
        var context = new Context();
        var booking = Paid(context, out var payment, inFull: true, fee: 4.5m);
        DueForNoShow(context, booking);

        var report = await context.Run();

        Assert.Equal(1, report.Value.MarkedNoShow);
        Assert.Same(BookingStatus.NoShow, booking.Status);
        var refund = Assert.Single(payment.Refunds);
        Assert.Same(RefundReason.EndedBeforePickup, refund.Reason);
        Assert.Equal(Money.Jod(76.5m), refund.Amount);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_no_show_of_a_deposit_only_booking_records_no_refund_and_never_reads_the_payment()
    {
        var context = new Context();
        var booking = Paid(context, out var payment);
        DueForNoShow(context, booking);

        var report = await context.Run();

        Assert.Equal(1, report.Value.MarkedNoShow);
        Assert.Empty(payment.Refunds);
        await context.Payments.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    /// <summary>
    /// A booking paid in full whose payment cannot take the refund is left exactly as it was — never
    /// marked a no-show with the money it owes unrecorded — and the rest of the pass carries on.
    /// </summary>
    [Fact]
    public async Task A_no_show_whose_payment_is_missing_is_left_unchanged_and_the_pass_carries_on()
    {
        var context = new Context();
        var (orphaned, _) = Build.PaidBooking(inFull: true);
        var fine = Paid(context, out var payment, inFull: true);
        context.Clock.UtcNow = fine.Period.Start.Add(fine.Terms.NoShowTimeout).AddMinutes(1);
        context.DueForNoShow(orphaned, fine);

        var report = await context.Run();

        Assert.Same(BookingStatus.Confirmed, orphaned.Status);
        Assert.Same(BookingStatus.NoShow, fine.Status);
        Assert.Single(payment.Refunds);
        Assert.Equal(1, report.Value.MarkedNoShow);
        Assert.Equal(1, report.Value.Failed);
    }

    /// <summary>A gallery cancelled a paid booking after the free window: the penalty is against the office.</summary>
    private static Booking CancelledByTheGallery(Context context, out Payment payment, bool inFull = false)
    {
        var booking = Paid(context, out payment, inFull);
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "The car failed its inspection.", Build.Now.AddHours(3)).IsSuccess);
        booking.ClearDomainEvents();
        context.DueForDepositRelease(booking);
        return booking;
    }

    /// <summary>
    /// Owner, 2026-09-26, decision 3(a): once the dispute window closes CLEANLY — no dispute, no
    /// penalty against the customer — the held deposit goes back to the customer, decided by the
    /// backend. A penalty against the OFFICE does not hold the customer's deposit.
    /// </summary>
    [Fact]
    public async Task A_deposit_nobody_claimed_goes_back_when_the_window_closes()
    {
        var context = new Context();
        var booking = CancelledByTheGallery(context, out var payment);
        context.Clock.UtcNow = booking.FinishedAt!.Value.Add(booking.Terms.PostReturnSettlementWindow);

        var report = await context.Run();

        Assert.Equal(1, report.Value.DepositsReleased);
        var refund = Assert.Single(payment.Refunds);
        Assert.Same(RefundReason.DisputeWindowClosed, refund.Reason);
        Assert.Equal(Money.Jod(booking.Pricing.DepositAmount.Amount), refund.Amount);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        // A second pass records nothing more.
        var again = await context.Run();
        Assert.Equal(0, again.Value.DepositsReleased);
        Assert.Single(payment.Refunds);
    }

    [Fact]
    public async Task A_deposit_is_not_released_a_moment_before_its_own_window_closes()
    {
        var context = new Context();
        var booking = CancelledByTheGallery(context, out var payment);
        context.Clock.UtcNow = booking.FinishedAt!.Value.Add(booking.Terms.PostReturnSettlementWindow).AddSeconds(-1);

        var report = await context.Run();

        Assert.Equal(0, report.Value.DepositsReleased);
        Assert.Empty(payment.Refunds);
    }

    /// <summary>A ticket opened between the query and the release claims the deposit: time alone never releases it.</summary>
    [Fact]
    public async Task A_deposit_with_a_claim_on_it_is_not_released_however_much_time_has_passed()
    {
        var context = new Context();
        var booking = CancelledByTheGallery(context, out var payment);
        context.Clock.UtcNow = booking.FinishedAt!.Value.AddDays(30);
        context.Disputes.HasClaimOnDepositAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(true);

        var report = await context.Run();

        Assert.Equal(0, report.Value.DepositsReleased);
        Assert.Empty(payment.Refunds);
    }

    /// <summary>A penalty assessed against the CUSTOMER is a valid hold: the deposit stays, whatever the clock says.</summary>
    [Fact]
    public async Task A_deposit_held_for_a_penalty_against_the_customer_is_not_released()
    {
        var context = new Context();
        var booking = Paid(context, out var payment);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, "Changed plans.", Build.Now.AddHours(3)).IsSuccess);
        context.DueForDepositRelease(booking);
        context.Clock.UtcNow = booking.FinishedAt!.Value.AddDays(30);

        var report = await context.Run();

        Assert.Equal(0, report.Value.DepositsReleased);
        Assert.Empty(payment.Refunds);
    }

    /// <summary>After the money above the deposit went back, the release returns exactly the deposit: never more than was taken.</summary>
    [Fact]
    public async Task A_booking_paid_in_full_gets_its_deposit_back_after_the_money_above_it()
    {
        var context = new Context();
        var booking = CancelledByTheGallery(context, out var payment, inFull: true);
        Khadra.Application.Payments.BookingEndingRefunds.Record(booking, payment, Build.Now.AddHours(3));
        context.Clock.UtcNow = booking.FinishedAt!.Value.Add(booking.Terms.PostReturnSettlementWindow);

        var report = await context.Run();

        Assert.Equal(1, report.Value.DepositsReleased);
        Assert.Equal(2, payment.Refunds.Count);
        Assert.Equal(payment.AmountCaptured, payment.RefundedOrOwed);
        Assert.Contains(payment.Refunds, refund => refund.Reason == RefundReason.DisputeWindowClosed && refund.Amount == Money.Jod(18m));
    }

    [Fact]
    public async Task A_release_whose_payment_is_missing_is_deferred_not_thrown()
    {
        var context = new Context();
        var (booking, _) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", Build.Now.AddHours(3)).IsSuccess);
        context.DueForDepositRelease(booking);
        context.Clock.UtcNow = booking.FinishedAt!.Value.AddDays(3);

        var report = await context.Run();

        Assert.Equal(0, report.Value.DepositsReleased);
        Assert.Equal(1, report.Value.Failed);
    }

    private static Booking Returned(Context context, out DateTimeOffset returnedAt)
    {
        var booking = Build.ConfirmedBooking();
        returnedAt = booking.Period.End;
        booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start);
        booking.RecordReturn(BookingParty.Dealer, Id.New(), returnedAt);
        booking.ClearDomainEvents();
        context.DueForSettlement(booking);
        return booking;
    }
}
