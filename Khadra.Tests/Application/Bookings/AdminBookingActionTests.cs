using Khadra.Application.Auditing;
using Khadra.Application.Bookings.AdminBookings;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Bookings;

// The three interventions an administrator can make in a booking.
//
// The one that matters most is what an admin cancellation does NOT do: attributing it to the customer
// or the dealer would have the aggregate assess a penalty against that party — a full deposit, for a
// customer past the free window — which is the judgement spec 3.3 reserves for a dispute ticket where
// both sides have been heard. These pin that, and that neither expiry can be used to end a booking
// before its own frozen window has run out.
public sealed class AdminBookingActionTests
{
    private static readonly Id AdminId = Id.New();

    private sealed class Context
    {
        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IBookingReader Reader { get; } = Substitute.For<IBookingReader>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IAuditTrail AuditTrail { get; } = Substitute.For<IAuditTrail>();
        public ICurrentActor Actor { get; } = Substitute.For<ICurrentActor>();
        public TestClock Clock { get; } = new(Build.Now);
        public List<AuditEntry> Recorded { get; } = [];

        public Context()
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            AuditTrail.When(trail => trail.Record(Arg.Any<AuditEntry>()))
                .Do(call => Recorded.Add(call.Arg<AuditEntry>()));
            Actor.UserId.Returns(AdminId);
            Actor.Role.Returns(UserRole.Admin);
            Actor.Name.Returns("Rania Haddad");
            Actor.CorrelationId.Returns("test-correlation");
            Reader.ContextAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>())
                .Returns(new BookingContext(null, "Petra Rentals", "Sami Khoury", null));
        }

        public Booking Given(Booking booking)
        {
            Bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
            return booking;
        }

        public void At(DateTimeOffset now) => Clock.UtcNow = now;

        public AdminBookingCommandHandlers Handlers() => new(
            Bookings,
            Reader,
            new AdminActionRecorder(AuditTrail, Actor, Clock),
            Actor,
            UnitOfWork,
            Clock);
    }

    [Fact]
    public async Task An_admin_cancellation_assesses_no_penalty_against_either_party()
    {
        var context = new Context();
        // Approved and past its free-cancellation window: cancelled BY the customer here, the
        // aggregate would assess the whole deposit against them.
        var booking = context.Given(Build.ConfirmedBooking());
        context.At(Build.Now.AddDays(3));

        var result = await context.Handlers().Handle(
            new CancelBookingAsAdminCommand(booking.Id, "The dealership was suspended mid-rental."),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(BookingStatus.Cancelled, booking.Status);
        Assert.Same(BookingParty.Admin, booking.CancelledBy);
        Assert.NotNull(booking.Penalty);
        Assert.True(booking.Penalty!.IsNothingOwed);
    }

    [Fact]
    public async Task Cancelling_by_the_customer_would_have_assessed_the_deposit()
    {
        // The counterweight to the test above: this is what the admin path deliberately avoids, and
        // without it "no penalty" could pass for a booking that was never going to be penalised.
        var booking = Build.ConfirmedBooking();

        booking.Cancel(BookingParty.Customer, Id.New(), "Changed my mind.", Build.Now.AddDays(3));

        Assert.False(booking.Penalty!.IsNothingOwed);
        Assert.Same(BookingParty.Customer, booking.Penalty.AttributedTo);
    }

    [Fact]
    public async Task The_cancellation_reason_and_the_status_change_reach_the_audit_log()
    {
        var context = new Context();
        var booking = context.Given(Build.ConfirmedBooking());
        context.At(Build.Now.AddDays(3));

        await context.Handlers().Handle(
            new CancelBookingAsAdminCommand(booking.Id, "The dealership was suspended mid-rental."),
            CancellationToken.None);

        var entry = Assert.Single(context.Recorded);
        Assert.Same(AuditAction.BookingCancelledByAdmin, entry.Action);
        Assert.Same(AuditEntityType.Booking, entry.EntityType);
        Assert.Equal(booking.Reference.Value, entry.SubjectLabel);
        Assert.Equal("Confirmed", entry.PreviousValue);
        Assert.Equal("Cancelled", entry.NewValue);
        Assert.Equal("The dealership was suspended mid-rental.", entry.Reason);
        Assert.Equal(AdminId, entry.ActorUserId);
    }

    [Fact]
    public async Task An_expiry_is_refused_while_the_bookings_own_window_still_has_time_in_it()
    {
        var context = new Context();
        // Approved and not yet paid: still inside its payment window at Build.Now.
        var booking = context.Given(Build.ApprovedBooking());

        var result = await context.Handlers().Handle(
            new ExpireBookingAsAdminCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Same(BookingStatus.Approved, booking.Status);
        Assert.Empty(context.Recorded);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unpaid_booking_expires_once_its_payment_window_has_elapsed()
    {
        var context = new Context();
        var booking = context.Given(Build.ApprovedBooking());
        context.At(booking.PaymentDeadline!.Value.AddMinutes(1));

        var result = await context.Handlers().Handle(
            new ExpireBookingAsAdminCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(BookingStatus.Expired, booking.Status);
        Assert.True(booking.Penalty!.IsNothingOwed);
    }

    /// <summary>
    /// The handler picks the expiry from the booking's own state, never from the caller, so the same
    /// command has to reach the other clock too: a request no dealer ever answered.
    /// </summary>
    [Fact]
    public async Task An_unanswered_request_expires_once_the_dealers_answer_window_has_elapsed()
    {
        var context = new Context();
        var booking = context.Given(Build.Booking());
        context.At(booking.DecisionDeadline.AddMinutes(1));

        var result = await context.Handlers().Handle(
            new ExpireBookingAsAdminCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(BookingStatus.Expired, booking.Status);
        Assert.True(booking.Penalty!.IsNothingOwed);
    }

    [Fact]
    public async Task An_admin_triggered_expiry_names_the_admin_in_the_history_rather_than_the_timer()
    {
        var context = new Context();
        var booking = context.Given(Build.ApprovedBooking());
        context.At(booking.PaymentDeadline!.Value.AddMinutes(1));

        await context.Handlers().Handle(new ExpireBookingAsAdminCommand(booking.Id), CancellationToken.None);

        var last = booking.StatusHistory.Last();
        Assert.Same(BookingStatus.Expired, last.To);
        Assert.Same(BookingParty.Admin, last.ActorParty);
        Assert.Equal(AdminId, last.ActorUserId);
    }

    [Fact]
    public async Task A_no_show_is_refused_before_the_no_show_window_has_run_out()
    {
        var context = new Context();
        var booking = context.Given(Build.ConfirmedBooking());

        var result = await context.Handlers().Handle(
            new MarkBookingNoShowAsAdminCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Same(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public async Task A_self_pickup_no_show_assesses_the_deposit_the_booking_froze()
    {
        var context = new Context();
        var booking = context.Given(Build.ConfirmedBooking(pickupMethod: PickupMethod.SelfPickup));
        context.At(booking.Period.Start.Add(booking.Terms.NoShowTimeout).AddMinutes(1));

        var result = await context.Handlers().Handle(
            new MarkBookingNoShowAsAdminCommand(booking.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(BookingStatus.NoShow, booking.Status);
        Assert.False(booking.Penalty!.IsNothingOwed);
        Assert.Same(BookingParty.Customer, booking.Penalty.AttributedTo);
        Assert.Equal(booking.Pricing.DepositAmount.Amount, booking.Penalty.MaxAmount.Amount);
    }

    [Fact]
    public async Task A_booking_that_is_not_there_is_not_found_rather_than_a_crash()
    {
        var context = new Context();

        var result = await context.Handlers().Handle(
            new CancelBookingAsAdminCommand(Id.New(), "Anything."),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(BookingErrors.NotFound.Code, result.Error.Code);
    }
}
