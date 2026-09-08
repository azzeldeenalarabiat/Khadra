using Khadra.Application.Bookings;
using Khadra.Application.Bookings.CreateBooking;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common.Ports;
using Khadra.Application.Notifications;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Fleet;
using Khadra.Domain.Fleet.Repositories;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Domain.Notifications;
using Khadra.Domain.Notifications.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Bookings;

// A customer asking a gallery for a car (spec 5.2), under the order settled on 2026-09-07: nothing
// is paid here, and the request holds the vehicle until the dealer's answer window closes.
//
// The guards are the substance. Every one of them is a way a customer can be told no, and each has
// to be told no for the RIGHT reason -- a booking refused as "not available" when the truth is "you
// have not uploaded a licence" is a customer who cannot act on the answer.
public sealed class CreateBookingTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    private sealed class Context
    {
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IVehicleRepository Vehicles { get; } = Substitute.For<IVehicleRepository>();
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IBookingReader Reader { get; } = Substitute.For<IBookingReader>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IVehicleHoldLock VehicleLock { get; } = Substitute.For<IVehicleHoldLock>();
        public INotifier Notifier { get; } = Substitute.For<INotifier>();
        public TestClock Clock { get; } = new(Now);

        /// <summary>Every notification the handler staged, in the order it staged them.</summary>
        public List<Notification> Notified { get; } = [];

        public User Customer { get; private set; }
        public Dealer Dealer { get; }
        public Vehicle Vehicle { get; }

        /// <summary>Every booking the handler added, in order.</summary>
        public List<Booking> Added { get; } = [];

        public Context()
        {
            Customer = Build.Customer();
            Dealer = Build.ApprovedDealer();
            Vehicle = Listed(Dealer.Id);

            Users.GetByIdAsync(Customer.Id, Arg.Any<CancellationToken>()).Returns(Customer);
            Vehicles.GetByIdAsync(Vehicle.Id, Arg.Any<CancellationToken>()).Returns(Vehicle);
            Dealers.GetByIdAsync(Dealer.Id, Arg.Any<CancellationToken>()).Returns(Dealer);
            Bookings.ListStaleHoldsForVehicleAsync(
                    Arg.Any<Id>(), Arg.Any<DateRange>(), Arg.Any<TimeSpan>(), Arg.Any<DateTimeOffset>(),
                    Arg.Any<CancellationToken>())
                .Returns([]);
            Bookings.AddAsync(Arg.Any<Booking>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask)
                .AndDoes(call => Added.Add(call.Arg<Booking>()));
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            Notifier
                .When(notifier => notifier.RaiseMany(Arg.Any<IEnumerable<Notification>>()))
                .Do(call => Notified.AddRange(call.Arg<IEnumerable<Notification>>()));
            Reader.ContextAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>())
                .Returns(new BookingContext(null, "Petra Rentals", "Rana Sharif", null, null));

            // A substituted IUnitOfWork returns a completed task and never invokes the delegate, so
            // every guard inside the transaction would be silently skipped and every test would
            // "pass" on a handler that wrote nothing. Run the work for real.
            UnitOfWork.ExecuteInTransactionAsync(
                    Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<Func<CancellationToken, Task>>()(call.Arg<CancellationToken>()));
        }

        public void WithCustomer(User customer)
        {
            Customer = customer;
            Users.GetByIdAsync(customer.Id, Arg.Any<CancellationToken>()).Returns(customer);
        }

        /// <summary>Holds on this car that overlap the candidate window and whose clock has run out.</summary>
        public void StaleHolds(params Booking[] stale) =>
            Bookings.ListStaleHoldsForVehicleAsync(
                    Arg.Any<Id>(), Arg.Any<DateRange>(), Arg.Any<TimeSpan>(), Arg.Any<DateTimeOffset>(),
                    Arg.Any<CancellationToken>())
                .Returns(stale);

        /// <summary>The car is already spoken for across the candidate dates.</summary>
        public void CarIsTaken() =>
            Bookings.HasOverlappingBookingAsync(
                    Arg.Any<Id>(), Arg.Any<DateRange>(), Arg.Any<TimeSpan>(), Arg.Any<DateTimeOffset>(),
                    Arg.Any<Id?>(), Arg.Any<CancellationToken>())
                .Returns(true);

        public CreateBookingHandler Handler() => new(
            Users,
            Vehicles,
            Dealers,
            Bookings,
            Reader,
            new BookingPricer(TestBusinessRules.Provider(), TestBusinessRules.Calendar()),
            TestBusinessRules.Provider(),
            TestBusinessRules.Calendar(),
            VehicleLock,
            new DealerTeamNotifier(Notifier, Users),
            UnitOfWork,
            Clock);

        public CreateBookingCommand Command(
            DateTimeOffset? pickupAt = null,
            int days = 3,
            string pickupMethod = "SelfPickup",
            double? latitude = null,
            double? longitude = null,
            Id? vehicleId = null)
        {
            var pickup = pickupAt ?? Now.AddDays(7);
            return new CreateBookingCommand(
                Customer.Id,
                vehicleId ?? Vehicle.Id,
                pickup,
                pickup.AddDays(days),
                pickupMethod,
                latitude,
                longitude);
        }
    }

    private static Vehicle Listed(Id dealerId, bool deliveryEligible = true)
    {
        var vehicle = Build.Vehicle(dealerId, isDeliveryEligible: deliveryEligible);
        vehicle.AddImage("cars/1.jpg", Now);
        vehicle.Publish(dealerCanTrade: true, Now);
        vehicle.ClearDomainEvents();
        return vehicle;
    }

    // ── The happy path ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_request_is_created_holding_the_car_with_nothing_paid()
    {
        var context = new Context();

        var result = await context.Handler().Handle(context.Command(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var booking = Assert.Single(context.Added);
        Assert.Same(BookingStatus.Requested, booking.Status);
        Assert.True(booking.OccupiesVehicle);
        // The dealer's clock is running; the customer's has not started, because nothing is owed
        // until somebody says yes.
        Assert.Equal(Now.AddHours(48), booking.DecisionDeadline);
        Assert.Null(booking.PaymentDeadline);
        Assert.Null(booking.DepositPaymentId);
        Assert.Null(booking.FreeCancellationDeadline);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_booking_freezes_the_price_and_the_rules_it_was_made_under()
    {
        var context = new Context();

        var result = await context.Handler().Handle(context.Command(days: 3), CancellationToken.None);

        var booking = Assert.Single(context.Added);
        // 3 days at the factory's 30 JOD, 20% deposit. The customer sent no figures at all.
        Assert.Equal(3, booking.Pricing.Days);
        Assert.Equal(90m, booking.Pricing.RentalTotal.Amount);
        Assert.Equal(18m, booking.Pricing.DepositAmount.Amount);
        Assert.Equal(20m, booking.Terms.DepositPercent.Value);
        Assert.Equal(TimeSpan.FromHours(48), booking.Terms.AnswerWindow);
        Assert.Equal(TimeSpan.FromHours(24), booking.Terms.PaymentWindow);
        Assert.Equal(TimeSpan.FromHours(2), booking.Terms.TurnaroundBuffer);
        // The owner's 15 MINUTES, in the unit it was decided in. This assertion is the guard on the
        // unit itself: the value used to be configured in hours, and reading 15 through the old
        // TimeSpan.FromHours would freeze a fifteen-HOUR grace onto every booking and quietly put
        // the report out of reach for most of a day.
        Assert.Equal(TimeSpan.FromMinutes(15), booking.Terms.NonDeliveryGrace);
        // And the DTO the customer receives carries the same figures.
        Assert.Equal(90m, result.Value.Pricing.RentalTotal.Amount);
        Assert.Equal("Requested", result.Value.Status);
    }

    [Fact]
    public async Task The_car_is_claimed_before_the_customer_collects_it()
    {
        var context = new Context();
        var pickup = Now.AddDays(7);

        await context.Handler().Handle(context.Command(pickupAt: pickup), CancellationToken.None);

        var booking = Assert.Single(context.Added);
        // The gallery's turnaround gap, on the leading edge only.
        Assert.Equal(pickup.AddHours(-2), booking.HoldStart);
        Assert.Equal(pickup, booking.Period.Start);
    }

    [Fact]
    public async Task A_delivery_booking_carries_the_location_and_the_gallerys_own_fee()
    {
        var context = new Context();
        // Delivery is off until a gallery turns it on, and the fee is ITS fee, not a platform figure.
        context.Dealer.EnableDelivery(30m, Money.Jod(8m), Now);
        var amman = Build.Amman;

        var result = await context.Handler().Handle(
            context.Command(pickupMethod: "Delivery", latitude: amman.Latitude, longitude: amman.Longitude),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var booking = Assert.Single(context.Added);
        Assert.Same(PickupMethod.Delivery, booking.PickupMethod);
        Assert.NotNull(booking.DeliveryLocation);
        // The fee belongs to the dealership performing the delivery, and the booking freezes it.
        Assert.Equal(context.Dealer.Delivery.Fee!.Amount, booking.Pricing.DeliveryFee.Amount);
    }

    // ── Telling the gallery ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// A request is somebody waiting on this dealership with a clock running. Until this existed the
    /// only way to find out was to go and look at the Pending tab.
    /// </summary>
    [Fact]
    public async Task The_whole_dealership_is_told_a_request_arrived()
    {
        var context = new Context();
        var employeeUserId = Id.New();
        context.Dealer.HireEmployee(employeeUserId, canViewReports: false, Now);

        var result = await context.Handler().Handle(context.Command(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(2, context.Notified.Count);
        Assert.Contains(context.Notified, notification => notification.RecipientUserId == context.Dealer.OwnerUserId);
        Assert.Contains(context.Notified, notification => notification.RecipientUserId == employeeUserId);
        Assert.All(
            context.Notified,
            notification => Assert.Same(NotificationKind.BookingRequested, notification.Kind));
    }

    /// <summary>
    /// The customer is never named on a notification row. Spec 7: this table is not deleted from, so
    /// copying a customer's name into it would outlive their account. The dealership sees whose
    /// booking it is on the booking, where closing an account removes it.
    /// </summary>
    [Fact]
    public async Task The_customer_is_not_named_on_the_notification()
    {
        var context = new Context();

        await context.Handler().Handle(context.Command(), CancellationToken.None);

        var notification = Assert.Single(context.Notified);
        Assert.Equal("A customer", notification.ActorName);
        Assert.Null(notification.ActorUserId);
        Assert.NotEqual(context.Customer.Name.Value, notification.ActorName);
    }

    /// <summary>A deactivated employee has no standing at the dealership (spec 4.2).</summary>
    [Fact]
    public async Task A_deactivated_employee_is_not_told()
    {
        var context = new Context();
        var employeeUserId = Id.New();
        var employee = context.Dealer.HireEmployee(employeeUserId, canViewReports: false, Now).Value;
        context.Dealer.DeactivateEmployee(employee.Id, Now);

        await context.Handler().Handle(context.Command(), CancellationToken.None);

        Assert.DoesNotContain(context.Notified, notification => notification.RecipientUserId == employeeUserId);
        // The owner is still told: it is the deactivated employee who has no standing, not the team.
        var only = Assert.Single(context.Notified);
        Assert.Equal(context.Dealer.OwnerUserId, only.RecipientUserId);
    }

    /// <summary>
    /// The alert is staged in the same transaction as the insert, so a refused booking never leaves
    /// a gallery told about a request that does not exist.
    /// </summary>
    [Fact]
    public async Task A_refused_booking_tells_nobody()
    {
        var context = new Context();
        context.CarIsTaken();

        var result = await context.Handler().Handle(context.Command(), CancellationToken.None);

        Assert.Equal("booking.vehicle_unavailable", result.Error.Code);
        Assert.Empty(context.Notified);
    }

    /// <summary>The row points at the booking, so the reader can open it.</summary>
    [Fact]
    public async Task The_notification_carries_the_booking_it_is_about()
    {
        var context = new Context();

        var result = await context.Handler().Handle(context.Command(), CancellationToken.None);

        var notification = Assert.Single(context.Notified);
        Assert.Equal(result.Value.BookingId, notification.SubjectId!.Value.Value);
        Assert.Equal(result.Value.Reference, notification.SubjectReference);
    }

    // ── The longest a rental may run ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_rental_longer_than_the_maximum_is_refused_on_its_billed_days()
    {
        var context = new Context();

        var result = await context.Handler().Handle(
            context.Command(days: 91), CancellationToken.None);

        Assert.Equal("booking.rental_too_long", result.Error.Code);
        Assert.Contains("90 days", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("91", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(context.Added);
    }

    [Fact]
    public async Task A_rental_exactly_at_the_maximum_is_allowed()
    {
        var context = new Context();

        var result = await context.Handler().Handle(
            context.Command(days: 90), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(90, Assert.Single(context.Added).Pricing.Days);
    }

    // ── Who may book at all ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_account_that_no_longer_exists_cannot_book()
    {
        var context = new Context();
        context.Users.GetByIdAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await context.Handler().Handle(context.Command(), CancellationToken.None);

        Assert.Equal("booking.account_cannot_book", result.Error.Code);
        Assert.Empty(context.Added);
    }

    [Fact]
    public async Task A_customer_who_has_not_verified_their_email_cannot_book()
    {
        var context = new Context();
        context.WithCustomer(Build.Customer(emailVerified: false));

        var result = await context.Handler().Handle(context.Command(), CancellationToken.None);

        Assert.Equal("booking.email_not_verified", result.Error.Code);
        Assert.Equal(ErrorKind.Forbidden, result.Error.Kind);
        Assert.Empty(context.Added);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task A_customer_missing_a_licence_or_an_identity_document_cannot_book(bool licence, bool identity)
    {
        var context = new Context();
        context.WithCustomer(Build.Customer(hasLicence: licence, hasIdentity: identity));

        var result = await context.Handler().Handle(context.Command(), CancellationToken.None);

        Assert.Equal("booking.documents_incomplete", result.Error.Code);
        Assert.Empty(context.Added);
    }

    /// <summary>
    /// The documents rule is a CHECKBOX. Nothing can move a document out of PendingReview yet, so a
    /// booking must not require verification — and this pins that, because the day review exists,
    /// somebody will have to decide deliberately whether to tighten it (checklist item 63).
    /// </summary>
    [Fact]
    public async Task Uploaded_is_enough_documents_are_not_verified_yet()
    {
        var context = new Context();

        var result = await context.Handler().Handle(context.Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.All(
            context.Customer.Documents,
            document => Assert.Same(CustomerDocumentStatus.PendingReview, document.Status));
    }

    [Fact]
    public async Task A_suspended_account_cannot_book()
    {
        var context = new Context();
        var suspended = Build.Customer();
        suspended.Suspend("Chargebacks.", Now);
        context.WithCustomer(suspended);

        var result = await context.Handler().Handle(context.Command(), CancellationToken.None);

        Assert.Equal("booking.account_cannot_book", result.Error.Code);
        Assert.Empty(context.Added);
    }

    [Fact]
    public async Task A_dealer_owner_cannot_book_a_car_through_the_customer_endpoint()
    {
        var context = new Context();
        var owner = User.RegisterDealerOwner(
            EmailAddress.Create("owner@petra.jo").Value,
            PhoneNumber.Create("0799999999").Value,
            PersonName.Create("Petra Owner").Value,
            PasswordHash.FromHash("hash"),
            Now.AddYears(-1));
        owner.VerifyEmail(Now);
        context.WithCustomer(owner);

        var result = await context.Handler().Handle(context.Command(), CancellationToken.None);

        Assert.Equal("booking.account_cannot_book", result.Error.Code);
    }

    // ── What may be booked ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unknown_car_is_not_a_car()
    {
        var context = new Context();

        var result = await context.Handler().Handle(
            context.Command(vehicleId: Id.New()), CancellationToken.None);

        Assert.Equal("vehicle.not_found", result.Error.Code);
    }

    /// <summary>
    /// A suspended gallery's car answers exactly as an unknown id does. A distinguishable response
    /// would let anyone walk a competitor's inventory by trying ids.
    /// </summary>
    [Fact]
    public async Task A_car_whose_gallery_cannot_trade_answers_the_same_as_an_unknown_id()
    {
        var context = new Context();
        context.Dealer.Suspend(Id.New(), "Complaints.", Now);

        var result = await context.Handler().Handle(context.Command(), CancellationToken.None);

        Assert.Equal("vehicle.not_found", result.Error.Code);
        Assert.Empty(context.Added);
    }

    [Fact]
    public async Task An_unlisted_car_cannot_be_booked()
    {
        var context = new Context();
        var draft = Build.Vehicle(context.Dealer.Id, plateNumber: "99-99999");
        context.Vehicles.GetByIdAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);

        var result = await context.Handler().Handle(
            context.Command(vehicleId: draft.Id), CancellationToken.None);

        Assert.Equal("vehicle.not_found", result.Error.Code);
    }

    // ── The dates ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_rental_starting_too_soon_is_refused_with_the_figure()
    {
        var context = new Context();

        var result = await context.Handler().Handle(
            context.Command(pickupAt: Now.AddMinutes(90)), CancellationToken.None);

        Assert.Equal("booking.too_soon", result.Error.Code);
        Assert.Contains("2 hours", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(context.Added);
    }

    [Fact]
    public async Task A_rental_exactly_at_the_lead_time_is_accepted()
    {
        var context = new Context();

        var result = await context.Handler().Handle(
            context.Command(pickupAt: Now.AddHours(2)), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
    }

    [Fact]
    public async Task A_rental_beyond_the_booking_horizon_is_refused()
    {
        var context = new Context();

        var result = await context.Handler().Handle(
            context.Command(pickupAt: Now.AddDays(181)), CancellationToken.None);

        Assert.Equal("booking.beyond_horizon", result.Error.Code);
        Assert.Empty(context.Added);
    }

    [Fact]
    public async Task A_rental_in_the_past_is_refused()
    {
        var context = new Context();

        var result = await context.Handler().Handle(
            context.Command(pickupAt: Now.AddDays(-1)), CancellationToken.None);

        Assert.Equal("booking.period_in_past", result.Error.Code);
    }

    [Fact]
    public async Task An_unknown_pickup_method_is_refused_before_anything_is_loaded()
    {
        var context = new Context();

        var result = await context.Handler().Handle(
            context.Command(pickupMethod: "Teleport"), CancellationToken.None);

        Assert.Equal("booking.unknown_pickup_method", result.Error.Code);
        await context.Users.DidNotReceive().GetByIdAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>());
    }

    // ── Delivery ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_delivery_without_a_location_is_refused()
    {
        var context = new Context();
        context.Dealer.EnableDelivery(30m, Money.Jod(8m), Now);

        var result = await context.Handler().Handle(
            context.Command(pickupMethod: "Delivery"), CancellationToken.None);

        Assert.Equal("booking.delivery_location_required", result.Error.Code);
    }

    [Fact]
    public async Task A_self_pickup_carrying_a_location_is_refused()
    {
        var context = new Context();
        var amman = Build.Amman;

        var result = await context.Handler().Handle(
            context.Command(latitude: amman.Latitude, longitude: amman.Longitude),
            CancellationToken.None);

        Assert.Equal("booking.delivery_location_not_allowed", result.Error.Code);
    }

    [Fact]
    public async Task A_delivery_outside_the_gallerys_radius_is_refused()
    {
        var context = new Context();
        context.Dealer.EnableDelivery(30m, Money.Jod(8m), Now);
        // Aqaba is 300km from the Amman gallery.
        var aqaba = Build.Aqaba;

        var result = await context.Handler().Handle(
            context.Command(pickupMethod: "Delivery", latitude: aqaba.Latitude, longitude: aqaba.Longitude),
            CancellationToken.None);

        Assert.Equal("booking.delivery_out_of_range", result.Error.Code);
        Assert.Empty(context.Added);
    }

    [Fact]
    public async Task A_car_the_gallery_will_not_deliver_cannot_be_booked_for_delivery()
    {
        var context = new Context();
        context.Dealer.EnableDelivery(30m, Money.Jod(8m), Now);
        var collectOnly = Listed(context.Dealer.Id, deliveryEligible: false);
        context.Vehicles.GetByIdAsync(collectOnly.Id, Arg.Any<CancellationToken>()).Returns(collectOnly);
        var amman = Build.Amman;

        var result = await context.Handler().Handle(
            context.Command(vehicleId: collectOnly.Id, pickupMethod: "Delivery",
                latitude: amman.Latitude, longitude: amman.Longitude),
            CancellationToken.None);

        Assert.Equal("booking.vehicle_not_delivery_eligible", result.Error.Code);
    }

    // ── The gallery's opening hours ───────────────────────────────────────────────────────────

    /// <summary>
    /// Build.NineToFive opens the test gallery 09:00-17:00, and Build.Now is 13:00 in Amman, so the
    /// default command collects and returns in the middle of the working day. These move the clock
    /// rather than the schedule, which is how a customer would hit it.
    /// </summary>
    [Fact]
    public async Task A_self_pickup_when_the_gallery_is_shut_is_refused()
    {
        var context = new Context();

        var result = await context.Handler().Handle(
            context.Command(pickupAt: new DateTimeOffset(Now.AddDays(7).Date, TimeSpan.Zero)),
            CancellationToken.None);

        Assert.Equal("booking.pickup_outside_opening_hours", result.Error.Code);
        Assert.Contains("09:00-17:00", result.Error.Message, StringComparison.Ordinal);
        Assert.Empty(context.Added);
        Assert.Empty(context.Notified);
    }

    /// <summary>
    /// The owner's rule, and the reason it is not simply "the gallery must be open": a gallery
    /// driving a car out to a customer may do that outside the hours it keeps its counter.
    /// </summary>
    [Fact]
    public async Task A_delivery_at_the_same_hour_is_allowed()
    {
        var context = new Context();
        context.Dealer.EnableDelivery(30m, Money.Jod(8m), Now);
        var amman = Build.Amman;

        var result = await context.Handler().Handle(
            context.Command(
                pickupAt: new DateTimeOffset(Now.AddDays(7).Date, TimeSpan.Zero),
                pickupMethod: "Delivery",
                latitude: amman.Latitude,
                longitude: amman.Longitude),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(PickupMethod.Delivery, Assert.Single(context.Added).PickupMethod);
    }

    /// <summary>
    /// The customer brings the car back to the same counter, so the return is judged too — and
    /// reported separately, so they know which of the two dates to move.
    /// </summary>
    [Fact]
    public async Task A_self_pickup_returning_after_closing_is_refused_on_the_return()
    {
        var context = new Context();
        var pickup = Now.AddDays(7);

        var result = await context.Handler().Handle(
            new CreateBookingCommand(
                context.Customer.Id,
                context.Vehicle.Id,
                pickup,
                // 23:00 Amman three days later.
                new DateTimeOffset(pickup.AddDays(3).Date, TimeSpan.Zero).AddHours(20),
                "SelfPickup",
                null,
                null),
            CancellationToken.None);

        Assert.Equal("booking.return_outside_opening_hours", result.Error.Code);
        Assert.Empty(context.Added);
    }

    // ── Availability, and the race under it ───────────────────────────────────────────────────

    [Fact]
    public async Task A_car_already_held_for_those_dates_is_refused_with_a_code_a_phone_can_use()
    {
        var context = new Context();
        context.CarIsTaken();

        var result = await context.Handler().Handle(context.Command(), CancellationToken.None);

        Assert.Equal("booking.vehicle_unavailable", result.Error.Code);
        Assert.Equal(ErrorKind.Conflict, result.Error.Kind);
        Assert.Empty(context.Added);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The floor under the guard: between asking "is this car free" and inserting, somebody else
    /// booked it, and only the database could see that.
    /// </summary>
    [Fact]
    public async Task Losing_the_race_to_the_exclusion_constraint_reads_as_the_car_being_taken()
    {
        var context = new Context();
        context.UnitOfWork
            .When(unitOfWork => unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new ExclusiveHoldConflictException(
                "Something else already holds what this write was claiming.",
                ExclusiveHoldConflictException.VehicleHoldConstraint,
                new InvalidOperationException("23P01")));

        var result = await context.Handler().Handle(context.Command(), CancellationToken.None);

        Assert.Equal("booking.vehicle_unavailable", result.Error.Code);
    }

    /// <summary>
    /// A different exclusion constraint is not this handler's race to translate. It stays an
    /// exception and becomes a generic 409, which is the honest answer for a conflict nobody here
    /// anticipated.
    /// </summary>
    [Fact]
    public async Task An_unrecognised_exclusion_conflict_is_not_swallowed()
    {
        var context = new Context();
        context.UnitOfWork
            .When(unitOfWork => unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new ExclusiveHoldConflictException(
                "Something else already holds what this write was claiming.",
                "some_other_constraint",
                new InvalidOperationException("23P01")));

        await Assert.ThrowsAsync<ExclusiveHoldConflictException>(
            () => context.Handler().Handle(context.Command(), CancellationToken.None));
    }

    /// <summary>
    /// Losing a row to somebody else's write is NOT the same event as losing the car, and must not
    /// be reported as one.
    /// </summary>
    /// <remarks>
    /// This is the finding that made the handler take a lock. A stale hold can be changed under this
    /// transaction by an administrator, or by the expiry job once it exists, and neither says
    /// anything about whether the car is free for these dates. Answering "no longer free for those
    /// dates" would send a customer away from dates that are fine; the generic conflict, which tells
    /// them to reload and try again, is true.
    /// </remarks>
    [Fact]
    public async Task Losing_a_stale_hold_to_another_writer_is_not_reported_as_the_car_being_taken()
    {
        var context = new Context();
        context.UnitOfWork
            .When(unitOfWork => unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new ConcurrencyConflictException("xmin changed"));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => context.Handler().Handle(context.Command(), CancellationToken.None));
    }

    /// <summary>
    /// The lock is what makes that race rare rather than routine: it serialises every creator of a
    /// booking for one car, so the second one reads a world that has stopped moving.
    /// </summary>
    [Fact]
    public async Task Creating_a_booking_serialises_on_the_vehicle()
    {
        var context = new Context();

        await context.Handler().Handle(context.Command(), CancellationToken.None);

        await context.VehicleLock.Received(1)
            .AcquireAsync(context.Vehicle.Id.Value, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A stale hold the repository handed over must be expirable. If it is not, the query and the
    /// aggregate's guards have drifted apart, and that is a bug to surface rather than swallow --
    /// swallowing it would turn into a 23P01 further down that reads as "the car was taken".
    /// </summary>
    [Fact]
    public async Task A_stale_hold_that_refuses_to_expire_is_a_programming_error()
    {
        var context = new Context();
        // Confirmed: paid for, no clock on it, and nothing the query should ever return.
        var wrong = Build.ConfirmedBooking();
        context.StaleHolds(wrong);

        await Assert.ThrowsAsync<DomainException>(
            () => context.Handler().Handle(context.Command(), CancellationToken.None));
    }

    // ── Stale holds: the reason this handler needs a transaction ───────────────────────────────

    /// <summary>
    /// The exclusion constraint cannot see the clock, so a request past its answer deadline still
    /// occupies its index while the application has stopped counting it. Without this the guard says
    /// the car is free and the INSERT is refused by the database.
    /// </summary>
    [Fact]
    public async Task A_request_nobody_answered_is_expired_before_the_new_one_is_written()
    {
        var context = new Context();
        var stale = Build.Booking(vehicleId: context.Vehicle.Id, now: Now.AddDays(-5));
        context.StaleHolds(stale);

        var result = await context.Handler().Handle(context.Command(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(BookingStatus.Expired, stale.Status);
        Assert.False(stale.OccupiesVehicle);
    }

    [Fact]
    public async Task An_approval_nobody_paid_for_is_expired_before_the_new_one_is_written()
    {
        var context = new Context();
        var stale = Build.Booking(vehicleId: context.Vehicle.Id, now: Now.AddDays(-5));
        stale.Approve(Id.New(), Now.AddDays(-5));
        context.StaleHolds(stale);

        var result = await context.Handler().Handle(context.Command(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(BookingStatus.Expired, stale.Status);
    }

    /// <summary>
    /// The clock ended those bookings, not the customer who happened to arrive next, and the status
    /// history must not name them as though it had.
    /// </summary>
    [Fact]
    public async Task An_expired_stale_hold_is_attributed_to_nobody()
    {
        var context = new Context();
        var stale = Build.Booking(vehicleId: context.Vehicle.Id, now: Now.AddDays(-5));
        context.StaleHolds(stale);

        await context.Handler().Handle(context.Command(), CancellationToken.None);

        var last = stale.StatusHistory.Last();
        Assert.Same(BookingStatus.Expired, last.To);
        Assert.Null(last.ActorUserId);
        Assert.True(stale.Penalty!.IsNothingOwed);
    }

    /// <summary>
    /// A hold whose deadline has NOT passed is somebody's live booking. The repository query is what
    /// decides that, so the handler must not second-guess it — but it must also not expire anything
    /// the query did not hand it.
    /// </summary>
    [Fact]
    public async Task Nothing_is_expired_when_the_vehicle_has_no_stale_holds()
    {
        var context = new Context();

        await context.Handler().Handle(context.Command(), CancellationToken.None);

        // One save for the insert, and none for an expiry that had nothing to do.
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_whole_thing_happens_in_one_transaction()
    {
        var context = new Context();

        await context.Handler().Handle(context.Command(), CancellationToken.None);

        await context.UnitOfWork.Received(1).ExecuteInTransactionAsync(
            Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The order inside the transaction is the point of the whole handler: lock, expire, ask,
    /// insert. Asking before expiring would consult a world the expiry is about to change; expiring
    /// before locking would race another creator over the same rows.
    /// </summary>
    [Fact]
    public async Task The_lock_is_taken_first_then_stale_holds_then_availability()
    {
        var context = new Context();
        var calls = new List<string>();
        context.VehicleLock
            .AcquireAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => { calls.Add("lock"); return Task.CompletedTask; });
        context.Bookings
            .ListStaleHoldsForVehicleAsync(
                Arg.Any<Id>(), Arg.Any<DateRange>(), Arg.Any<TimeSpan>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => { calls.Add("expire"); return []; });
        context.Bookings
            .HasOverlappingBookingAsync(
                Arg.Any<Id>(), Arg.Any<DateRange>(), Arg.Any<TimeSpan>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<Id?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { calls.Add("guard"); return false; });

        await context.Handler().Handle(context.Command(), CancellationToken.None);

        Assert.Equal(["lock", "expire", "guard"], calls);
    }
}
