using CSharpFunctionalExtensions;
using Khadra.Application.Bookings;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Notifications;
using Khadra.Application.Payments;
using Khadra.Application.Payments.OpenCheckout;
using Khadra.Application.Payments.ReceiveProviderEvent;
using Khadra.Application.Payments.SettlePayments;
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
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Khadra.Tests.Application.Payments;

/// <summary>
/// Taking a deposit, and hearing back about it.
/// </summary>
/// <remarks>
/// The tests that matter here are the ones about money going astray, not the happy path. A capture
/// that lands on an expired booking, a webhook delivered twice, a customer who taps Pay twice, a
/// provider that names a reference nobody issued: each is a way for a real person to be charged for
/// a rental they will not get, and each has to end with a refund recorded.
/// </remarks>
public sealed class PaymentUseCaseTests
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly Id CustomerId = Id.New();

    private sealed class Context
    {
        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IPaymentRepository Payments { get; } = Substitute.For<IPaymentRepository>();
        public IProviderEventReceiptRepository Receipts { get; } = Substitute.For<IProviderEventReceiptRepository>();
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public INotifier Notifier { get; } = Substitute.For<INotifier>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public TestClock Clock { get; } = new(Now);
        public IPaymentProvider Provider { get; set; } = TestPayments.Working();
        public IPaymentSettings Settings { get; } = TestPayments.Settings();

        public List<Payment> Added { get; } = [];
        public List<ProviderEventReceipt> Recorded { get; } = [];

        public Context()
        {
            Payments.When(repository => repository.Add(Arg.Any<Payment>()))
                .Do(call => Added.Add(call.Arg<Payment>()));
            Receipts.When(repository => repository.Add(Arg.Any<ProviderEventReceipt>()))
                .Do(call => Recorded.Add(call.Arg<ProviderEventReceipt>()));

            var customer = Build.Customer(email: "renter@khadra.test");
            Users.GetByIdAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>()).Returns(customer);
        }

        public Booking GivenApproved(TimeSpan? paymentWindow = null)
        {
            var terms = paymentWindow is { } window ? Build.Terms(paymentWindow: window) : Build.Terms();
            var booking = Build.ApprovedBooking(Now, terms: terms, customerId: CustomerId);
            Bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
            return booking;
        }

        public void GivenDealerFor(Booking booking)
        {
            var dealer = Build.ApprovedDealer(Now);
            Dealers.GetByIdAsync(booking.DealerId, Arg.Any<CancellationToken>()).Returns(dealer);
        }

        public void GivenLive(Payment payment) =>
            Payments.GetLiveForBookingAsync(payment.BookingId, Arg.Any<CancellationToken>()).Returns(payment);

        public void GivenReference(Payment payment) =>
            Payments.GetByProviderReferenceAsync(
                    TestPayments.TestProviderName,
                    payment.ProviderReference!,
                    Arg.Any<CancellationToken>())
                .Returns(payment);

        public OpenDepositCheckoutHandler Open() =>
            new(Bookings, Payments, Users, Provider, Settings, Clock, UnitOfWork);

        public ReceiveProviderEventHandler Receive() =>
            new(
                Provider,
                Payments,
                Receipts,
                Bookings,
                Dealers,
                new DealerTeamNotifier(Notifier, Users),
                Clock,
                UnitOfWork,
                NullLogger<ReceiveProviderEventHandler>.Instance);

        public SettlePaymentsHandler Sweep() =>
            new(Payments, Provider, Settings, Clock, UnitOfWork, NullLogger<SettlePaymentsHandler>.Instance);
    }

    // ---------------------------------------------------------------- opening a checkout

    /// <summary>
    /// The shipped configuration. Nothing is created, because a row that can never be paid would
    /// occupy the one-live-attempt slot and block the customer once a provider does exist.
    /// </summary>
    [Fact]
    public async Task With_no_provider_configured_nothing_is_created_and_the_answer_is_503()
    {
        var context = new Context { Provider = TestPayments.NoProvider() };
        var booking = context.GivenApproved();

        var result = await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id), CancellationToken.None);

        Assert.Equal("payments.provider_unavailable", result.Error.Code);
        Assert.Equal(ErrorKind.Unavailable, result.Error.Kind);
        Assert.Empty(context.Added);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_checkout_opens_against_the_bookings_own_frozen_deposit()
    {
        var context = new Context();
        var booking = context.GivenApproved();

        var result = await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var payment = Assert.Single(context.Added);
        // The customer sent no figure at all; the amount is the one the booking froze.
        Assert.Equal(booking.Pricing.DepositAmount.Amount, payment.Amount.Amount);
        Assert.Equal(booking.Pricing.CurrencyCode, payment.Amount.CurrencyCode);
        Assert.Same(PaymentStatus.Pending, payment.Status);
        Assert.Equal("sess_1", payment.ProviderReference);
        Assert.Equal(booking.Id, payment.BookingId);
    }

    /// <summary>
    /// A booking that is not this customer's answers NOT FOUND, not forbidden. Telling a stranger
    /// that a booking exists is itself a leak.
    /// </summary>
    [Fact]
    public async Task Another_customers_booking_is_not_found()
    {
        var context = new Context();
        var booking = context.GivenApproved();

        var result = await context.Open().Handle(
            new OpenDepositCheckoutCommand(Id.New(), booking.Id), CancellationToken.None);

        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
        Assert.Empty(context.Added);
    }

    [Theory]
    [InlineData("Requested")]
    [InlineData("Confirmed")]
    [InlineData("Cancelled")]
    public async Task A_booking_that_is_not_awaiting_payment_cannot_open_a_checkout(string state)
    {
        var context = new Context();
        var booking = state switch
        {
            "Requested" => Build.RequestedBooking(Now),
            "Confirmed" => Build.ConfirmedBooking(Now, customerId: CustomerId),
            _ => Cancelled()
        };
        context.Bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var result = await context.Open().Handle(
            new OpenDepositCheckoutCommand(booking.CustomerId, booking.Id), CancellationToken.None);

        Assert.Equal("booking.not_awaiting_payment", result.Error.Code);
        Assert.Empty(context.Added);

        static Booking Cancelled()
        {
            var booking = Build.ApprovedBooking(Now, customerId: CustomerId);
            booking.Cancel(BookingParty.Customer, CustomerId, "changed plans", Now);
            return booking;
        }
    }

    /// <summary>
    /// The payment window is enforced HERE and nowhere else. Once a provider has captured, the money
    /// has moved and refusing it would mean keeping it.
    /// </summary>
    [Fact]
    public async Task A_checkout_cannot_open_after_the_payment_deadline()
    {
        var context = new Context();
        var booking = context.GivenApproved(paymentWindow: TimeSpan.FromHours(24));
        context.Clock.UtcNow = booking.PaymentDeadline!.Value;

        var result = await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id), CancellationToken.None);

        Assert.Equal("booking.not_awaiting_payment", result.Error.Code);
        Assert.Empty(context.Added);
    }

    /// <summary>
    /// The cheap half of the late-capture problem: the session dies before the booking does, so the
    /// provider itself refuses a payment that would land too late to be applied.
    /// </summary>
    [Fact]
    public async Task A_session_never_outlives_the_bookings_own_payment_deadline()
    {
        var context = new Context();
        var booking = context.GivenApproved(paymentWindow: TimeSpan.FromHours(24));
        // Ten minutes left: less than the 30-minute session, so the deadline is what caps it.
        context.Clock.UtcNow = booking.PaymentDeadline!.Value.AddMinutes(-10);

        await context.Open().Handle(new OpenDepositCheckoutCommand(CustomerId, booking.Id), CancellationToken.None);

        var payment = Assert.Single(context.Added);
        Assert.Equal(booking.PaymentDeadline!.Value.AddMinutes(-5), payment.ExpiresAt);
        Assert.True(payment.ExpiresAt < booking.PaymentDeadline);
    }

    [Fact]
    public async Task A_checkout_is_refused_when_the_margin_leaves_no_time_at_all()
    {
        var context = new Context();
        var booking = context.GivenApproved(paymentWindow: TimeSpan.FromHours(24));
        // Inside the closing margin: the window is technically open, but a session opened now would
        // expire before it opened.
        context.Clock.UtcNow = booking.PaymentDeadline!.Value.AddMinutes(-4);

        var result = await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id), CancellationToken.None);

        Assert.Equal("booking.not_awaiting_payment", result.Error.Code);
        Assert.Empty(context.Added);
    }

    /// <summary>Two taps land on ONE session. Two would be two card forms for one deposit.</summary>
    [Fact]
    public async Task Opening_twice_returns_the_attempt_already_in_flight()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        var first = await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id), CancellationToken.None);
        context.GivenLive(context.Added[0]);

        var second = await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id), CancellationToken.None);

        Assert.Equal(first.Value.PaymentId, second.Value.PaymentId);
        Assert.Single(context.Added);
    }

    /// <summary>A declined card is not the end: the dead attempt is retired and a fresh one opens.</summary>
    [Fact]
    public async Task An_expired_attempt_is_superseded_rather_than_blocking_the_next_one()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        var stale = Payment.Open(booking.Id, CustomerId, Money.Jod(18m), TestPayments.TestProviderName, Now.AddMinutes(1), Now);
        stale.AttachProviderSession("sess_old", "https://provider.test/old");
        context.GivenLive(stale);
        context.Clock.UtcNow = Now.AddMinutes(2);

        var result = await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(PaymentStatus.Failed, stale.Status);
        Assert.Equal("superseded", stale.FailureCode);
        Assert.Single(context.Added);
    }

    /// <summary>
    /// A provider that refuses leaves the row Initiated, not Failed: it is still usable, so the next
    /// tap resumes it with the same idempotency key rather than stacking another dead attempt.
    /// </summary>
    [Fact]
    public async Task A_provider_that_refuses_leaves_something_to_resume()
    {
        var context = new Context();
        context.Provider.CreateCheckoutAsync(Arg.Any<CheckoutRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<CheckoutSession, Error>(PaymentErrors.ProviderRefused));
        var booking = context.GivenApproved();

        var result = await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id), CancellationToken.None);

        Assert.Equal("payments.provider_refused", result.Error.Code);
        var payment = Assert.Single(context.Added);
        Assert.Same(PaymentStatus.Initiated, payment.Status);
        Assert.True(payment.IsUsable(Now));
    }

    [Fact]
    public async Task An_initiated_attempt_is_resumed_with_the_same_payment_id()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        var initiated = Payment.Open(booking.Id, CustomerId, Money.Jod(18m), TestPayments.TestProviderName, Now.AddMinutes(30), Now);
        context.GivenLive(initiated);

        var result = await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(initiated.Id.Value, result.Value.PaymentId);
        // No new row: the provider is asked again under the SAME key, so it hands back the session it
        // already made rather than opening a second one.
        Assert.Empty(context.Added);
        Assert.Same(PaymentStatus.Pending, initiated.Status);
    }

    // ---------------------------------------------------------------- receiving a provider event

    private static Payment PendingFor(Booking booking, decimal amount = 18m, string reference = "sess_1")
    {
        var payment = Payment.Open(
            booking.Id, booking.CustomerId, Money.Jod(amount), TestPayments.TestProviderName, Build.Now.AddMinutes(30), Build.Now);
        payment.AttachProviderSession(reference, $"https://provider.test/{reference}");
        return payment;
    }

    [Fact]
    public async Task A_capture_confirms_the_booking_and_records_the_receipt_that_makes_it_unrepeatable()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        context.GivenDealerFor(booking);
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        context.GivenReference(payment);
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(
                TestPayments.Captured("sess_1", Money.Jod(booking.Pricing.DepositAmount.Amount), Now)));

        var result = await context.Receive().Handle(
            new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(payment.Id, booking.DepositPaymentId);
        Assert.Same(PaymentStatus.Applied, payment.Status);

        var receipt = Assert.Single(context.Recorded);
        Assert.Same(ProviderEventOutcome.Acted, receipt.Outcome);
        Assert.Equal("evt_1", receipt.ProviderEventId);
        Assert.Equal(payment.Id, receipt.PaymentId);

        // ONE save: the capture, the confirmation, the notifications and the receipt land together.
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The sharpest case in the feature: money taken for a booking that had already gone. It must not
    /// confirm, and it must not be kept.
    /// </summary>
    [Fact]
    public async Task A_capture_on_an_expired_booking_is_orphaned_and_refunded()
    {
        var context = new Context();
        var booking = context.GivenApproved(paymentWindow: TimeSpan.FromHours(24));
        booking.ExpireUnpaid(booking.PaymentDeadline!.Value);
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        context.GivenReference(payment);
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(
                TestPayments.Captured("sess_1", Money.Jod(booking.Pricing.DepositAmount.Amount), Now)));

        var result = await context.Receive().Handle(
            new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None);

        // 2xx: there is nothing the provider could do differently by retrying.
        Assert.True(result.IsSuccess);
        Assert.Same(BookingStatus.Expired, booking.Status);
        Assert.Null(booking.DepositPaymentId);
        Assert.Same(PaymentStatus.Orphaned, payment.Status);
        Assert.Equal("BookingExpired", payment.OrphanReason);

        var refund = Assert.Single(payment.Refunds);
        Assert.Same(RefundReason.OrphanedCapture, refund.Reason);
        Assert.Equal(booking.Pricing.DepositAmount.Amount, refund.Amount.Amount);
        Assert.Same(ProviderEventOutcome.Orphaned, Assert.Single(context.Recorded).Outcome);
    }

    [Fact]
    public async Task A_capture_on_a_cancelled_booking_is_orphaned_and_refunded()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        booking.Cancel(BookingParty.Customer, CustomerId, "changed plans", Now);
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        context.GivenReference(payment);
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(
                TestPayments.Captured("sess_1", Money.Jod(booking.Pricing.DepositAmount.Amount), Now)));

        await context.Receive().Handle(
            new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None);

        Assert.Same(PaymentStatus.Orphaned, payment.Status);
        Assert.Equal("BookingCancelled", payment.OrphanReason);
        Assert.Single(payment.Refunds);
    }

    /// <summary>
    /// Two attempts, both captured. The second cannot confirm a booking another payment already
    /// paid, so it goes back -- and the booking keeps the FIRST payment's id.
    /// </summary>
    [Fact]
    public async Task A_second_capture_on_an_already_paid_booking_is_orphaned()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        var alreadyPaid = Id.New();
        booking.ConfirmDepositPaid(alreadyPaid, Now);
        var second = PendingFor(booking, booking.Pricing.DepositAmount.Amount, "sess_2");
        context.GivenReference(second);
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(
                TestPayments.Captured("sess_2", Money.Jod(booking.Pricing.DepositAmount.Amount), Now)));

        await context.Receive().Handle(
            new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None);

        Assert.Equal(alreadyPaid, booking.DepositPaymentId);
        Assert.Same(PaymentStatus.Orphaned, second.Status);
        Assert.Equal("AlreadyPaidByAnotherAttempt", second.OrphanReason);
        Assert.Single(second.Refunds);
    }

    /// <summary>
    /// A capture for the wrong amount never reaches the booking at all: the payment's own guard runs
    /// first, so nothing is confirmed and the money still goes back.
    /// </summary>
    [Fact]
    public async Task A_capture_for_the_wrong_amount_is_orphaned_without_touching_the_booking()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        context.GivenReference(payment);
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(TestPayments.Captured("sess_1", Money.Jod(1m), Now)));

        await context.Receive().Handle(
            new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None);

        Assert.Same(BookingStatus.Approved, booking.Status);
        Assert.Null(booking.DepositPaymentId);
        Assert.Same(PaymentStatus.Orphaned, payment.Status);
        Assert.Equal("payments.amount_mismatch", payment.OrphanReason);
        // What was actually taken, not what was asked for.
        Assert.Equal(Money.Jod(1m), Assert.Single(payment.Refunds).Amount);
    }

    /// <summary>
    /// An event naming a reference this platform never issued. It is recorded -- it is exactly the
    /// event somebody will need to find -- and answered as a success, because a retry changes nothing.
    /// </summary>
    [Fact]
    public async Task An_event_for_an_unknown_reference_is_recorded_and_ignored()
    {
        var context = new Context();
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(
                TestPayments.Captured("sess_nobody_issued", Money.Jod(18m), Now)));

        var result = await context.Receive().Handle(
            new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var receipt = Assert.Single(context.Recorded);
        Assert.Same(ProviderEventOutcome.Unknown, receipt.Outcome);
        Assert.Null(receipt.PaymentId);
        Assert.Equal("sess_nobody_issued", receipt.ProviderReference);
    }

    /// <summary>An unverifiable body must never reach a query, let alone a booking.</summary>
    [Fact]
    public async Task An_event_that_does_not_verify_is_refused_before_anything_is_read()
    {
        var context = new Context { Provider = TestPayments.NoProvider() };

        var result = await context.Receive().Handle(
            new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None);

        Assert.Equal("payments.untrusted_event", result.Error.Code);
        Assert.Equal(ErrorKind.Unauthorized, result.Error.Kind);
        Assert.Empty(context.Recorded);
        await context.Payments.DidNotReceive().GetByProviderReferenceAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failure_event_closes_the_attempt_and_leaves_the_booking_alone()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        var payment = PendingFor(booking);
        context.GivenReference(payment);
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(TestPayments.Failed("sess_1", Now)));

        await context.Receive().Handle(
            new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None);

        Assert.Same(PaymentStatus.Failed, payment.Status);
        Assert.Equal("card_declined", payment.FailureCode);
        // The booking is untouched: the customer may still try again inside their window.
        Assert.Same(BookingStatus.Approved, booking.Status);
        Assert.True(booking.IsAwaitingPayment(Now));
    }

    /// <summary>
    /// A capture with no amount on it is not a capture. Assuming the figure we hoped for would put a
    /// number on the record that the provider never confirmed.
    /// </summary>
    [Fact]
    public async Task A_capture_carrying_no_amount_is_refused_rather_than_guessed()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        var payment = PendingFor(booking);
        context.GivenReference(payment);
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(
                new ProviderEvent("evt_1", "sess_1", ProviderEventKind.Captured, null, null, Now)));

        await context.Receive().Handle(
            new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None);

        Assert.Same(PaymentStatus.Pending, payment.Status);
        Assert.Same(BookingStatus.Approved, booking.Status);
        Assert.Same(ProviderEventOutcome.Ignored, Assert.Single(context.Recorded).Outcome);
    }

    // ---------------------------------------------------------------- the sweep

    /// <summary>
    /// The sharpest race in the feature: the settlement job expiring a booking at the same instant a
    /// capture lands on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both load the booking as Approved and exactly one save wins on <c>xmin</c>. When the JOB wins,
    /// this handler's save throws and NOTHING it did is written -- not the confirmation, not the
    /// capture, not the receipt that would have made the delivery un-replayable.
    /// </para>
    /// <para>
    /// The exception escapes on purpose, and this test is why. The first draft retried in place, and
    /// this test failed: EF keeps the in-memory mutations after a failed <c>SaveChanges</c>, so the
    /// second pass found a payment that already read Applied, could not orphan it, and left a
    /// customer's money attached to an expired booking with no refund recorded. Handing the delivery
    /// back to the provider is the only retry that reads the database again.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_capture_that_loses_a_race_is_handed_back_to_the_provider_unwritten()
    {
        var context = new Context();
        var booking = context.GivenApproved(paymentWindow: TimeSpan.FromHours(24));
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        context.GivenReference(payment);
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(
                TestPayments.Captured("sess_1", Money.Jod(booking.Pricing.DepositAmount.Amount), Now)));

        var saves = 0;
        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ =>
            {
                saves++;
                throw new ConcurrencyConflictException("The settlement job got there first.");
            });

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            context.Receive().Handle(
                new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None));

        // Tried exactly once. A second pass through the same scope would decide against dirty state.
        Assert.Equal(1, saves);
        // And the receipt went with the failed transaction, so the re-delivery is not a replay.
        Assert.Single(context.Recorded);
    }

    [Fact]
    public async Task With_no_provider_the_sweep_does_nothing_and_says_so()
    {
        var context = new Context { Provider = TestPayments.NoProvider() };
        context.Payments.ListWithOutstandingRefundsAsync(Arg.Any<CancellationToken>())
            .Returns([]);

        var report = await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Assert.Equal(PaymentSweepReport.Empty, report.Value);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A lost expiry notice must not cost the customer their booking: the row is closed so a
    /// replacement can open inside the same payment window.
    /// </summary>
    [Fact]
    public async Task The_sweep_closes_an_attempt_the_provider_agrees_was_never_paid()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        var stale = PendingFor(booking);
        context.Payments.ListStaleLiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([stale]);
        context.Payments.ListWithOutstandingRefundsAsync(Arg.Any<CancellationToken>()).Returns([]);
        context.Provider.QueryAsync("sess_1", Arg.Any<CancellationToken>())
            .Returns(Result.Success<ProviderPaymentState, Error>(
                new ProviderPaymentState(ProviderEventKind.Failed, null, "expired")));

        var report = await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Assert.Equal(1, report.Value.Closed);
        Assert.Same(PaymentStatus.Failed, stale.Status);
        Assert.Equal("expired", stale.FailureCode);
    }

    /// <summary>
    /// A session the platform gave up on that the provider says WAS paid. Closing it would mark a
    /// paid booking unpaid, so the sweep refuses to touch it.
    /// </summary>
    [Fact]
    public async Task The_sweep_never_closes_an_attempt_the_provider_says_was_captured()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        var stale = PendingFor(booking);
        context.Payments.ListStaleLiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([stale]);
        context.Payments.ListWithOutstandingRefundsAsync(Arg.Any<CancellationToken>()).Returns([]);
        context.Provider.QueryAsync("sess_1", Arg.Any<CancellationToken>())
            .Returns(Result.Success<ProviderPaymentState, Error>(
                new ProviderPaymentState(ProviderEventKind.Captured, Money.Jod(18m), null)));

        var report = await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Assert.Equal(0, report.Value.Closed);
        Assert.Same(PaymentStatus.Pending, stale.Status);
    }

    /// <summary>An unreachable provider is not evidence of anything. Leave the row alone.</summary>
    [Fact]
    public async Task The_sweep_leaves_an_attempt_alone_when_the_provider_cannot_be_reached()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        var stale = PendingFor(booking);
        context.Payments.ListStaleLiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([stale]);
        context.Payments.ListWithOutstandingRefundsAsync(Arg.Any<CancellationToken>()).Returns([]);
        context.Provider.QueryAsync("sess_1", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ProviderPaymentState, Error>(PaymentErrors.ProviderUnavailable));

        var report = await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Assert.Equal(0, report.Value.Closed);
        Assert.Same(PaymentStatus.Pending, stale.Status);
    }

    [Fact]
    public async Task The_sweep_sends_the_refunds_the_platform_owes()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        payment.Orphan(booking.Pricing.DepositAmount, Now, "BookingExpired", Now);
        context.Payments.ListStaleLiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        context.Payments.ListWithOutstandingRefundsAsync(Arg.Any<CancellationToken>()).Returns([payment]);

        var report = await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Assert.Equal(1, report.Value.RefundsSent);
        var refund = Assert.Single(payment.Refunds);
        Assert.Same(RefundStatus.Sent, refund.Status);
        Assert.Equal("ref_1", refund.ProviderReference);
    }

    [Fact]
    public async Task A_refund_the_provider_refuses_stays_owed()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        payment.Orphan(booking.Pricing.DepositAmount, Now, "BookingExpired", Now);
        context.Payments.ListStaleLiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        context.Payments.ListWithOutstandingRefundsAsync(Arg.Any<CancellationToken>()).Returns([payment]);
        context.Provider.RefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ProviderRefund, Error>(PaymentErrors.ProviderRefused));

        var report = await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Assert.Equal(1, report.Value.RefundsFailed);
        Assert.Same(RefundStatus.Failed, Assert.Single(payment.Refunds).Status);
    }
}
