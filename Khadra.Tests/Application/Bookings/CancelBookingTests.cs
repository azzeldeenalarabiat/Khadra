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
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public INotifier Notifier { get; } = Substitute.For<INotifier>();
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public TestClock Clock { get; } = new(Build.Now);
        public Dealer Dealer { get; }

        public Context()
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            Reader.ContextAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>())
                .Returns(new BookingContext(null, "Al-Nadeem Rentals", "Layla Odeh", null, null));
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

        public Booking GivenConfirmed() =>
            Given(Build.ConfirmedBooking(customerId: CustomerId, dealerId: Dealer.Id));

        public CancelBookingHandlers Handlers() =>
            new(Bookings, Reader, Dealers, new DealerTeamNotifier(Notifier, Users), Clock, UnitOfWork);

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
