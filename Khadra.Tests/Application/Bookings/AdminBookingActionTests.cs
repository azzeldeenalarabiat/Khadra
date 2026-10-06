using Khadra.Application.Auditing;
using Khadra.Application.Bookings.AdminBookings;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Notifications;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications;
using Khadra.Domain.Notifications.Repositories;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
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
        public IPaymentRepository Payments { get; } = Substitute.For<IPaymentRepository>();
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
                .Returns(new BookingContext(null, "Petra Rentals", false, null, "Sami Khoury", false, null, null));
        }

        public Booking Given(Booking booking)
        {
            Bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
            return booking;
        }

        /// <summary>A booking paid against a real payment the repository answers for.</summary>
        public Booking GivenPaid(out Payment payment, bool inFull = false, decimal fee = 0m, bool feeRefundable = true)
        {
            var (booking, paid) = Build.PaidBooking(inFull, fee, feeRefundable);
            payment = paid;
            Payments.GetByIdAsync(paid.Id, Arg.Any<CancellationToken>()).Returns(paid);
            return Given(booking);
        }

        public void At(DateTimeOffset now) => Clock.UtcNow = now;

        public INotifier Notifier { get; } = Substitute.For<INotifier>();

        public List<Notification> Told { get; } = [];

        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();

        /// <summary>The office the booking belongs to: its owner, an active employee, and one deactivated.</summary>
        public Dealer Office { get; } = Build.ApprovedDealer();

        public Id ActiveEmployee { get; } = Id.New();

        public Context Staffed()
        {
            Office.HireEmployee(ActiveEmployee, canViewReports: false, Build.Now);
            var gone = Office.HireEmployee(Id.New(), canViewReports: false, Build.Now).Value;
            Office.DeactivateEmployee(gone.Id, Build.Now);
            Dealers.GetByIdAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>()).Returns(Office);
            return this;
        }

        /// <summary>What the office was told, as against the customer's own row.</summary>
        public List<Notification> ToldOffice(NotificationKind kind) => Told.Where(told => told.Kind == kind).ToList();

        public AdminBookingCommandHandlers Handlers()
        {
            Notifier.When(n => n.Raise(Arg.Any<Notification>())).Do(call => Told.Add(call.Arg<Notification>()));
            Notifier.When(n => n.RaiseMany(Arg.Any<IEnumerable<Notification>>()))
                .Do(call => Told.AddRange(call.Arg<IEnumerable<Notification>>()));
            return new(
            Bookings,
            Payments,
            Reader,
            new AdminActionRecorder(AuditTrail, Actor, Clock),
            new DealerTeamNotifier(Notifier, Substitute.For<IUserRepository>()),
            Dealers,
            Actor,
            UnitOfWork,
            Clock);
        }
    }

    [Fact]
    public async Task An_admin_cancellation_assesses_no_penalty_against_either_party()
    {
        var context = new Context();
        // Approved and past its free-cancellation window: cancelled BY the customer here, the
        // aggregate would assess the whole deposit against them.
        var booking = context.GivenPaid(out _);
        context.At(Build.Now.AddDays(3));

        var result = await context.Handlers().Handle(
            new CancelBookingAsAdminCommand(booking.Id, "The dealership was suspended mid-rental."),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(BookingStatus.Cancelled, booking.Status);
        Assert.Same(BookingParty.Admin, booking.CancelledBy);
        Assert.NotNull(booking.Penalty);
        Assert.True(booking.Penalty!.IsNothingOwed);

        // And the customer is told, named by the gallery, in the same save.
        var told = Assert.Single(context.Told);
        Assert.Same(NotificationKind.YourBookingCancelled, told.Kind);
        Assert.Equal(booking.CustomerId, told.RecipientUserId);
        Assert.Equal("Petra Rentals", told.ActorName);
        Assert.Equal(booking.Id, told.SubjectId);
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
        var booking = context.GivenPaid(out _);
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

    // ---------------------------------------------------------------- the office is told (Fix & Polish Wave 3, C5)

    /// <summary>
    /// What an administrator did to an office's booking reaches its whole team, active staff only, as Khadra's: no
    /// actor id, the platform's name, in the console and by email. The administrator's own name never travels.
    /// </summary>
    [Fact]
    public async Task An_admin_cancellation_tells_the_whole_office_as_Khadra()
    {
        var context = new Context().Staffed();
        var booking = context.Given(Build.ApprovedBooking());

        await context.Handlers().Handle(
            new CancelBookingAsAdminCommand(booking.Id, "The dealership was suspended mid-rental."),
            CancellationToken.None);

        var office = context.ToldOffice(NotificationKind.BookingCancelledByAdmin);
        Assert.Equal(
            new[] { context.Office.OwnerUserId, context.ActiveEmployee }.OrderBy(id => id.Value),
            office.Select(told => told.RecipientUserId).OrderBy(id => id.Value));
        Assert.All(office, told =>
        {
            Assert.True(told.IsFromPlatform);
            Assert.Null(told.ActorUserId);
            Assert.Equal(booking.Id, told.SubjectId);
            Assert.Equal(booking.Reference.Value, told.SubjectReference);
        });
        Assert.Equal([NotificationChannel.Email], NotificationKind.BookingCancelledByAdmin.DeliveredOn());
        // And the customer as before, named by the gallery.
        Assert.Single(context.ToldOffice(NotificationKind.YourBookingCancelled));
    }

    [Fact]
    public async Task An_admin_expiry_tells_the_office_of_an_approval_nobody_paid_for()
    {
        var context = new Context().Staffed();
        var booking = context.Given(Build.ApprovedBooking());
        context.At(booking.PaymentDeadline!.Value.AddMinutes(1));

        await context.Handlers().Handle(new ExpireBookingAsAdminCommand(booking.Id), CancellationToken.None);

        Assert.Equal(2, context.ToldOffice(NotificationKind.BookingExpiredUnpaid).Count);
    }

    /// <summary>A request the office let lapse tells the office nothing (Wave 3, C7): only its customer hears.</summary>
    [Fact]
    public async Task An_admin_expiry_of_an_unanswered_request_tells_the_office_nothing()
    {
        var context = new Context().Staffed();
        var booking = context.Given(Build.Booking());
        context.At(booking.DecisionDeadline.AddMinutes(1));

        await context.Handlers().Handle(new ExpireBookingAsAdminCommand(booking.Id), CancellationToken.None);

        var only = Assert.Single(context.Told);
        Assert.Same(NotificationKind.YourBookingExpired, only.Kind);
        Assert.Equal(booking.CustomerId, only.RecipientUserId);
    }

    [Fact]
    public async Task An_admin_no_show_tells_the_office()
    {
        var context = new Context().Staffed();
        var booking = context.Given(Build.ConfirmedBooking(pickupMethod: PickupMethod.SelfPickup));
        context.At(booking.Period.Start.Add(booking.Terms.NoShowTimeout).AddMinutes(1));

        await context.Handlers().Handle(new MarkBookingNoShowAsAdminCommand(booking.Id), CancellationToken.None);

        Assert.Equal(2, context.ToldOffice(NotificationKind.BookingMarkedNoShow).Count);
    }

    /// <summary>A refused action tells nobody anything.</summary>
    [Fact]
    public async Task A_refused_action_tells_the_office_nothing()
    {
        var context = new Context().Staffed();
        var booking = context.Given(Build.ApprovedBooking());

        await context.Handlers().Handle(new ExpireBookingAsAdminCommand(booking.Id), CancellationToken.None);

        Assert.Empty(context.Told);
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

    // ---------------------------------------------------------------- the refund an ending owes (Phase 3, 2026-09-26)

    /// <summary>
    /// Owner, 2026-09-26: an administrator's cancellation of a paid booking before pickup, with no
    /// penalty on the customer, returns the WHOLE payment — the deposit included — in the same save.
    /// </summary>
    [Fact]
    public async Task An_admin_cancellation_of_a_paid_booking_refunds_the_whole_deposit_in_the_same_save()
    {
        var context = new Context();
        var booking = context.GivenPaid(out var payment);
        context.At(Build.Now.AddDays(3));

        var result = await context.Handlers().Handle(
            new CancelBookingAsAdminCommand(booking.Id, "The office lost its licence."),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.True(booking.ReturnsWholePayment);
        var refund = Assert.Single(payment.Refunds);
        Assert.Same(RefundReason.PlatformCancellation, refund.Reason);
        Assert.Same(RefundStatus.Requested, refund.Status);
        Assert.Equal(payment.AmountCaptured, refund.Amount);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>90 paid for the booking and a 4.5 fee on top: the fee goes back only if the payment froze it refundable.</summary>
    [Theory]
    [InlineData(true, 94.5)]
    [InlineData(false, 90)]
    public async Task An_admin_cancellation_of_a_booking_paid_in_full_returns_the_fee_only_when_it_was_refundable(
        bool feeRefundable, decimal expected)
    {
        var context = new Context();
        var booking = context.GivenPaid(out var payment, inFull: true, fee: 4.5m, feeRefundable: feeRefundable);
        context.At(Build.Now.AddDays(3));

        await context.Handlers().Handle(new CancelBookingAsAdminCommand(booking.Id, "Fraud check."), CancellationToken.None);

        var refund = Assert.Single(payment.Refunds);
        Assert.Same(RefundReason.PlatformCancellation, refund.Reason);
        Assert.Equal(Money.Jod(expected), refund.Amount);
    }

    /// <summary>
    /// A no-show keeps the deposit held — the penalty is assessed against it — and returns everything
    /// the customer paid above it (Phase 3), with a refundable fee.
    /// </summary>
    [Fact]
    public async Task An_admin_no_show_of_a_booking_paid_in_full_returns_everything_above_the_deposit()
    {
        var context = new Context();
        var booking = context.GivenPaid(out var payment, inFull: true, fee: 4.5m);
        context.At(booking.Period.Start.Add(booking.Terms.NoShowTimeout).AddMinutes(1));

        var result = await context.Handlers().Handle(new MarkBookingNoShowAsAdminCommand(booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(BookingStatus.NoShow, booking.Status);
        var refund = Assert.Single(payment.Refunds);
        Assert.Same(RefundReason.EndedBeforePickup, refund.Reason);
        // 90 paid for the booking, 18 of it the deposit, plus the 4.5 fee that goes back with the rest.
        Assert.Equal(Money.Jod(76.5m), refund.Amount);
    }

    [Fact]
    public async Task An_admin_no_show_of_a_deposit_only_booking_records_no_refund_and_never_asks_for_the_payment()
    {
        var context = new Context();
        var booking = context.GivenPaid(out var payment);
        context.At(booking.Period.Start.Add(booking.Terms.NoShowTimeout).AddMinutes(1));

        await context.Handlers().Handle(new MarkBookingNoShowAsAdminCommand(booking.Id), CancellationToken.None);

        Assert.Same(BookingStatus.NoShow, booking.Status);
        Assert.Empty(payment.Refunds);
        await context.Payments.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    /// <summary>
    /// A paid booking whose payment cannot be found is a programming error, not a business outcome:
    /// the whole request fails, so the cancellation never commits with the money it owes unrecorded.
    /// </summary>
    [Fact]
    public async Task An_admin_cancellation_whose_payment_is_missing_fails_whole_rather_than_ending_without_the_refund()
    {
        var context = new Context();
        var booking = context.Given(Build.ConfirmedBooking());
        context.At(Build.Now.AddDays(3));

        await Assert.ThrowsAsync<DomainException>(() => context.Handlers().Handle(
            new CancelBookingAsAdminCommand(booking.Id, "Anything."),
            CancellationToken.None));

        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
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
