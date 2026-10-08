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
using Microsoft.Extensions.Logging;
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
        public IPaymentIncidentRepository Incidents { get; } = Substitute.For<IPaymentIncidentRepository>();
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public INotifier Notifier { get; } = Substitute.For<INotifier>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public TestClock Clock { get; } = new(Now);
        public IPaymentProvider Provider { get; set; } = TestPayments.Working();
        public IPaymentSettings Settings { get; } = TestPayments.Settings();
        public IBusinessRulesProvider Rules { get; set; } = TestBusinessRules.Provider();

        public List<Payment> Added { get; } = [];
        public List<ProviderEventReceipt> Recorded { get; } = [];
        public List<PaymentIncident> Raised { get; } = [];

        public Context()
        {
            Payments.When(repository => repository.Add(Arg.Any<Payment>()))
                .Do(call => Added.Add(call.Arg<Payment>()));
            Receipts.When(repository => repository.Add(Arg.Any<ProviderEventReceipt>()))
                .Do(call => Recorded.Add(call.Arg<ProviderEventReceipt>()));
            Incidents.When(repository => repository.Add(Arg.Any<PaymentIncident>()))
                .Do(call => Raised.Add(call.Arg<PaymentIncident>()));

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

        /// <summary>
        /// The sweep's view of these payments: listed while one of their refunds is due by its own schedule, as the
        /// repository's query decides, and each loaded by its id.
        /// </summary>
        public void GivenOwing(params Payment[] owing)
        {
            Payments.ListIdsWithRefundsDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult<IReadOnlyList<Id>>(
                [
                    .. owing
                        .Where(payment => payment.ProviderReference is not null
                            && payment.Refunds.Any(refund => refund.IsDueToSend(call.Arg<DateTimeOffset>())))
                        .Select(payment => payment.Id),
                ]));
            foreach (var payment in owing)
                Payments.GetByIdAsync(payment.Id, Arg.Any<CancellationToken>()).Returns(payment);
        }

        public void GivenReference(Payment payment) =>
            Payments.GetByProviderReferenceAsync(
                    TestPayments.TestProviderName,
                    payment.ProviderReference!,
                    Arg.Any<CancellationToken>())
                .Returns(payment);

        public OpenDepositCheckoutHandler Open() =>
            new(Bookings, Payments, Users, Provider, Settings, Rules, Clock, UnitOfWork);

        public ReceiveProviderEventHandler Receive() =>
            new(
                Provider,
                Payments,
                Receipts,
                Incidents,
                Bookings,
                Dealers,
                new DealerTeamNotifier(Notifier, Users),
                Clock,
                Settings,
                UnitOfWork,
                ReceiveLog);

        public RecordingLogger<ReceiveProviderEventHandler> ReceiveLog { get; } = new();

        public RecordingLogger<SettlePaymentsHandler> SweepLog { get; } = new();

        public SettlePaymentsHandler Sweep() =>
            new(Payments, Provider, Settings, Clock, UnitOfWork, SweepLog);
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
    /// Back from the checkout in the language it was opened in (Fix & Polish Wave 3, E3; E2E F25): the request's own,
    /// else the customer's stored one, else the default. The device that opened the checkout returns in its own
    /// language; a phone in Arabic cannot send a website session back to Arabic.
    /// </summary>
    [Theory]
    [InlineData("ar", null, "ar")]
    [InlineData("en", "ar", "en")]
    [InlineData(null, "ar", "ar")]
    [InlineData(null, null, "en")]
    public async Task The_checkout_returns_in_the_language_it_was_opened_in(string? requested, string? stored, string expected)
    {
        var context = new Context();
        var customer = Build.Customer(email: "reader@khadra.test");
        if (stored is not null)
            customer.ChoosePreferredLanguage(Enumeration.FromName<Language>(stored)!);
        context.Users.GetByIdAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>()).Returns(customer);
        var booking = context.GivenApproved();
        CheckoutRequest? asked = null;
        await context.Provider.CreateCheckoutAsync(Arg.Do<CheckoutRequest>(request => asked = request), Arg.Any<CancellationToken>());

        var language = requested is null ? null : Enumeration.FromName<Language>(requested);
        var result = await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id, Language: language), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.NotNull(asked);
        Assert.Equal(expected, asked!.Language.Name);
        Assert.Equal($"https://app.test/{expected}/bookings/{booking.Id.Value}", asked.ReturnUrl.AbsoluteUri);
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
    /// Money that lands a moment after the deadline, on a booking the sweep has not reached, still
    /// confirms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ConfirmDepositPaid</c> does not read the deadline, and that is deliberate: the alternative
    /// is refusing money the platform has already taken, which means refunding a customer who did
    /// everything right. The car is still theirs to take — the hold row was never deleted, so nobody
    /// else could have booked it in the intervening seconds.
    /// </para>
    /// <para>
    /// Written on 2026-09-11, when the payment window went from 24 hours to 2. Nothing in the code
    /// changed; what changed is how often this happens. The settlement pass runs once a minute, so
    /// the gap between a spent deadline and an Expired row is up to a minute either way, and a
    /// customer paying in the last seconds of a two-hour window is a far more ordinary event than one
    /// paying in the last seconds of a day.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_capture_just_after_the_deadline_still_confirms_while_the_booking_is_approved()
    {
        var context = new Context();
        var booking = context.GivenApproved(paymentWindow: TimeSpan.FromHours(2));
        context.GivenDealerFor(booking);

        // Approved at Now, so the deposit is owed by Now + 2h — and the capture below lands thirty
        // seconds past that, on a row the sweep has not reached. That is the whole scenario.
        Assert.Equal(Now.AddHours(2), booking.PaymentDeadline);
        Assert.Same(BookingStatus.Approved, booking.Status);

        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        context.GivenReference(payment);
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(
                TestPayments.Captured(
                    "sess_1",
                    Money.Jod(booking.Pricing.DepositAmount.Amount),
                    booking.PaymentDeadline!.Value.AddSeconds(30))));

        var result = await context.Receive().Handle(
            new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(payment.Id, booking.DepositPaymentId);
        Assert.Same(PaymentStatus.Applied, payment.Status);
        Assert.Empty(payment.Refunds);

        // And the booking can no longer be expired out from under the customer who just paid.
        Assert.Equal(
            "booking.not_awaiting_payment",
            booking.ExpireUnpaid(booking.PaymentDeadline!.Value.AddMinutes(1)).Error.Code);
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

    /// <summary>
    /// A delivery the platform has already applied is ACKNOWLEDGED, not refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every provider redelivers until it gets a 2xx, so a duplicate is the protocol working rather
    /// than a fault. The receipt's unique index is what makes the effect happen once; losing that
    /// insert means somebody else already did the work, and the only correct answer is success.
    /// </para>
    /// <para>
    /// It answered 409 until 2026-09-21. Nothing was ever applied twice — the index saw to that — so
    /// this was never a money bug, and that is exactly why it survived: the only symptom was a
    /// provider retrying a delivery for days, and providers disable an endpoint that keeps failing.
    /// The capture that never arrives afterwards is a real booking left unconfirmed.
    /// </para>
    /// <para>
    /// The exception this catches is the TRANSLATED one. `UnitOfWork` turns SQLSTATE 23505 into it
    /// and carries the constraint name, so a different unique index racing elsewhere in the same save
    /// still escapes as the 409 it always was. That translation is Postgres-specific and cannot be
    /// exercised on the SQLite the persistence tests use, which is why it is asserted here, at the
    /// boundary where the decision is actually made.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_delivery_already_applied_is_acknowledged_rather_than_refused()
    {
        var context = new Context();
        var booking = context.GivenApproved(paymentWindow: TimeSpan.FromHours(24));
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        context.GivenReference(payment);
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(
                TestPayments.Captured("sess_1", Money.Jod(booking.Pricing.DepositAmount.Amount), Now)));

        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new UniqueConstraintConflictException(
                "already recorded",
                UniqueConstraintConflictException.ProviderEventReceiptConstraint,
                new InvalidOperationException()));

        var result = await context.Receive().Handle(
            new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None);

        // Success: the provider stops retrying, which is the whole point.
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// An ordinary redelivery is recognised BEFORE the capture is re-applied, so nothing is orphaned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The index catch above is the guard and always will be, but on its own it let every redelivery
    /// run the whole apply first: the payment's <c>already_captured</c> guard refused it, the refusal
    /// routed to the orphan path, the orphan failed too — an Applied payment cannot be orphaned — and
    /// the log recorded "captured 27 JOD is UNACCOUNTED FOR" about money sitting on that very row.
    /// The data was never wrong; the transaction rolled back and the provider got its 204. The LOG
    /// was wrong, and on a platform where somebody is meant to act on a missing-money line, a
    /// fabricated one is expensive. A manual replay on 2026-09-21 produced it three times.
    /// </para>
    /// <para>
    /// So the test asserts the absence of work rather than a status: the payment must never be asked
    /// to accept or orphan anything. Asserting only <c>IsSuccess</c> would pass on the old code too.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_redelivery_is_recognised_before_anything_is_orphaned()
    {
        var context = new Context();
        var booking = context.GivenApproved(paymentWindow: TimeSpan.FromHours(24));
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        context.GivenReference(payment);
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(
                TestPayments.Captured("sess_1", Money.Jod(booking.Pricing.DepositAmount.Amount), Now)));

        context.Receipts.HasSeenAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await context.Receive().Handle(
            new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Nothing was looked up, nothing was written, nothing was recorded a second time.
        await context.Payments.DidNotReceive().GetByProviderReferenceAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        context.Receipts.DidNotReceive().Add(Arg.Any<ProviderEventReceipt>());
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        // And the payment is untouched: still awaiting its capture, not orphaned.
        Assert.Same(PaymentStatus.Pending, payment.Status);
    }

    /// <summary>
    /// A different unique index losing the same save is NOT a duplicate delivery.
    /// </summary>
    /// <remarks>
    /// The catch is keyed on the constraint name precisely so it cannot swallow an unrelated race and
    /// tell a provider that a capture was applied when it was not. Anything unrecognised escapes and
    /// becomes the 409 it was before the translation existed.
    /// </remarks>
    [Fact]
    public async Task A_unique_violation_on_a_different_index_is_not_mistaken_for_a_replay()
    {
        var context = new Context();
        var booking = context.GivenApproved(paymentWindow: TimeSpan.FromHours(24));
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        context.GivenReference(payment);
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(
                TestPayments.Captured("sess_1", Money.Jod(booking.Pricing.DepositAmount.Amount), Now)));

        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new UniqueConstraintConflictException(
                "some other index",
                "ix_payments_booking_id",
                new InvalidOperationException()));

        await Assert.ThrowsAsync<UniqueConstraintConflictException>(() =>
            context.Receive().Handle(
                new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None));
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

    /// <summary>
    /// Pre-launch item 236: an unreachable provider will not answer the next stale checkout either, so the pass stops at
    /// the first one and says once how many it left — rather than waiting out a timeout per checkout and logging each,
    /// every tick. A provider that ANSWERS with a refusal for one checkout does not stop the others.
    /// </summary>
    [Fact]
    public async Task The_stale_checkout_pass_stops_at_the_first_unreachable_provider()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        var first = PendingFor(booking, reference: "sess_1");
        var second = PendingFor(booking, reference: "sess_2");
        var third = PendingFor(booking, reference: "sess_3");
        context.Payments.ListStaleLiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([first, second, third]);
        context.Payments.ListWithOutstandingRefundsAsync(Arg.Any<CancellationToken>()).Returns([]);
        context.Provider.QueryAsync("sess_1", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ProviderPaymentState, Error>(Error.Failure("provider.session_unknown", "Unknown session.")));
        context.Provider.QueryAsync("sess_2", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ProviderPaymentState, Error>(PaymentErrors.ProviderUnavailable));

        var report = await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Assert.Equal(0, report.Value.Closed);
        await context.Provider.DidNotReceive().QueryAsync("sess_3", Arg.Any<CancellationToken>());
        Assert.All(new[] { first, second, third }, payment => Assert.Same(PaymentStatus.Pending, payment.Status));
        // One line for the one that answered and would not say, one for the outage — and none per checkout after it.
        Assert.Single(context.SweepLog.Entries, entry => entry.Id.Id == 2312);
        var stopped = Assert.Single(context.SweepLog.Entries, entry => entry.Id.Id == 2326);
        Assert.Contains("2 stale checkout(s)", stopped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_sweep_sends_the_refunds_the_platform_owes()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        payment.Orphan(booking.Pricing.DepositAmount, Now, "BookingExpired", Now);
        context.Payments.ListStaleLiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        context.GivenOwing(payment);

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
        context.GivenOwing(payment);
        context.Provider.RefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ProviderRefund, Error>(PaymentErrors.ProviderRefused));

        var report = await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Assert.Equal(1, report.Value.RefundsFailed);
        Assert.Same(RefundStatus.Failed, Assert.Single(payment.Refunds).Status);
    }

    // ---------------------------------------------------------------- the free cancellation's refund (owner, 2026-09-24)

    /// <summary>A paid, confirmed booking, cancelled by its customer inside the window, with its refund recorded.</summary>
    private static (Booking Booking, Payment Payment) FreelyCancelled(Context context)
    {
        var booking = context.GivenApproved();
        context.GivenDealerFor(booking);
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        Assert.True(payment.Apply(Money.Jod(booking.Pricing.DepositAmount.Amount), Now, Now).IsSuccess);
        Assert.True(booking.ConfirmDepositPaid(payment.Id, Now).IsSuccess);
        Assert.True(booking.Cancel(BookingParty.Customer, CustomerId, null, Now).IsSuccess);
        BookingEndingRefunds.Record(booking, payment, Now);
        context.GivenReference(payment);
        context.Payments.ListStaleLiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        context.GivenOwing(payment);
        return (booking, payment);
    }

    private static ProviderEvent RefundEvent(
        ProviderEventKind kind,
        string eventId,
        string? failureCode = null,
        string? refundReference = "ref_1") =>
        new(eventId, "sess_1", kind, null, failureCode, Now, refundReference);

    private static void Deliver(Context context, ProviderEvent notification) =>
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(notification));

    private static Task<UnitResult<Error>> Receive(Context context) =>
        context.Receive().Handle(new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None);

    [Fact]
    public async Task The_sweep_sends_a_free_cancellation_refund_under_its_own_id()
    {
        var context = new Context();
        var (_, payment) = FreelyCancelled(context);
        var refund = payment.FreeCancellationRefund!;

        var report = await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Assert.Equal(1, report.Value.RefundsSent);
        Assert.Same(RefundStatus.Sent, refund.Status);
        // The refund's own id is the idempotency key, and it goes against the ORIGINAL payment's reference.
        await context.Provider.Received(1).RefundAsync(
            Arg.Is<RefundRequest>(request =>
                request.RefundId == refund.Id &&
                request.PaymentProviderReference == "sess_1" &&
                request.Amount == payment.AmountCaptured),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_settled_free_cancellation_refund_tells_the_customer_once()
    {
        var context = new Context();
        var (booking, payment) = FreelyCancelled(context);
        await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Deliver(context, RefundEvent(ProviderEventKind.RefundSettled, "evt_refund_1"));
        Assert.True((await Receive(context)).IsSuccess);

        Assert.Same(RefundStatus.Settled, payment.FreeCancellationRefund!.Status);
        context.Notifier.Received(1).Raise(Arg.Is<Notification>(notification =>
            notification.RecipientUserId == booking.CustomerId &&
            notification.Kind == NotificationKind.YourDepositRefunded &&
            notification.SubjectReference == booking.Reference.Value));

        // A second, different delivery for the same refund finds nothing still sent, and says nothing.
        context.Notifier.ClearReceivedCalls();
        Deliver(context, RefundEvent(ProviderEventKind.RefundSettled, "evt_refund_2"));
        Assert.True((await Receive(context)).IsSuccess);
        context.Notifier.DidNotReceive().Raise(Arg.Any<Notification>());
        Assert.Same(ProviderEventOutcome.Ignored, context.Recorded.Last().Outcome);
    }

    /// <summary>The same delivery again is refused at the receipt, before the refund is touched.</summary>
    [Fact]
    public async Task A_replayed_refund_event_is_acknowledged_without_acting_again()
    {
        var context = new Context();
        FreelyCancelled(context);
        await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);
        context.Receipts.HasSeenAsync(TestPayments.TestProviderName, "evt_refund_1", Arg.Any<CancellationToken>()).Returns(true);
        context.UnitOfWork.ClearReceivedCalls();

        Deliver(context, RefundEvent(ProviderEventKind.RefundSettled, "evt_refund_1"));
        var result = await Receive(context);

        Assert.True(result.IsSuccess);
        context.Notifier.DidNotReceive().Raise(Arg.Any<Notification>());
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>A refused refund is still owed: it reads Failed, and the next sweep sends it again under the same id.</summary>
    [Fact]
    public async Task A_refused_free_cancellation_refund_is_sent_again_by_the_next_sweep()
    {
        var context = new Context();
        var (_, payment) = FreelyCancelled(context);
        var refund = payment.FreeCancellationRefund!;
        await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Deliver(context, RefundEvent(ProviderEventKind.RefundFailed, "evt_refund_1", "refund_declined"));
        await Receive(context);
        Assert.Same(RefundStatus.Failed, refund.Status);
        Assert.Equal("refund_declined", refund.FailureCode);
        context.Notifier.DidNotReceive().Raise(Arg.Is<Notification>(n => n.Kind == NotificationKind.YourDepositRefunded));

        // Not before its wait (Wave 4, B4): the first refusal waits the policy's first delay.
        context.Provider.ClearReceivedCalls();
        await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);
        await context.Provider.DidNotReceive().RefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>());

        context.Clock.Advance(TestPayments.RetryPolicy.FirstDelay);
        await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Assert.Same(RefundStatus.Sent, refund.Status);
        Assert.Single(payment.Refunds);
        await context.Provider.Received(1).RefundAsync(
            Arg.Is<RefundRequest>(request => request.RefundId == refund.Id), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A late capture for the payment that already paid is the capture it already took, said again: it
    /// creates no second refund beside the free cancellation's, and no incident.
    /// </summary>
    /// <remarks>
    /// Neither side carries a capture reference here, so the money alone decides and the match is only ASSUMED
    /// (Wave 4, B1). Before Wave 4 this notice was refused, failed to orphan, and logged money "UNACCOUNTED FOR".
    /// </remarks>
    [Fact]
    public async Task A_late_capture_for_the_paid_attempt_creates_no_second_refund()
    {
        var context = new Context();
        var (booking, payment) = FreelyCancelled(context);

        Deliver(context, TestPayments.Captured("sess_1", Money.Jod(booking.Pricing.DepositAmount.Amount), Now, "evt_late"));
        Assert.True((await Receive(context)).IsSuccess);

        Assert.Single(payment.Refunds);
        Assert.Same(PaymentStatus.Applied, payment.Status);
        Assert.Same(ProviderEventOutcome.AssumedDuplicate, Assert.Single(context.Recorded).Outcome);
        Assert.Empty(context.Raised);
        Assert.False(context.ReceiveLog.Logged(2304));
    }

    /// <summary>A gallery that has since left the platform does not cost the customer the news that their money is back.</summary>
    [Fact]
    public async Task A_settled_refund_still_tells_the_customer_when_the_gallery_is_gone()
    {
        var context = new Context();
        var (booking, _) = FreelyCancelled(context);
        context.Dealers.GetByIdAsync(booking.DealerId, Arg.Any<CancellationToken>()).Returns((Dealer?)null);
        await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Deliver(context, RefundEvent(ProviderEventKind.RefundSettled, "evt_refund_gone"));
        Assert.True((await Receive(context)).IsSuccess);

        context.Notifier.Received(1).Raise(Arg.Is<Notification>(notification =>
            notification.RecipientUserId == booking.CustomerId &&
            notification.Kind == NotificationKind.YourDepositRefunded));
    }

    // ---------------------------------------------------------------- who made the refund (Wave 2 C6; E2E F48)

    /// <summary>The refund an administrator's dispute decision ordered is Khadra's: the office did not make it.</summary>
    [Fact]
    public async Task A_refund_a_dispute_decision_ordered_is_announced_as_khadras()
    {
        var context = new Context();
        var (booking, payment) = Build.PaidBooking(customerId: CustomerId);
        context.Bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        context.GivenDealerFor(booking);
        var refund = payment.RequestRefund(Money.Jod(booking.Pricing.DepositAmount.Amount), Id.New(), Now).Value;
        Assert.Same(RefundReason.DisputeResolution, refund.Reason);
        refund.MarkSent("rf_dispute", Now);
        context.GivenReference(payment);

        Deliver(context, RefundEventFor(payment, ProviderEventKind.RefundSettled, "evt_dispute", "rf_dispute"));
        Assert.True((await Receive(context)).IsSuccess);

        context.Notifier.Received(1).Raise(Arg.Is<Notification>(notification =>
            notification.RecipientUserId == booking.CustomerId &&
            notification.Kind == NotificationKind.YourDepositRefunded &&
            notification.ActorName == Notification.PlatformActorName &&
            notification.ActorUserId == null &&
            notification.IsFromPlatform));
    }

    /// <summary>An administrator's cancellation is Khadra's act too, and so is the refund it orders.</summary>
    [Fact]
    public async Task A_refund_an_administrators_cancellation_ordered_is_announced_as_khadras()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        context.GivenDealerFor(booking);
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        Assert.True(payment.Apply(Money.Jod(booking.Pricing.DepositAmount.Amount), Now, Now).IsSuccess);
        Assert.True(booking.ConfirmDepositPaid(payment.Id, Now).IsSuccess);
        Assert.True(booking.Cancel(BookingParty.Admin, Id.New(), "Office closed.", Now).IsSuccess);
        BookingEndingRefunds.Record(booking, payment, Now);
        var refund = Assert.Single(payment.Refunds);
        Assert.Same(RefundReason.PlatformCancellation, refund.Reason);
        refund.MarkSent("rf_platform", Now);
        context.GivenReference(payment);

        Deliver(context, RefundEventFor(payment, ProviderEventKind.RefundSettled, "evt_platform", "rf_platform"));
        Assert.True((await Receive(context)).IsSuccess);

        context.Notifier.Received(1).Raise(Arg.Is<Notification>(notification =>
            notification.Kind == NotificationKind.YourDepositRefunded && notification.IsFromPlatform));
    }

    /// <summary>A refund the office's own booking rules made keeps the office's name, as it always did.</summary>
    [Fact]
    public async Task A_free_cancellation_refund_keeps_the_offices_name()
    {
        var context = new Context();
        var (booking, _) = FreelyCancelled(context);
        await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Deliver(context, RefundEvent(ProviderEventKind.RefundSettled, "evt_refund_office"));
        Assert.True((await Receive(context)).IsSuccess);

        context.Notifier.Received(1).Raise(Arg.Is<Notification>(notification =>
            notification.RecipientUserId == booking.CustomerId &&
            !notification.IsFromPlatform &&
            notification.ActorName != Notification.PlatformActorName));
    }

    // ---------------------------------------------------------------- deposit or full payment (2026-09-24)

    private static IBusinessRulesProvider RulesWithFee(decimal percent, string basis = "FullAmount")
    {
        var rules = Substitute.For<IBusinessRulesProvider>();
        rules.GetAsync(Arg.Any<CancellationToken>())
            .Returns(TestBusinessRules.Values() with { ProcessingFee = new ProcessingFeeRules(true, percent, basis, true) });
        return rules;
    }

    private static Payment PendingFull(Booking booking, Money charged, Money? fee = null)
    {
        var payment = Payment.Open(
            booking.Id, booking.CustomerId, charged, TestPayments.TestProviderName, Now.AddMinutes(30), Now,
            PaymentPurpose.FullPayment, fee);
        payment.AttachProviderSession("sess_1", "https://provider.test/sess_1");
        return payment;
    }

    [Fact]
    public async Task Choosing_full_payment_opens_a_checkout_for_the_whole_booking()
    {
        var context = new Context();
        var booking = context.GivenApproved();

        var result = await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id, PaymentPurpose.FullPayment), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var payment = Assert.Single(context.Added);
        Assert.Same(PaymentPurpose.FullPayment, payment.Purpose);
        Assert.Equal(booking.Pricing.TotalPrice.Amount, payment.Amount.Amount);
        Assert.Equal("FullPayment", result.Value.Purpose);
        // The provider was asked for exactly the row's amount, never a figure from the request.
        await context.Provider.Received(1).CreateCheckoutAsync(
            Arg.Is<CheckoutRequest>(request => request.Amount == payment.Amount), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task With_the_fee_on_the_full_payment_charges_it_on_top_and_the_deposit_does_not()
    {
        var context = new Context { Rules = RulesWithFee(1.5m) };
        var booking = context.GivenApproved();

        await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id, PaymentPurpose.FullPayment), CancellationToken.None);
        var full = Assert.Single(context.Added);

        var total = booking.Pricing.TotalPrice.Amount;
        var fee = Math.Round(total * 1.5m / 100m, 3, MidpointRounding.ToEven);
        Assert.Equal(fee, full.ProcessingFee.Amount);
        Assert.Equal(total + fee, full.Amount.Amount);
        Assert.Equal(total, full.AppliedToBooking.Amount);

        var depositContext = new Context { Rules = RulesWithFee(1.5m) };
        var depositBooking = depositContext.GivenApproved();
        await depositContext.Open().Handle(new OpenDepositCheckoutCommand(CustomerId, depositBooking.Id), CancellationToken.None);
        Assert.Equal(0m, Assert.Single(depositContext.Added).ProcessingFee.Amount);
    }

    [Fact]
    public async Task Switching_between_deposit_and_full_payment_retires_the_open_checkout()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        await context.Open().Handle(new OpenDepositCheckoutCommand(CustomerId, booking.Id), CancellationToken.None);
        var deposit = context.Added[0];
        context.GivenLive(deposit);

        var full = await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id, PaymentPurpose.FullPayment), CancellationToken.None);

        Assert.True(full.IsSuccess);
        Assert.Same(PaymentStatus.Failed, deposit.Status);
        Assert.Equal("superseded", deposit.FailureCode);
        Assert.Equal(2, context.Added.Count);
        Assert.Same(PaymentPurpose.FullPayment, context.Added[1].Purpose);
    }

    [Fact]
    public async Task A_fee_changed_while_a_full_payment_is_open_retires_it_rather_than_reusing_it()
    {
        var context = new Context { Rules = RulesWithFee(1.5m) };
        var booking = context.GivenApproved();
        await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id, PaymentPurpose.FullPayment), CancellationToken.None);
        var first = context.Added[0];
        context.GivenLive(first);

        context.Rules = RulesWithFee(2m);
        await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id, PaymentPurpose.FullPayment), CancellationToken.None);

        // The open session was priced under the old fee: it is retired, never charged at a figure the
        // customer was not shown.
        Assert.Same(PaymentStatus.Failed, first.Status);
        Assert.Equal(2, context.Added.Count);
        Assert.NotEqual(first.Amount, context.Added[1].Amount);
    }

    [Fact]
    public async Task The_fee_refundability_in_force_is_frozen_on_the_payment()
    {
        var rules = Substitute.For<IBusinessRulesProvider>();
        rules.GetAsync(Arg.Any<CancellationToken>())
            .Returns(TestBusinessRules.Values() with { ProcessingFee = new ProcessingFeeRules(true, 1.5m, "FullAmount", false) });
        var context = new Context { Rules = rules };
        var booking = context.GivenApproved();

        await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id, PaymentPurpose.FullPayment), CancellationToken.None);

        Assert.False(Assert.Single(context.Added).FeeRefundable);
    }

    [Fact]
    public async Task The_remaining_balance_cannot_be_paid_online_in_this_release()
    {
        var context = new Context();
        var booking = context.GivenApproved();

        var result = await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id, PaymentPurpose.RemainingBalance), CancellationToken.None);

        Assert.Equal("payments.purpose_unavailable", result.Error.Code);
        Assert.Empty(context.Added);
    }

    [Fact]
    public async Task A_full_payment_capture_confirms_the_booking_and_leaves_nothing_owed()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        context.GivenDealerFor(booking);
        var total = Money.Jod(booking.Pricing.TotalPrice.Amount);
        var payment = PendingFull(booking, total);
        context.GivenReference(payment);
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(TestPayments.Captured("sess_1", total, Now)));

        var result = await context.Receive().Handle(
            new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(total, booking.OnlinePaid);
        Assert.Equal(0m, booking.RemainingBalance.Amount);
        Assert.Same(PaymentStatus.Applied, payment.Status);
    }

    [Fact]
    public async Task A_capture_with_a_fee_records_only_the_booking_part_as_paid()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        context.GivenDealerFor(booking);
        var total = booking.Pricing.TotalPrice.Amount;
        var charged = Money.Jod(total + 1.35m);
        var payment = PendingFull(booking, charged, Money.Jod(1.35m));
        context.GivenReference(payment);
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(TestPayments.Captured("sess_1", charged, Now)));

        await context.Receive().Handle(new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None);

        Assert.Equal(total, booking.OnlinePaid.Amount);
        Assert.Equal(0m, booking.RemainingBalance.Amount);
    }

    [Fact]
    public async Task A_full_payment_that_arrives_after_the_deposit_confirmed_is_orphaned_and_refunded_whole()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        context.GivenDealerFor(booking);
        booking.ConfirmDepositPaid(Id.New(), Now);
        var total = Money.Jod(booking.Pricing.TotalPrice.Amount);
        var late = PendingFull(booking, total);
        context.GivenReference(late);
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(TestPayments.Captured("sess_1", total, Now)));

        await context.Receive().Handle(new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None);

        Assert.Same(PaymentStatus.Orphaned, late.Status);
        Assert.Equal("AlreadyPaidByAnotherAttempt", late.OrphanReason);
        Assert.Equal(total, Assert.Single(late.Refunds).Amount);
        // The booking still records only the deposit that confirmed it.
        Assert.Equal(booking.Pricing.DepositAmount.Amount, booking.OnlinePaid.Amount);
    }

    [Fact]
    public async Task A_declined_full_payment_leaves_the_booking_approved_and_payable()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        var payment = PendingFull(booking, Money.Jod(booking.Pricing.TotalPrice.Amount));
        context.GivenReference(payment);
        context.Provider.ParseEvent(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(Result.Success<ProviderEvent, Error>(TestPayments.Failed("sess_1", Now)));

        await context.Receive().Handle(new ReceiveProviderEventCommand("{}", new Dictionary<string, string>()), CancellationToken.None);

        Assert.Same(PaymentStatus.Failed, payment.Status);
        Assert.Same(BookingStatus.Approved, booking.Status);
        Assert.True(booking.IsAwaitingPayment(Now));
        // And the customer may try again, with either option.
        var retry = await context.Open().Handle(
            new OpenDepositCheckoutCommand(CustomerId, booking.Id, PaymentPurpose.FullPayment), CancellationToken.None);
        Assert.True(retry.IsSuccess);
    }

    // ---------------------------------------------------------------- refunds named by the provider (Phase 3, 2026-09-26)

    /// <summary>
    /// A booking paid in full (90 and a 4.5 fee) that the GALLERY cancelled after the free window: the
    /// money above the deposit with the fee (76.5) went back at once, and the deposit (18) was released
    /// when the dispute window closed. Both refunds are out with the provider.
    /// </summary>
    private static (Booking Booking, Payment Payment, Refund Above, Refund Deposit) TwoRefundsOut(Context context)
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m, customerId: CustomerId);
        context.Bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        context.GivenDealerFor(booking);
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "The car failed its inspection.", Now.AddHours(3)).IsSuccess);

        var above = BookingEndingRefunds.Record(booking, payment, Now.AddHours(3))!;
        var deposit = payment.RefundHeldDeposit(Money.Jod(booking.Pricing.DepositAmount.Amount), Now.AddDays(3)).Value!;
        above.MarkSent("rf_above", Now.AddHours(3));
        deposit.MarkSent("rf_deposit", Now.AddDays(3));
        context.GivenReference(payment);
        return (booking, payment, above, deposit);
    }

    private static ProviderEvent RefundEventFor(
        Payment payment,
        ProviderEventKind kind,
        string eventId,
        string? refundReference,
        Money? amount = null,
        string? failureCode = null) =>
        new(eventId, payment.ProviderReference!, kind, amount, failureCode, Now.AddDays(3), refundReference);

    /// <summary>
    /// Pre-launch item 159: with two refunds in flight, "the first one still sent" is the wrong one
    /// half the time. Each event settles exactly the refund it names, and the customer is told "part
    /// of your payment" until the last of it is back, then "your payment has been refunded".
    /// </summary>
    [Fact]
    public async Task Each_refund_event_settles_exactly_the_refund_it_names()
    {
        var context = new Context();
        var (booking, payment, above, deposit) = TwoRefundsOut(context);

        Deliver(context, RefundEventFor(payment, ProviderEventKind.RefundSettled, "evt_deposit", "rf_deposit"));
        Assert.True((await Receive(context)).IsSuccess);

        Assert.Same(RefundStatus.Settled, deposit.Status);
        Assert.Same(RefundStatus.Sent, above.Status);
        Assert.Same(ProviderEventOutcome.Acted, context.Recorded.Last().Outcome);
        context.Notifier.Received(1).Raise(Arg.Is<Notification>(notification =>
            notification.RecipientUserId == booking.CustomerId &&
            notification.Kind == NotificationKind.YourPartialRefundSettled));
        context.Notifier.DidNotReceive().Raise(Arg.Is<Notification>(n => n.Kind == NotificationKind.YourDepositRefunded));

        context.Notifier.ClearReceivedCalls();
        Deliver(context, RefundEventFor(payment, ProviderEventKind.RefundSettled, "evt_above", "rf_above"));
        Assert.True((await Receive(context)).IsSuccess);

        Assert.Same(RefundStatus.Settled, above.Status);
        Assert.True(payment.IsRefundedInFull);
        context.Notifier.Received(1).Raise(Arg.Is<Notification>(notification =>
            notification.RecipientUserId == booking.CustomerId &&
            notification.Kind == NotificationKind.YourDepositRefunded));
    }

    [Fact]
    public async Task A_refund_event_naming_a_refund_this_payment_never_sent_is_recorded_unmatched_and_moves_nothing()
    {
        var context = new Context();
        var (_, payment, above, deposit) = TwoRefundsOut(context);

        Deliver(context, RefundEventFor(payment, ProviderEventKind.RefundSettled, "evt_stranger", "rf_somebody_else"));
        Assert.True((await Receive(context)).IsSuccess);

        Assert.Same(ProviderEventOutcome.Unmatched, context.Recorded.Last().Outcome);
        Assert.Same(RefundStatus.Sent, above.Status);
        Assert.Same(RefundStatus.Sent, deposit.Status);
        context.Notifier.DidNotReceive().Raise(Arg.Any<Notification>());
    }

    /// <summary>
    /// A provider that cannot name refunds is matched only when the event cannot be anyone else's:
    /// exactly one refund out for exactly its amount.
    /// </summary>
    [Fact]
    public async Task An_unnamed_refund_event_settles_only_the_one_refund_its_amount_can_mean()
    {
        var context = new Context();
        var (_, payment, above, deposit) = TwoRefundsOut(context);

        Deliver(context, RefundEventFor(payment, ProviderEventKind.RefundSettled, "evt_unnamed_1", null, Money.Jod(18m)));
        Assert.True((await Receive(context)).IsSuccess);
        Assert.Same(RefundStatus.Settled, deposit.Status);
        Assert.Same(RefundStatus.Sent, above.Status);

        Deliver(context, RefundEventFor(payment, ProviderEventKind.RefundSettled, "evt_unnamed_2", null, Money.Jod(999m)));
        Assert.True((await Receive(context)).IsSuccess);
        Assert.Same(ProviderEventOutcome.Unmatched, context.Recorded.Last().Outcome);
        Assert.Same(RefundStatus.Sent, above.Status);

        // No amount at all is never enough to choose.
        Deliver(context, RefundEventFor(payment, ProviderEventKind.RefundSettled, "evt_unnamed_3", null));
        Assert.True((await Receive(context)).IsSuccess);
        Assert.Same(ProviderEventOutcome.Unmatched, context.Recorded.Last().Outcome);
        Assert.Same(RefundStatus.Sent, above.Status);
    }

    [Fact]
    public async Task An_unnamed_refund_event_that_two_refunds_could_answer_is_never_guessed()
    {
        var context = new Context();
        var (booking, payment) = Build.PaidBooking(inFull: true, customerId: CustomerId);
        context.Bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        var first = payment.RequestRefund(Money.Jod(9m), Id.New(), Now).Value;
        var second = payment.RequestRefund(Money.Jod(9m), Id.New(), Now).Value;
        first.MarkSent("rf_first", Now);
        second.MarkSent("rf_second", Now);
        context.GivenReference(payment);

        Deliver(context, RefundEventFor(payment, ProviderEventKind.RefundSettled, "evt_ambiguous", null, Money.Jod(9m)));
        Assert.True((await Receive(context)).IsSuccess);

        Assert.Same(ProviderEventOutcome.Unmatched, context.Recorded.Last().Outcome);
        Assert.Same(RefundStatus.Sent, first.Status);
        Assert.Same(RefundStatus.Sent, second.Status);
    }

    [Fact]
    public async Task A_named_refusal_fails_only_that_refund_and_the_next_sweep_sends_it_again()
    {
        var context = new Context();
        var (_, payment, above, deposit) = TwoRefundsOut(context);

        Deliver(context, RefundEventFor(payment, ProviderEventKind.RefundFailed, "evt_refused", "rf_above", failureCode: "refund_declined"));
        Assert.True((await Receive(context)).IsSuccess);

        Assert.Same(RefundStatus.Failed, above.Status);
        Assert.Equal("refund_declined", above.FailureCode);
        Assert.Same(RefundStatus.Sent, deposit.Status);
        context.Notifier.DidNotReceive().Raise(Arg.Any<Notification>());

        context.Payments.ListStaleLiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        context.GivenOwing(payment);
        context.Clock.Advance(TestPayments.RetryPolicy.FirstDelay);
        await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Assert.Same(RefundStatus.Sent, above.Status);
        await context.Provider.Received(1).RefundAsync(
            Arg.Is<RefundRequest>(request => request.RefundId == above.Id && request.Amount == above.Amount),
            Arg.Any<CancellationToken>());
        await context.Provider.DidNotReceive().RefundAsync(
            Arg.Is<RefundRequest>(request => request.RefundId == deposit.Id), Arg.Any<CancellationToken>());
    }

    /// <summary>A settlement the platform already recorded is acknowledged, and nobody is told twice.</summary>
    [Fact]
    public async Task A_second_settlement_for_a_settled_refund_is_ignored_and_tells_nobody()
    {
        var context = new Context();
        var (_, payment, _, deposit) = TwoRefundsOut(context);
        deposit.MarkSettled(Now.AddDays(3));

        Deliver(context, RefundEventFor(payment, ProviderEventKind.RefundSettled, "evt_again", "rf_deposit"));
        Assert.True((await Receive(context)).IsSuccess);

        Assert.Same(ProviderEventOutcome.Ignored, context.Recorded.Last().Outcome);
        context.Notifier.DidNotReceive().Raise(Arg.Any<Notification>());
    }

    /// <summary>
    /// Every settled refund now tells the customer (Phase 3), an orphaned capture's included: the
    /// money came back whole, so it is "your payment has been refunded".
    /// </summary>
    [Fact]
    public async Task A_settled_orphan_refund_tells_the_customer_their_payment_was_refunded()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        context.GivenDealerFor(booking);
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        Assert.True(payment.Orphan(Money.Jod(booking.Pricing.DepositAmount.Amount), Now, "BookingExpired", Now).IsSuccess);
        Assert.Single(payment.Refunds).MarkSent("rf_orphan", Now);
        context.GivenReference(payment);

        Deliver(context, RefundEventFor(payment, ProviderEventKind.RefundSettled, "evt_orphan", "rf_orphan"));
        Assert.True((await Receive(context)).IsSuccess);

        context.Notifier.Received(1).Raise(Arg.Is<Notification>(notification =>
            notification.RecipientUserId == booking.CustomerId &&
            notification.Kind == NotificationKind.YourDepositRefunded));
    }

    /// <summary>
    /// The safety net under the ending refunds: a payment in full whose booking ended before pickup
    /// with no refund recorded is named in the log every sweep, and nothing is recorded FOR it — a
    /// sweep that wrote refunds would be a second writer of money owed.
    /// </summary>
    [Fact]
    public async Task The_sweep_names_a_payment_in_full_whose_ending_recorded_no_refund_and_records_nothing()
    {
        var context = new Context { Provider = TestPayments.NoProvider() };
        var (_, payment) = Build.PaidBooking(inFull: true, customerId: CustomerId);
        context.Payments.ListEndedWithoutEndingRefundAsync(Arg.Any<CancellationToken>()).Returns([payment]);
        context.Payments.ListWithOutstandingRefundsAsync(Arg.Any<CancellationToken>()).Returns([]);

        await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Assert.True(context.SweepLog.Logged(2316));
        Assert.Contains(payment.Id.Value.ToString(), context.SweepLog.AllText, StringComparison.Ordinal);
        Assert.Empty(payment.Refunds);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------- a capture notice for money already taken (Wave 4, B1; E2E F30)

    /// <summary>
    /// A booking whose deposit this handler has already applied, under <paramref name="captureReference"/>. What the
    /// first capture recorded, raised, logged and saved is cleared, so a test reads only the notice after it.
    /// </summary>
    private static async Task<(Booking Booking, Payment Payment)> PaidAsync(Context context, string? captureReference)
    {
        var booking = context.GivenApproved();
        context.GivenDealerFor(booking);
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        context.GivenReference(payment);
        Deliver(context, TestPayments.Captured(
            "sess_1", Money.Jod(booking.Pricing.DepositAmount.Amount), Now, "evt_first", captureReference));
        Assert.True((await Receive(context)).IsSuccess);
        Assert.Same(PaymentStatus.Applied, payment.Status);

        // What the database now answers: this attempt holds its capture's reference.
        if (captureReference is not null)
        {
            context.Payments.GetByCaptureReferenceAsync(
                    TestPayments.TestProviderName, captureReference, Arg.Any<CancellationToken>())
                .Returns(payment);
        }

        context.Recorded.Clear();
        context.Raised.Clear();
        context.ReceiveLog.Entries.Clear();
        context.UnitOfWork.ClearReceivedCalls();
        context.Notifier.ClearReceivedCalls();
        return (booking, payment);
    }

    private static bool LoggedAt(Context context, int eventId, LogLevel level) =>
        context.ReceiveLog.Entries.Exists(entry => entry.Id.Id == eventId && entry.Level == level);

    [Fact]
    public async Task The_first_capture_keeps_its_reference_on_the_payment_and_on_its_receipt()
    {
        var context = new Context();
        var booking = context.GivenApproved();
        context.GivenDealerFor(booking);
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        context.GivenReference(payment);
        Deliver(context, TestPayments.Captured(
            "sess_1", Money.Jod(booking.Pricing.DepositAmount.Amount), Now, "evt_1", " cap_1 "));

        Assert.True((await Receive(context)).IsSuccess);

        Assert.Equal("cap_1", payment.ProviderCaptureReference);
        var receipt = Assert.Single(context.Recorded);
        Assert.Same(ProviderEventOutcome.Acted, receipt.Outcome);
        Assert.Equal("cap_1", receipt.CaptureReference);
        Assert.Empty(context.Raised);
        // Asked once, trimmed, before anything was applied: no other attempt held it.
        await context.Payments.Received(1).GetByCaptureReferenceAsync(
            TestPayments.TestProviderName, "cap_1", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The same capture said again under a new event id, which every provider does. Recorded as a duplicate in its own
    /// save, and nothing else happens: no refund, no incident, no second notification, no change to the payment.
    /// </summary>
    [Fact]
    public async Task The_same_capture_said_again_is_recorded_as_a_duplicate_and_changes_nothing()
    {
        var context = new Context();
        var (booking, payment) = await PaidAsync(context, "cap_1");

        Deliver(context, TestPayments.Captured(
            "sess_1", Money.Jod(booking.Pricing.DepositAmount.Amount), Now, "evt_again", "cap_1"));
        Assert.True((await Receive(context)).IsSuccess);

        var receipt = Assert.Single(context.Recorded);
        Assert.Same(ProviderEventOutcome.Duplicate, receipt.Outcome);
        Assert.Equal(payment.Id, receipt.PaymentId);
        Assert.Equal("cap_1", receipt.CaptureReference);
        Assert.Empty(context.Raised);
        Assert.Empty(payment.Refunds);
        Assert.Same(PaymentStatus.Applied, payment.Status);
        Assert.Same(BookingStatus.Confirmed, booking.Status);
        context.Notifier.DidNotReceive().Raise(Arg.Any<Notification>());
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.True(LoggedAt(context, 2309, LogLevel.Information));
        Assert.False(context.ReceiveLog.Logged(2304));
    }

    /// <summary>The same capture with other money: the provider contradicts itself, and a person has to look.</summary>
    [Fact]
    public async Task The_same_reference_with_other_money_raises_an_amount_mismatch_and_refunds_nothing()
    {
        var context = new Context();
        var (booking, payment) = await PaidAsync(context, "cap_1");
        var deposit = booking.Pricing.DepositAmount.Amount;

        Deliver(context, TestPayments.Captured("sess_1", Money.Jod(deposit + 5m), Now, "evt_more", "cap_1"));
        Assert.True((await Receive(context)).IsSuccess);

        var receipt = Assert.Single(context.Recorded);
        Assert.Same(ProviderEventOutcome.AmountMismatch, receipt.Outcome);
        var incident = Assert.Single(context.Raised);
        Assert.Same(PaymentIncidentKind.AmountMismatch, incident.Kind);
        Assert.Equal(payment.Id, incident.PaymentId);
        Assert.Equal(receipt.Id, incident.ReceiptId);
        Assert.Equal(TestPayments.TestProviderName, incident.Provider);
        Assert.Equal("cap_1", incident.CaptureReference);
        Assert.Equal(Money.Jod(deposit + 5m), incident.Reported);
        Assert.Equal(Money.Jod(deposit), incident.Expected);
        Assert.Null(incident.OtherPaymentId);
        Assert.Equal(Now, incident.DetectedAt);
        Assert.False(incident.IsHandled);

        // The payment keeps what it took, and nothing is owed back by code.
        Assert.Equal(Money.Jod(deposit), payment.AmountCaptured);
        Assert.Empty(payment.Refunds);
        context.Notifier.DidNotReceive().Raise(Arg.Any<Notification>());
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.True(LoggedAt(context, 2318, LogLevel.Error));
    }

    /// <summary>
    /// A different capture on an attempt that already took its money: the card was charged twice. It is an incident,
    /// and it is NEVER refunded automatically — a refund the platform cannot prove is owed is not code's to send.
    /// </summary>
    [Fact]
    public async Task Another_capture_on_a_paid_attempt_is_a_second_capture_and_is_never_refunded_automatically()
    {
        var context = new Context();
        var (booking, payment) = await PaidAsync(context, "cap_1");

        Deliver(context, TestPayments.Captured(
            "sess_1", Money.Jod(booking.Pricing.DepositAmount.Amount), Now, "evt_second", "cap_2"));
        Assert.True((await Receive(context)).IsSuccess);

        var receipt = Assert.Single(context.Recorded);
        Assert.Same(ProviderEventOutcome.SecondCapture, receipt.Outcome);
        Assert.Equal("cap_2", receipt.CaptureReference);
        var incident = Assert.Single(context.Raised);
        Assert.Same(PaymentIncidentKind.SecondCapture, incident.Kind);
        Assert.Equal("cap_2", incident.CaptureReference);
        Assert.Equal(receipt.Id, incident.ReceiptId);

        // The payment keeps its own capture; the second one is recorded, not adopted.
        Assert.Equal("cap_1", payment.ProviderCaptureReference);
        Assert.Empty(payment.Refunds);

        // And the sweep, which sends what is recorded, finds nothing to send for it.
        context.GivenOwing(payment);
        await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);
        await context.Provider.DidNotReceive().RefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>());
        Assert.True(LoggedAt(context, 2318, LogLevel.Error));
    }

    /// <summary>
    /// No reference on one side or the other: the money alone decides, and a match is only ASSUMED to be the same
    /// capture. A warning, because a genuine second charge of the same amount would look exactly like this.
    /// </summary>
    [Fact]
    public async Task With_no_reference_to_compare_the_same_money_is_assumed_the_same_capture_and_warned()
    {
        var context = new Context();
        var (booking, payment) = await PaidAsync(context, captureReference: null);

        Deliver(context, TestPayments.Captured(
            "sess_1", Money.Jod(booking.Pricing.DepositAmount.Amount), Now, "evt_again"));
        Assert.True((await Receive(context)).IsSuccess);

        var receipt = Assert.Single(context.Recorded);
        Assert.Same(ProviderEventOutcome.AssumedDuplicate, receipt.Outcome);
        Assert.Null(receipt.CaptureReference);
        Assert.Empty(context.Raised);
        Assert.Empty(payment.Refunds);
        Assert.True(LoggedAt(context, 2317, LogLevel.Warning));
        // Nobody else can hold a reference the notice does not carry, so nobody was asked.
        await context.Payments.DidNotReceive().GetByCaptureReferenceAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task With_no_reference_to_compare_other_money_is_a_second_capture()
    {
        var context = new Context();
        var (booking, payment) = await PaidAsync(context, captureReference: null);
        var deposit = booking.Pricing.DepositAmount.Amount;

        Deliver(context, TestPayments.Captured("sess_1", Money.Jod(deposit + 1m), Now, "evt_other"));
        Assert.True((await Receive(context)).IsSuccess);

        Assert.Same(ProviderEventOutcome.SecondCapture, Assert.Single(context.Recorded).Outcome);
        var incident = Assert.Single(context.Raised);
        Assert.Same(PaymentIncidentKind.SecondCapture, incident.Kind);
        Assert.Null(incident.CaptureReference);
        Assert.Equal(Money.Jod(deposit + 1m), incident.Reported);
        Assert.Equal(Money.Jod(deposit), incident.Expected);
        Assert.Empty(payment.Refunds);
    }

    /// <summary>
    /// Orphaned money is captured money too. Its repeat notice is the same capture, and the refund the orphaning
    /// recorded stays the only one.
    /// </summary>
    [Fact]
    public async Task An_orphaned_payments_repeat_notice_is_a_duplicate_and_records_no_second_refund()
    {
        var context = new Context();
        var booking = context.GivenApproved(paymentWindow: TimeSpan.FromHours(24));
        booking.ExpireUnpaid(booking.PaymentDeadline!.Value);
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        context.GivenReference(payment);
        var deposit = Money.Jod(booking.Pricing.DepositAmount.Amount);

        Deliver(context, TestPayments.Captured("sess_1", deposit, Now, "evt_first", "cap_1"));
        Assert.True((await Receive(context)).IsSuccess);
        Assert.Same(PaymentStatus.Orphaned, payment.Status);
        Assert.Equal("cap_1", payment.ProviderCaptureReference);
        var refund = Assert.Single(payment.Refunds);
        context.Payments.GetByCaptureReferenceAsync(TestPayments.TestProviderName, "cap_1", Arg.Any<CancellationToken>())
            .Returns(payment);
        context.Recorded.Clear();

        Deliver(context, TestPayments.Captured("sess_1", deposit, Now, "evt_again", "cap_1"));
        Assert.True((await Receive(context)).IsSuccess);

        Assert.Same(ProviderEventOutcome.Duplicate, Assert.Single(context.Recorded).Outcome);
        Assert.Empty(context.Raised);
        Assert.Same(PaymentStatus.Orphaned, payment.Status);
        Assert.Same(refund, Assert.Single(payment.Refunds));
    }

    /// <summary>
    /// A capture whose reference ANOTHER attempt already holds. Applying it would collide with the index that keeps
    /// one capture on one attempt, and that 5xx would bring the notice back for days; so it is judged first, before
    /// the attempt's own status, and recorded as an incident on the attempt the notice named.
    /// </summary>
    [Fact]
    public async Task A_capture_another_attempt_already_holds_is_an_incident_and_confirms_nothing()
    {
        var context = new Context();
        var (_, holder) = await PaidAsync(context, "cap_1");
        var other = context.GivenApproved();
        context.GivenDealerFor(other);
        var named = PendingFor(other, other.Pricing.DepositAmount.Amount, "sess_2");
        context.GivenReference(named);

        Deliver(context, TestPayments.Captured(
            "sess_2", Money.Jod(other.Pricing.DepositAmount.Amount), Now, "evt_other_attempt", "cap_1"));
        Assert.True((await Receive(context)).IsSuccess);

        var receipt = Assert.Single(context.Recorded);
        Assert.Same(ProviderEventOutcome.OtherAttempt, receipt.Outcome);
        Assert.Equal(named.Id, receipt.PaymentId);
        Assert.Equal("cap_1", receipt.CaptureReference);

        var incident = Assert.Single(context.Raised);
        Assert.Same(PaymentIncidentKind.CaptureOnAnotherAttempt, incident.Kind);
        Assert.Equal(named.Id, incident.PaymentId);
        Assert.Equal(holder.Id, incident.OtherPaymentId);
        Assert.Equal(receipt.Id, incident.ReceiptId);
        Assert.Equal(named.Amount, incident.Expected);

        // Nothing applied, orphaned or refunded on either attempt, and the named booking is not confirmed.
        Assert.Same(PaymentStatus.Pending, named.Status);
        Assert.Null(named.ProviderCaptureReference);
        Assert.Empty(named.Refunds);
        Assert.Same(BookingStatus.Approved, other.Status);
        Assert.Null(other.DepositPaymentId);
        Assert.Same(PaymentStatus.Applied, holder.Status);
        Assert.Empty(holder.Refunds);
        context.Notifier.DidNotReceive().Raise(Arg.Any<Notification>());
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.True(LoggedAt(context, 2319, LogLevel.Error));
    }

    /// <summary>
    /// The race the index settles: two attempts claim one capture at the same instant, both pass the read, and this
    /// one loses at the save. It is answered 2xx with a receipt and an incident, read afresh after the tracker is
    /// discarded — never the 5xx the uncaught index would produce, which the provider would redeliver for days.
    /// </summary>
    /// <remarks>
    /// The substitutes here hand back the same in-memory objects the refused save mutated, so this proves the
    /// handler's DECISIONS. That the refused save leaves the database untouched and the second save commits a receipt
    /// and an incident is proved against PostgreSQL in <c>PostgresCaptureReferenceRaceTests</c>.
    /// </remarks>
    [Fact]
    public async Task Losing_the_race_for_one_capture_is_recorded_as_a_capture_on_another_attempt()
    {
        var context = new Context();
        var winnerBooking = context.GivenApproved();
        var winner = PendingFor(winnerBooking, winnerBooking.Pricing.DepositAmount.Amount, "sess_1");
        Assert.True(winner.Apply(Money.Jod(winnerBooking.Pricing.DepositAmount.Amount), Now, Now, "cap_1").IsSuccess);

        var booking = context.GivenApproved();
        context.GivenDealerFor(booking);
        var loser = PendingFor(booking, booking.Pricing.DepositAmount.Amount, "sess_2");
        context.GivenReference(loser);
        Deliver(context, TestPayments.Captured(
            "sess_2", Money.Jod(booking.Pricing.DepositAmount.Amount), Now, "evt_loser", "cap_1"));

        // The read before the save finds nobody holding the reference; by the save, the winner has committed.
        context.Payments.GetByCaptureReferenceAsync(TestPayments.TestProviderName, "cap_1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Payment?>(null), Task.FromResult<Payment?>(winner));
        var saves = 0;
        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (++saves == 1)
                {
                    throw new UniqueConstraintConflictException(
                        "lost the race", UniqueConstraintConflictException.ProviderCaptureReferenceConstraint, new InvalidOperationException());
                }
                return Task.FromResult(1);
            });

        var result = await Receive(context);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, saves);
        context.UnitOfWork.Received(1).DiscardChanges();

        // The first receipt went with the refused save; the one that commits says what happened.
        var receipt = context.Recorded[^1];
        Assert.Same(ProviderEventOutcome.OtherAttempt, receipt.Outcome);
        Assert.Equal(loser.Id, receipt.PaymentId);
        Assert.Equal("cap_1", receipt.CaptureReference);
        var incident = Assert.Single(context.Raised);
        Assert.Same(PaymentIncidentKind.CaptureOnAnotherAttempt, incident.Kind);
        Assert.Equal(loser.Id, incident.PaymentId);
        Assert.Equal(winner.Id, incident.OtherPaymentId);
        Assert.Equal(receipt.Id, incident.ReceiptId);
        Assert.True(LoggedAt(context, 2319, LogLevel.Error));
    }

    /// <summary>
    /// The same index refusing a save that is NOT that race — the reference resolves to nobody, or to this very
    /// attempt — is not swallowed: it escapes as before, and the provider's redelivery meets a clean context.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_capture_reference_conflict_that_is_not_that_race_still_escapes(bool resolvesToThisAttempt)
    {
        var context = new Context();
        var booking = context.GivenApproved();
        context.GivenDealerFor(booking);
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount);
        context.GivenReference(payment);
        Deliver(context, TestPayments.Captured(
            "sess_1", Money.Jod(booking.Pricing.DepositAmount.Amount), Now, "evt_1", "cap_1"));
        if (resolvesToThisAttempt)
        {
            context.Payments.GetByCaptureReferenceAsync(TestPayments.TestProviderName, "cap_1", Arg.Any<CancellationToken>())
                .Returns(payment);
        }

        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new UniqueConstraintConflictException(
                "not that race", UniqueConstraintConflictException.ProviderCaptureReferenceConstraint, new InvalidOperationException()));

        await Assert.ThrowsAsync<UniqueConstraintConflictException>(() => Receive(context));

        context.UnitOfWork.Received(1).DiscardChanges();
        Assert.Empty(context.Raised);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The incident's own save losing to the receipt index means a redelivery of this very notice recorded it first:
    /// recorded once either way, and answered 2xx.
    /// </summary>
    [Fact]
    public async Task A_lost_race_whose_notice_was_already_recorded_is_acknowledged()
    {
        var context = new Context();
        var winnerBooking = context.GivenApproved();
        var winner = PendingFor(winnerBooking, winnerBooking.Pricing.DepositAmount.Amount, "sess_1");
        Assert.True(winner.Apply(Money.Jod(winnerBooking.Pricing.DepositAmount.Amount), Now, Now, "cap_1").IsSuccess);
        var booking = context.GivenApproved();
        context.GivenDealerFor(booking);
        var loser = PendingFor(booking, booking.Pricing.DepositAmount.Amount, "sess_2");
        context.GivenReference(loser);
        Deliver(context, TestPayments.Captured(
            "sess_2", Money.Jod(booking.Pricing.DepositAmount.Amount), Now, "evt_loser", "cap_1"));
        context.Payments.GetByCaptureReferenceAsync(TestPayments.TestProviderName, "cap_1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Payment?>(null), Task.FromResult<Payment?>(winner));

        var saves = 0;
        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new UniqueConstraintConflictException(
                "refused",
                ++saves == 1
                    ? UniqueConstraintConflictException.ProviderCaptureReferenceConstraint
                    : UniqueConstraintConflictException.ProviderEventReceiptConstraint,
                new InvalidOperationException()));

        Assert.True((await Receive(context)).IsSuccess);
        Assert.Equal(2, saves);
        Assert.True(context.ReceiveLog.Logged(2306));
    }

    // ---------------------------------------------------------------- a refused refund waits (Wave 4, B4; checklist 157)

    /// <summary>An orphaned capture's refund: owed, never sent, with a provider that refuses every send.</summary>
    private static Payment OwedToARefusingProvider(Context context, string reference = "sess_1")
    {
        var booking = context.GivenApproved();
        var payment = PendingFor(booking, booking.Pricing.DepositAmount.Amount, reference);
        Assert.True(payment.Orphan(Money.Jod(booking.Pricing.DepositAmount.Amount), Now, "BookingExpired", Now).IsSuccess);
        context.Payments.ListStaleLiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        context.Provider.RefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ProviderRefund, Error>(PaymentErrors.ProviderRefused));
        return payment;
    }

    private static async Task SweepAt(Context context, TimeSpan after)
    {
        context.Clock.UtcNow = Now + after;
        context.Provider.ClearReceivedCalls();
        await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);
    }

    /// <summary>
    /// The schedule, send by send: 1, 2 and 4 minutes between refusals, nothing sent early, every refused send
    /// counted, and a warning that becomes an error at the third refusal, when a person must look.
    /// </summary>
    [Fact]
    public async Task A_refused_refund_is_sent_again_only_when_its_wait_is_over_and_each_send_is_counted()
    {
        var context = new Context();
        var payment = OwedToARefusingProvider(context);
        context.GivenOwing(payment);
        var refund = Assert.Single(payment.Refunds);

        async Task Sends(TimeSpan after, int expected)
        {
            await SweepAt(context, after);
            await context.Provider.Received(expected).RefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>());
        }

        await Sends(TimeSpan.Zero, 1);
        await Sends(TimeSpan.FromSeconds(59), 0);
        await Sends(TimeSpan.FromMinutes(1), 1);
        await Sends(TimeSpan.FromMinutes(2), 0);
        await Sends(TimeSpan.FromMinutes(3), 1);
        await Sends(TimeSpan.FromMinutes(6), 0);
        await Sends(TimeSpan.FromMinutes(7), 1);

        Assert.Same(RefundStatus.Failed, refund.Status);
        Assert.Equal(4, refund.RefusalCount);
        Assert.Equal(Now.AddMinutes(7), refund.FailedAt);
        Assert.Equal(Now.AddMinutes(15), refund.NextAttemptAt);
        // Never abandoned, and never a second refund: the same refund each time, under its own id.
        Assert.Single(payment.Refunds);
        Assert.Equal(2, context.SweepLog.Entries.Count(entry => entry.Id.Id == 2315 && entry.Level == LogLevel.Warning));
        Assert.Equal(2, context.SweepLog.Entries.Count(entry => entry.Id.Id == 2323 && entry.Level == LogLevel.Error));
    }

    /// <summary>One payment can owe a refund that is due and one still waiting; only the due one is sent.</summary>
    [Fact]
    public async Task Of_two_refunds_on_one_payment_only_the_one_that_is_due_is_sent()
    {
        var context = new Context();
        var (booking, payment) = Build.PaidBooking(inFull: true, customerId: CustomerId);
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "The car failed its inspection.", Now).IsSuccess);
        var above = BookingEndingRefunds.Record(booking, payment, Now)!;
        var deposit = payment.RefundHeldDeposit(Money.Jod(booking.Pricing.DepositAmount.Amount), Now).Value!;
        above.RecordRefusedSend("card_closed", Now, TestPayments.RetryPolicy);
        context.Payments.ListStaleLiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        context.GivenOwing(payment);

        await SweepAt(context, TimeSpan.FromSeconds(30));

        await context.Provider.Received(1).RefundAsync(
            Arg.Is<RefundRequest>(request => request.RefundId == deposit.Id), Arg.Any<CancellationToken>());
        await context.Provider.DidNotReceive().RefundAsync(
            Arg.Is<RefundRequest>(request => request.RefundId == above.Id), Arg.Any<CancellationToken>());
        Assert.Same(RefundStatus.Sent, deposit.Status);
        Assert.Same(RefundStatus.Failed, above.Status);
        Assert.Equal(Now.AddMinutes(1), above.NextAttemptAt);
    }

    /// <summary>
    /// A payment whose refunds changed while they were being sent — the webhook settling one — loses only its own
    /// sends: the next payment is read into a clean tracker and saved.
    /// </summary>
    [Fact]
    public async Task A_conflict_on_one_payment_costs_only_that_payments_sends()
    {
        var context = new Context();
        var first = OwedToARefusingProvider(context, "sess_1");
        var second = OwedToARefusingProvider(context, "sess_2");
        context.Provider.RefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<ProviderRefund, Error>(new ProviderRefund("ref_1")));
        context.GivenOwing(first, second);
        var saves = 0;
        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (++saves == 1)
                    throw new ConcurrencyConflictException();
                return Task.FromResult(1);
            });

        var report = await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Assert.Equal(2, saves);
        await context.Provider.Received(2).RefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>());
        // Only what was saved is reported: the first payment's send is sent again next time, as the same refund.
        Assert.Equal(1, report.Value.RefundsSent);
        Assert.True(context.SweepLog.Logged(2324));
        // A clean tracker before each payment, and again after the conflict.
        context.UnitOfWork.Received(3).DiscardChanges();
    }

    /// <summary>
    /// The provider repeating its notice of one refusal, under a new event id, is recorded and changes nothing: the
    /// refusal is counted once, and its wait does not move.
    /// </summary>
    [Fact]
    public async Task A_refusal_notice_said_again_is_ignored_and_counted_once()
    {
        var context = new Context();
        var (_, payment, above, _) = TwoRefundsOut(context);

        Deliver(context, RefundEventFor(payment, ProviderEventKind.RefundFailed, "evt_refused", "rf_above", failureCode: "refund_declined"));
        Assert.True((await Receive(context)).IsSuccess);
        context.Clock.Advance(TimeSpan.FromSeconds(20));
        Deliver(context, RefundEventFor(payment, ProviderEventKind.RefundFailed, "evt_refused_again", "rf_above", failureCode: "refund_declined"));
        Assert.True((await Receive(context)).IsSuccess);

        Assert.Equal(1, above.RefusalCount);
        Assert.Equal(Now, above.FailedAt);
        Assert.Equal(Now.AddMinutes(1), above.NextAttemptAt);
        Assert.Equal(
            [ProviderEventOutcome.Acted, ProviderEventOutcome.Ignored],
            context.Recorded.Select(receipt => receipt.Outcome));
    }

    /// <summary>
    /// A provider that cannot be reached refused nothing (the advisor's review of B4): the refund is left exactly as it
    /// was — not counted, no wait set — the rest of the tick's sends wait for the next tick, and one line says so. A
    /// ten-minute outage must not put every refund on the work queue as refused three times.
    /// </summary>
    [Fact]
    public async Task An_unreachable_provider_refuses_nothing_and_nothing_is_counted()
    {
        var context = new Context();
        var first = OwedToARefusingProvider(context, "sess_1");
        var second = OwedToARefusingProvider(context, "sess_2");
        context.Provider.RefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ProviderRefund, Error>(PaymentErrors.ProviderUnavailable));
        context.GivenOwing(first, second);

        var report = await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Assert.Equal((0, 0), (report.Value.RefundsSent, report.Value.RefundsFailed));
        Assert.All(first.Refunds.Concat(second.Refunds), refund =>
        {
            Assert.Same(RefundStatus.Requested, refund.Status);
            Assert.Equal(0, refund.RefusalCount);
            Assert.Null(refund.NextAttemptAt);
            Assert.Null(refund.FailedAt);
        });
        // Asked once, and then left alone until the next tick.
        await context.Provider.Received(1).RefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>());
        Assert.Equal(1, context.SweepLog.Entries.Count(entry => entry.Id.Id == 2325 && entry.Level == LogLevel.Warning));
        Assert.False(context.SweepLog.Logged(2315));
        Assert.False(context.SweepLog.Logged(2323));
    }

    /// <summary>
    /// An outage part-way through one payment's sends keeps what was already sent (the advisor's review of the payments
    /// half): the refund the provider took is saved as Sent, and the one it could not be asked about is left exactly as
    /// it was. The outage ends the tick's sends; it never undoes one.
    /// </summary>
    [Fact]
    public async Task An_outage_part_way_through_a_payment_keeps_the_refund_already_sent()
    {
        var context = new Context();
        var (booking, payment) = Build.PaidBooking(inFull: true, customerId: CustomerId);
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "The car failed its inspection.", Now).IsSuccess);
        Assert.NotNull(BookingEndingRefunds.Record(booking, payment, Now));
        Assert.NotNull(payment.RefundHeldDeposit(Money.Jod(booking.Pricing.DepositAmount.Amount), Now).Value);
        context.Payments.ListStaleLiveAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        context.Provider.RefundAsync(Arg.Any<RefundRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<ProviderRefund, Error>(new ProviderRefund("rf_first")),
                Result.Failure<ProviderRefund, Error>(PaymentErrors.ProviderUnavailable));
        context.GivenOwing(payment);

        var report = await context.Sweep().Handle(new SettlePaymentsCommand(), CancellationToken.None);

        Assert.Equal(2, payment.Refunds.Count);
        var sent = Assert.Single(payment.Refunds, refund => refund.Status == RefundStatus.Sent);
        Assert.Equal(0, sent.RefusalCount);
        var untouched = Assert.Single(payment.Refunds, refund => refund.Status == RefundStatus.Requested);
        Assert.Equal(0, untouched.RefusalCount);
        Assert.Null(untouched.NextAttemptAt);
        Assert.Null(untouched.FailedAt);
        // The send that went out is saved with the payment, and reported.
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal(1, report.Value.RefundsSent);
        Assert.Equal(1, context.SweepLog.Entries.Count(entry => entry.Id.Id == 2325 && entry.Level == LogLevel.Warning));
    }
}
