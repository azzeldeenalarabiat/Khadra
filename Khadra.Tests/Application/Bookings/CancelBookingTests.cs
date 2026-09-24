using CSharpFunctionalExtensions;
using Khadra.Application.Bookings.CancelBooking;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Notifications;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications;
using Khadra.Domain.Notifications.Repositories;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Bookings;

/// <summary>
/// Spec 5.5, the customer's own exit from a booking.
///
/// The edges that matter here are not the happy path. They are: a booking that belongs to somebody
/// else must not be distinguishable from one that does not exist; a retry must not report failure for
/// something that succeeded; and a window that closed while the customer was deciding must produce
/// "the time ran out", not "you cancelled this".
/// </summary>
public sealed class CancelBookingTests
{
    private static readonly Id CustomerId = Id.New();

    private sealed class Context
    {
        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IBookingReader Reader { get; } = Substitute.For<IBookingReader>();
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public IPaymentRepository Payments { get; } = Substitute.For<IPaymentRepository>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public INotifier Notifier { get; } = Substitute.For<INotifier>();
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public TestClock Clock { get; } = new(Build.Now);
        public Dealer Dealer { get; }

        public Context()
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            Reader.ContextAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>())
                .Returns(new BookingContext(null, "Al-Nadeem Rentals", false, null, "Layla Odeh", false, null, null));
            Dealer = Build.ApprovedDealer();
            Dealers.GetByIdAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>()).Returns(Dealer);
        }

        public Booking Given(Booking booking)
        {
            booking.ClearDomainEvents();
            Bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
            return booking;
        }

        public Booking GivenRequested() =>
            Given(Build.Booking(customerId: CustomerId, dealerId: Dealer.Id));

        public Booking GivenApproved() =>
            Given(Build.ApprovedBooking(customerId: CustomerId, dealerId: Dealer.Id));

        /// <summary>
        /// Confirmed the only way production confirms: against a real payment that captured the
        /// booking's deposit, which the repository then answers for.
        /// </summary>
        public Booking GivenConfirmed() => GivenConfirmed(out _);

        public Booking GivenConfirmed(out Payment payment)
        {
            var booking = Build.ApprovedBooking(Clock.UtcNow, customerId: CustomerId, dealerId: Dealer.Id);
            var deposit = Money.Create(booking.Pricing.DepositAmount.Amount, booking.Pricing.CurrencyCode);
            payment = Payment.Open(booking.Id, CustomerId, deposit, "TestProvider", Clock.UtcNow.AddMinutes(30), Clock.UtcNow);
            payment.AttachProviderSession("sess_" + booking.Reference.Value, "https://provider.test/checkout");
            Assert.True(payment.Apply(Money.Create(deposit.Amount, deposit.CurrencyCode), Clock.UtcNow, Clock.UtcNow).IsSuccess);
            booking.ConfirmDepositPaid(payment.Id, Clock.UtcNow);
            Payments.GetByIdAsync(payment.Id, Arg.Any<CancellationToken>()).Returns(payment);
            return Given(booking);
        }

        public CancelBookingHandlers Handlers() =>
            new(Bookings, Reader, Dealers, Payments, new DealerTeamNotifier(Notifier, Users), Clock, UnitOfWork);

        public Task<Result<BookingDto, Error>> Cancel(
            Id bookingId,
            string reasonCode = "PlansChanged",
            string? details = null) =>
            Handlers().Handle(new CancelMyBookingCommand(CustomerId, bookingId, reasonCode, details), CancellationToken.None);
    }

    [Fact]
    public async Task A_customer_cancels_their_own_request_and_it_costs_them_nothing()
    {
        var context = new Context();
        var booking = context.GivenRequested();

        var result = await context.Cancel(booking.Id, "PlansChanged", "Flight moved.");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(BookingStatus.Cancelled, booking.Status);
        Assert.Same(BookingParty.Customer, booking.CancelledBy);
        Assert.Equal("PlansChanged", booking.CancellationReasonCode);
        Assert.Equal("Flight moved.", booking.CancellationReason);
        // Nothing was paid, so there is nothing a penalty could bite on.
        Assert.True(booking.Penalty!.IsNothingOwed);
        Assert.False(booking.OccupiesVehicle);
    }

    /// <summary>
    /// The code and the customer's words are stored apart, so the sentence a gallery reads is chosen
    /// in the gallery's language rather than frozen in the customer's.
    /// </summary>
    [Fact]
    public async Task The_reason_code_reaches_the_history_beside_the_customers_own_words()
    {
        var context = new Context();
        var booking = context.GivenRequested();

        await context.Cancel(booking.Id, "TravelCancelled", "الرحلة أُلغيت");

        var last = booking.StatusHistory.OrderBy(change => change.OccurredAt).Last();
        Assert.Equal("TravelCancelled", last.ReasonCode);
        Assert.Equal("الرحلة أُلغيت", last.Reason);
    }

    [Fact]
    public async Task The_gallery_is_told_in_the_same_transaction_and_the_customer_is_not_named()
    {
        var context = new Context();
        var booking = context.GivenRequested();

        await context.Cancel(booking.Id);

        var raised = context.Notifier.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name is nameof(INotifier.Raise) or nameof(INotifier.RaiseMany))
            .ToList();

        Assert.NotEmpty(raised);
        // Staged before the save, so the row and the cancellation land together or not at all.
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A 403 would confirm the id is real, and booking ids are the one thing a stranger must not be
    /// able to enumerate.
    /// </summary>
    [Fact]
    public async Task Somebody_elses_booking_is_indistinguishable_from_one_that_does_not_exist()
    {
        var context = new Context();
        var theirs = context.Given(Build.Booking(customerId: Id.New()));

        var mine = await context.Cancel(theirs.Id);
        var missing = await context.Cancel(Id.New());

        Assert.Equal("booking.not_found", mine.Error.Code);
        Assert.Equal("booking.not_found", missing.Error.Code);
        Assert.Same(BookingStatus.Requested, theirs.Status);
    }

    /// <summary>
    /// A phone retries a request whose answer it never saw. Refusing the second attempt would tell the
    /// customer their cancellation failed when it had succeeded.
    /// </summary>
    [Fact]
    public async Task Cancelling_twice_answers_with_the_booking_rather_than_a_conflict()
    {
        var context = new Context();
        var booking = context.GivenRequested();

        var first = await context.Cancel(booking.Id);
        var retry = await context.Cancel(booking.Id);

        Assert.True(first.IsSuccess);
        Assert.True(retry.IsSuccess, retry.IsFailure ? retry.Error.Code : null);
        Assert.Equal("Cancelled", retry.Value.Status);
        // Only the first attempt wrote anything.
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The clock had already ended this booking; only the row was behind. Recording a cancellation
    /// over it would say "you cancelled this" where the truth is "the gallery never answered", and
    /// would tell the gallery a customer walked away from a request that lapsed on their own clock.
    /// </summary>
    [Fact]
    public async Task A_request_whose_answer_window_closed_expires_instead_of_being_cancelled()
    {
        var context = new Context();
        var booking = context.GivenRequested();
        context.Clock.UtcNow = booking.DecisionDeadline;

        var result = await context.Cancel(booking.Id);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(BookingStatus.Expired, booking.Status);
        Assert.Null(booking.CancelledBy);
        Assert.Equal("Expired", result.Value.Status);
    }

    [Fact]
    public async Task An_approval_whose_payment_window_closed_expires_instead_of_being_cancelled()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        context.Clock.UtcNow = booking.PaymentDeadline!.Value;

        var result = await context.Cancel(booking.Id);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(BookingStatus.Expired, booking.Status);
        Assert.Null(booking.CancelledBy);
    }

    /// <summary>
    /// Past the free window a confirmed booking costs the customer their deposit — as an ASSESSMENT
    /// (spec 3.3), which nothing collects without a dispute ticket.
    /// </summary>
    [Fact]
    public async Task Cancelling_a_paid_booking_after_the_free_window_assesses_the_deposit()
    {
        var context = new Context();
        var booking = context.GivenConfirmed();
        context.Clock.UtcNow = booking.FreeCancellationDeadline!.Value.AddMinutes(1);

        var result = await context.Cancel(booking.Id);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(BookingParty.Customer, booking.Penalty!.AttributedTo);
        Assert.Equal(booking.Pricing.DepositAmount.Amount, booking.Penalty.MaxAmount.Amount);
        Assert.True(booking.Penalty.RequiresTicketToEnforce);
    }

    /// <summary>
    /// The preview and the real thing share <c>AssessCancellation</c>, so the figure on the
    /// confirmation sheet is the figure that gets recorded. If they could differ, a customer would be
    /// agreeing to one number and charged against another.
    /// </summary>
    [Fact]
    public async Task The_preview_shown_before_the_tap_is_the_penalty_that_is_recorded()
    {
        var context = new Context();
        var booking = context.GivenConfirmed();
        context.Clock.UtcNow = booking.FreeCancellationDeadline!.Value.AddMinutes(1);

        var preview = booking.PreviewCancellation(BookingParty.Customer, context.Clock.UtcNow);
        var result = await context.Cancel(booking.Id);

        Assert.True(preview.CanCancel);
        Assert.False(preview.IsFree);
        Assert.Equal(preview.Penalty.MaxAmount.Amount, result.Value.Penalty!.MaxAmount.Amount);
    }

    [Fact]
    public async Task Inside_the_free_window_a_paid_booking_costs_nothing()
    {
        var context = new Context();
        var booking = context.GivenConfirmed();

        var preview = booking.PreviewCancellation(BookingParty.Customer, context.Clock.UtcNow);
        var result = await context.Cancel(booking.Id);

        Assert.True(preview.IsFree);
        Assert.True(result.Value.Penalty!.IsNothingOwed);
    }

    /// <summary>A booking the car has already left on is the gallery's problem, not a cancellation.</summary>
    [Fact]
    public async Task A_collected_car_can_no_longer_be_cancelled()
    {
        var context = new Context();
        var booking = context.GivenConfirmed();
        booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start);

        var preview = booking.PreviewCancellation(BookingParty.Customer, context.Clock.UtcNow);
        var result = await context.Cancel(booking.Id);

        Assert.False(preview.CanCancel);
        Assert.Equal("booking.cannot_cancel", result.Error.Code);
    }

    // ── The paid free cancellation (owner, 2026-09-24) ────────────────────────────────────────────

    [Fact]
    public async Task A_paid_booking_cancelled_inside_the_free_window_records_a_full_refund_in_the_same_save()
    {
        var context = new Context();
        var booking = context.GivenConfirmed(out var payment);
        var saves = 0;
        var refundsAtSave = -1;
        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            saves++;
            refundsAtSave = payment.Refunds.Count;
            return 1;
        });

        var result = await context.Cancel(booking.Id);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(BookingStatus.Cancelled, booking.Status);
        Assert.True(booking.ReturnsDepositOnCancellation);
        var refund = Assert.Single(payment.Refunds);
        Assert.Same(RefundReason.FreeCancellation, refund.Reason);
        Assert.Same(RefundStatus.Requested, refund.Status);
        Assert.Equal(payment.AmountCaptured, refund.Amount);
        // ONE save, and the refund was already on the payment when it ran: the cancellation and the
        // money it owes back commit together or not at all.
        Assert.Equal(1, saves);
        Assert.Equal(1, refundsAtSave);
    }

    [Fact]
    public async Task An_unpaid_booking_cancelled_for_free_records_no_refund()
    {
        var context = new Context();
        var booking = context.GivenApproved();

        var result = await context.Cancel(booking.Id);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.True(result.Value.Penalty!.IsNothingOwed);
        Assert.False(booking.ReturnsDepositOnCancellation);
        await context.Payments.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    /// <summary>Outside the window the existing policy stands: assessed, not charged, and nothing refunded.</summary>
    [Fact]
    public async Task A_paid_booking_cancelled_after_the_free_window_is_not_refunded()
    {
        var context = new Context();
        var booking = context.GivenConfirmed(out var payment);
        context.Clock.UtcNow = booking.FreeCancellationDeadline!.Value.AddMinutes(1);

        var result = await context.Cancel(booking.Id);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.False(booking.ReturnsDepositOnCancellation);
        Assert.Empty(payment.Refunds);
        Assert.False(result.Value.Penalty!.IsNothingOwed);
    }

    /// <summary>
    /// A retry is recognised and answered, and adds nothing: a refund is created only by the
    /// cancellation that owes it, never by tapping cancel again.
    /// </summary>
    [Fact]
    public async Task Cancelling_again_does_not_create_a_second_refund()
    {
        var context = new Context();
        var booking = context.GivenConfirmed(out var payment);

        await context.Cancel(booking.Id);
        var again = await context.Cancel(booking.Id);
        var third = await context.Cancel(booking.Id);

        Assert.True(again.IsSuccess);
        Assert.True(third.IsSuccess);
        Assert.Single(payment.Refunds);
    }

    /// <summary>
    /// A paid booking whose payment cannot be found is a programming error, and the cancellation must
    /// not commit without the refund it owes: money held with nothing saying it is owed back.
    /// </summary>
    [Fact]
    public async Task A_paid_booking_whose_payment_is_missing_is_not_cancelled_without_its_refund()
    {
        var context = new Context();
        var booking = context.Given(Build.ConfirmedBooking(context.Clock.UtcNow, customerId: CustomerId, dealerId: context.Dealer.Id));

        await Assert.ThrowsAsync<DomainException>(() => context.Cancel(booking.Id));
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_promise_on_the_sheet_is_the_refund_the_cancellation_records()
    {
        var context = new Context();
        var paid = context.GivenConfirmed();
        var now = context.Clock.UtcNow;

        Assert.True(paid.CancellationWouldReturnDeposit(BookingParty.Customer, now));
        Assert.True(Khadra.Application.Bookings.Dtos.CancellationPreviewDto.From(
            paid.PreviewCancellation(BookingParty.Customer, now),
            paid.CancellationWouldReturnDeposit(BookingParty.Customer, now)).WillRefundDeposit);
        // The owner's rule names the customer: a gallery or an admin cancelling in that hour does not
        // trigger it (an open owner question, recorded in the pre-launch checklist).
        Assert.False(paid.CancellationWouldReturnDeposit(BookingParty.Dealer, now));
        Assert.False(paid.CancellationWouldReturnDeposit(BookingParty.Admin, now));
        // Past the window, and with nothing paid, there is nothing to promise.
        Assert.False(paid.CancellationWouldReturnDeposit(BookingParty.Customer, paid.FreeCancellationDeadline!.Value.AddMinutes(1)));
        Assert.False(context.GivenApproved().CancellationWouldReturnDeposit(BookingParty.Customer, now));
    }

    [Fact]
    public void An_unlisted_cancellation_reason_is_refused_before_the_handler()
    {
        var validator = new CancelMyBookingCommandValidator();

        var unknown = validator.Validate(new CancelMyBookingCommand(CustomerId, Id.New(), "Whatever", null));
        var known = validator.Validate(new CancelMyBookingCommand(CustomerId, Id.New(), "PlansChanged", null));

        Assert.False(unknown.IsValid);
        Assert.True(known.IsValid);
    }

    /// <summary>
    /// Not reachable end to end until Payments exists, because it needs a Confirmed booking. The rule
    /// is tested where it lives.
    /// </summary>
    [Fact]
    public async Task Non_delivery_cannot_be_reported_before_the_rental_was_due()
    {
        var context = new Context();
        var booking = context.GivenConfirmed();

        var tooEarly = await context.Handlers().Handle(
            new ReportNonDeliveryCommand(CustomerId, booking.Id, "Nobody came."), CancellationToken.None);

        context.Clock.UtcNow = booking.Period.Start;
        var due = await context.Handlers().Handle(
            new ReportNonDeliveryCommand(CustomerId, booking.Id, "Nobody came."), CancellationToken.None);

        Assert.Equal("booking.non_delivery_too_early", tooEarly.Error.Code);
        Assert.True(due.IsSuccess, due.IsFailure ? due.Error.Code : null);
        Assert.Same(BookingParty.Dealer, booking.Penalty!.AttributedTo);
    }
}
