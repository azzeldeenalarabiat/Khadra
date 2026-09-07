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
using Khadra.Domain.Notifications.Repositories;
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
        public IDisputeTicketRepository Disputes { get; } = Substitute.For<IDisputeTicketRepository>();
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public INotifier Notifier { get; } = Substitute.For<INotifier>();
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public TestClock Clock { get; } = new(Build.Now);

        public Context()
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            Dealers.GetByIdAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>()).Returns(Build.ApprovedDealer());
            Bookings.ListDueForDecisionExpiryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
            Bookings.ListDueForPaymentExpiryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
            Bookings.ListDueForNoShowAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
            Bookings.ListDueForSettlementAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        }

        public SettleDueBookingsHandler Handler() =>
            new(Bookings, Disputes, Dealers, new DealerTeamNotifier(Notifier, Users), Clock, UnitOfWork,
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
        context.Bookings.ListDueForDecisionExpiryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([booking]);

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
        context.Bookings.ListDueForPaymentExpiryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([booking]);

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
        context.Bookings.ListDueForDecisionExpiryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([answered]);

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
        context.Bookings.ListDueForDecisionExpiryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([booking]);

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
        context.Bookings.ListDueForDecisionExpiryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([booking]);

        await context.Run();

        var raised = context.Notifier.ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name is nameof(INotifier.Raise) or nameof(INotifier.RaiseMany));
        Assert.True(raised > 0);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
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
        context.Bookings.ListDueForDecisionExpiryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([first, second]);

        var calls = 0;
        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            ++calls == 1 ? throw new ConcurrencyConflictException("Someone got there first.") : 1);

        var report = await context.Run();

        Assert.Equal(1, report.Value.ExpiredUnanswered);
        Assert.Equal(1, report.Value.Failed);
    }

    private static Booking Returned(Context context, out DateTimeOffset returnedAt)
    {
        var booking = Build.ConfirmedBooking();
        returnedAt = booking.Period.End;
        booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start);
        booking.RecordReturn(BookingParty.Dealer, Id.New(), returnedAt);
        booking.ClearDomainEvents();
        context.Bookings.ListDueForSettlementAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([booking]);
        return booking;
    }
}
