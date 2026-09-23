using Khadra.Application.Auditing;
using Khadra.Application.Bookings;
using Khadra.Application.Bookings.Handover;
using Khadra.Application.Bookings.DecideBooking;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers;
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
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Security;
using Khadra.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Khadra.Tests.Application.Bookings;

// Spec 4.2: a dealer's staff answer booking requests and record handovers, and every action names
// who did it. The edges held hardest: a deactivated employee can do nothing, another dealer's
// booking does not exist, and a suspended dealer can still take back a car it already let out.
public sealed class BookingDecisionTests
{
    private static readonly Id OwnerId = Id.New();
    private static readonly Id EmployeeId = Id.New();

    private sealed class Context
    {
        public IBookingRepository Bookings { get; } = Substitute.For<IBookingRepository>();
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public IBookingReader Reader { get; } = Substitute.For<IBookingReader>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public INotifier Notifier { get; } = Substitute.For<INotifier>();
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IBookingEmailComposer Composer { get; } = Substitute.For<IBookingEmailComposer>();
        public IEmailSender Sender { get; } = Substitute.For<IEmailSender>();
        public RecordingLogger<BookingEmailDispatcher> EmailLog { get; } = new();
        public TestClock Clock { get; } = new(Build.Now);
        public Dealer Dealer { get; }

        /// <summary>Every message the platform handed the transport, in order.</summary>
        public List<EmailMessage> Sent { get; } = [];

        /// <summary>The token each of those sends was given.</summary>
        public List<CancellationToken> SendTokens { get; } = [];

        public Context()
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            Reader.ContextAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>())
                .Returns(new BookingContext(null, "Al-Nadeem Rentals", false, null, "Layla Odeh", false, null, null));
            Dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
            Dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(Dealer);

            Composer.BookingApproved(
                    Arg.Any<User>(), Arg.Any<BookingDto>(), Arg.Any<BookingContext>(), Arg.Any<string?>())
                .Returns(call => new EmailMessage(
                    call.Arg<User>().Email.Value,
                    call.Arg<User>().Name.Value,
                    $"approved {call.Arg<BookingDto>().Reference}",
                    "<p>html</p>",
                    "text"));
            Sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
                .Returns(TestEmail.Accepted())
                .AndDoes(call =>
                {
                    Sent.Add(call.Arg<EmailMessage>());
                    SendTokens.Add(call.Arg<CancellationToken>());
                });
        }

        /// <summary>The customer the booking belongs to, with an address somebody has proved.</summary>
        public User GivenCustomerFor(Booking booking)
        {
            var customer = Build.Customer();
            typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(customer, booking.CustomerId);
            Users.GetByIdAsync(booking.CustomerId, Arg.Any<CancellationToken>()).Returns(customer);
            return customer;
        }

        public Booking GivenRequested()
        {
            // A request as it now arrives: nothing paid, waiting on the dealer.
            var booking = Build.Booking(dealerId: Dealer.Id);
            booking.ClearDomainEvents();
            Bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
            return booking;
        }

        public Employee GivenEmployee()
        {
            var employee = Dealer.HireEmployee(EmployeeId, canViewReports: false, Build.Now).Value;
            Dealers.GetByStaffUserIdAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(Dealer);
            return employee;
        }

        public IHandoverCodeRepository HandoverCodes { get; } = Substitute.For<IHandoverCodeRepository>();
        public IHandoverSettings HandoverSettings { get; } = Substitute.For<IHandoverSettings>();
        public IAuditTrail AuditTrail { get; } = Substitute.For<IAuditTrail>();
        public ICurrentActor Actor { get; } = Substitute.For<ICurrentActor>();
        public HandoverCodeService CodeService { get; } = new(Options.Create(new JwtOptions { SigningKey = new string('k', 48) }));

        public BookingDecisionHandlers Handlers()
        {
            HandoverSettings.MaxFailedAttempts.Returns(5);
            HandoverSettings.CodeLifetime.Returns(TimeSpan.FromMinutes(15));
            return new(
                Bookings,
                new DealerMembershipResolver(Dealers),
                Reader,
                new DealerTeamNotifier(Notifier, Users),
                new BookingEmailDispatcher(Users, Composer, Sender, EmailLog),
                new HandoverVerifier(HandoverCodes, CodeService, HandoverSettings, new AdminActionRecorder(AuditTrail, Actor, Clock), UnitOfWork, Clock),
                Clock,
                UnitOfWork);
        }

        /// <summary>A code the customer has just been shown, and the stored row it lives as.</summary>
        public (string Code, HandoverCode Row) GivenCodeFor(Booking booking, HandoverType type)
        {
            var code = CodeService.Generate();
            var row = HandoverCode.Issue(booking.Id, type, CodeService.Hash(booking.Id, type, code), Clock.UtcNow, TimeSpan.FromMinutes(15));
            HandoverCodes.GetCurrentAsync(booking.Id, type, Arg.Any<CancellationToken>()).Returns(row);
            return (code, row);
        }
    }

    [Fact]
    public async Task The_owner_approves_a_request_and_the_note_reaches_the_history()
    {
        var context = new Context();
        var booking = context.GivenRequested();

        var result = await context.Handlers().Handle(
            new ApproveBookingCommand(OwnerId, booking.Id, "Collect from the Mecca Street lot; ask for Yousef."),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(BookingStatus.Approved, booking.Status);
        Assert.Equal(OwnerId, booking.ActedByUserId);
        var last = booking.StatusHistory.OrderBy(change => change.OccurredAt).Last();
        Assert.Equal(OwnerId, last.ActorUserId);
        Assert.Equal("Collect from the Mecca Street lot; ask for Yousef.", last.Reason);
        Assert.Equal("Approved", result.Value.Status);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_active_employee_can_approve_and_is_named_as_the_actor()
    {
        var context = new Context();
        context.GivenEmployee();
        var booking = context.GivenRequested();

        var result = await context.Handlers().Handle(
            new ApproveBookingCommand(EmployeeId, booking.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(EmployeeId, booking.ActedByUserId);
    }

    [Fact]
    public async Task A_deactivated_employee_is_told_the_booking_does_not_exist()
    {
        var context = new Context();
        var employee = context.GivenEmployee();
        context.Dealer.DeactivateEmployee(employee.Id, Build.Now);
        var booking = context.GivenRequested();

        var result = await context.Handlers().Handle(
            new ApproveBookingCommand(EmployeeId, booking.Id, null), CancellationToken.None);

        // No standing at all, so the answer is the one a stranger gets.
        Assert.Equal("dealer.not_registered", result.Error.Code);
        Assert.Same(BookingStatus.Requested, booking.Status);
    }

    /// <summary>
    /// The code and the dealer's own words are stored APART.
    /// </summary>
    /// <remarks>
    /// They used to be composed into one English sentence on the way in --
    /// "The dates conflict with another booking: The car is out until the 12th." -- which put
    /// untranslatable prose on a permanent record. An Arabic-speaking customer read English on their
    /// own booking and no client could do anything about it, because the words were in the row.
    /// Keeping the code means the sentence is chosen when the row is READ, in the reader's language.
    /// </remarks>
    [Fact]
    public async Task Rejection_records_the_code_and_the_dealers_words_separately()
    {
        var context = new Context();
        var booking = context.GivenRequested();

        var result = await context.Handlers().Handle(
            new RejectBookingCommand(OwnerId, booking.Id, "DatesConflict", "The car is out until the 12th."),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(BookingStatus.Rejected, booking.Status);

        var last = booking.StatusHistory.OrderBy(change => change.OccurredAt).Last();
        Assert.Equal("DatesConflict", last.ReasonCode);
        Assert.Equal("The car is out until the 12th.", last.Reason);
        Assert.DoesNotContain("The dates conflict", last.Reason, StringComparison.Ordinal);

        // Rejection never costs the customer anything.
        Assert.True(booking.Penalty!.IsNothingOwed);
    }

    [Fact]
    public void An_unlisted_rejection_reason_is_refused_before_the_handler()
    {
        var validator = new RejectBookingCommandValidator();

        var unknown = validator.Validate(new RejectBookingCommand(OwnerId, Id.New(), "Whim", "No."));
        var known = validator.Validate(new RejectBookingCommand(OwnerId, Id.New(), "Other", "No."));

        Assert.False(unknown.IsValid);
        Assert.True(known.IsValid);
    }

    [Fact]
    public async Task Another_dealers_booking_does_not_exist_for_this_owner()
    {
        var context = new Context();
        var foreign = Build.Booking(dealerId: Id.New());
        context.Bookings.GetByIdAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await context.Handlers().Handle(
            new ApproveBookingCommand(OwnerId, foreign.Id, null), CancellationToken.None);

        Assert.Equal("booking.not_found", result.Error.Code);
        Assert.Equal(ErrorKind.NotFound, result.Error.Kind);
    }

    [Fact]
    public async Task A_suspended_dealer_cannot_approve_but_can_still_take_the_car_back()
    {
        var context = new Context();
        var requested = context.GivenRequested();

        // A rental already under way when the sanction lands.
        var outOnRental = Build.ConfirmedBooking(terms: Build.Terms());
        var start = outOnRental.Period.Start;
        outOnRental.RecordPickup(BookingParty.Dealer, OwnerId, start);
        context.Bookings.GetByIdAsync(outOnRental.Id, Arg.Any<CancellationToken>()).Returns(outOnRental);
        // Re-home it under this dealer: the factory picks a random dealer id.
        var mine = Build.Booking(dealerId: context.Dealer.Id, period: Build.Period(Build.Now.AddDays(1), 3));
        mine.Approve(OwnerId, Build.Now);
        mine.ConfirmDepositPaid(Id.New(), Build.Now);
        mine.RecordPickup(BookingParty.Dealer, OwnerId, mine.Period.Start);
        context.Bookings.GetByIdAsync(mine.Id, Arg.Any<CancellationToken>()).Returns(mine);

        context.Dealer.Suspend(Id.New(), "Complaints.", Build.Now);
        context.Clock.UtcNow = mine.Period.End;

        var approve = await context.Handlers().Handle(
            new ApproveBookingCommand(OwnerId, requested.Id, null), CancellationToken.None);
        var giveBack = await context.Handlers().Handle(
            new RecordReturnCommand(OwnerId, mine.Id, 41_500, 0.75m, "Minor scuff, rear bumper.", 240m),
            CancellationToken.None);

        Assert.Equal("booking.actor_cannot_decide", approve.Error.Code);
        Assert.True(giveBack.IsSuccess, giveBack.IsFailure ? giveBack.Error.Code : null);
        Assert.Same(BookingStatus.Returned, mine.Status);
        var handover = mine.Handovers.Single(h => h.Type == HandoverType.Return);
        Assert.Equal(OwnerId, handover.RecordedByUserId);
        Assert.Equal(240m, handover.CashCollected!.Amount);
        Assert.Equal(mine.Pricing.CurrencyCode, handover.CashCollected.CurrencyCode);
    }

    [Fact]
    public async Task Pickup_is_recorded_against_the_person_who_handed_over_the_keys()
    {
        var context = new Context();
        context.GivenEmployee();
        var booking = Build.Booking(dealerId: context.Dealer.Id);
        booking.Approve(OwnerId, Build.Now);
        booking.ConfirmDepositPaid(Id.New(), Build.Now);
        context.Bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        context.Clock.UtcNow = booking.Period.Start;

        var result = await context.Handlers().Handle(
            new RecordPickupCommand(EmployeeId, booking.Id, 41_200, 1m, "Full tank, clean.", null),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(BookingStatus.PickedUp, booking.Status);
        Assert.Equal(EmployeeId, booking.Handovers.Single().RecordedByUserId);
        Assert.Single(result.Value.Handovers);
    }

    // ── Handover verification ──────────────────────────────────────────────────────────────────

    private static Booking ConfirmedFor(Context context)
    {
        var booking = Build.Booking(dealerId: context.Dealer.Id);
        booking.Approve(OwnerId, Build.Now);
        booking.ConfirmDepositPaid(Id.New(), Build.Now);
        booking.ClearDomainEvents();
        context.Bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        context.Clock.UtcNow = booking.Period.Start.AddMinutes(-10);
        return booking;
    }

    [Fact]
    public async Task The_customers_code_proves_the_pickup_is_spent_audited_and_the_customer_is_told()
    {
        var context = new Context();
        var booking = ConfirmedFor(context);
        var (code, row) = context.GivenCodeFor(booking, HandoverType.Pickup);

        var result = await context.Handlers().Handle(
            new RecordPickupCommand(OwnerId, booking.Id, null, null, null, null, HandoverCode: code), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(BookingStatus.PickedUp, booking.Status);
        var handover = booking.Handovers.Single();
        Assert.Same(HandoverVerification.Code, handover.Verification);
        Assert.Equal(row.Id, handover.HandoverCodeId);
        Assert.NotNull(row.UsedAt);
        Assert.Equal(OwnerId, row.UsedByUserId);
        context.AuditTrail.Received(1).Record(Arg.Is<AuditEntry>(e => e.Action == AuditAction.HandoverVerified && e.EntityId == booking.Id));
        context.Notifier.Received().Raise(Arg.Is<Notification>(n => n.Kind == NotificationKind.YourBookingPickedUp && n.RecipientUserId == booking.CustomerId));
        Assert.Equal("Code", result.Value.Handovers.Single().Verification);
    }

    [Fact]
    public async Task A_wrong_code_is_refused_and_the_failure_is_committed_before_the_answer()
    {
        var context = new Context();
        var booking = ConfirmedFor(context);
        var (code, row) = context.GivenCodeFor(booking, HandoverType.Pickup);
        var wrong = code == "000000" ? "000001" : "000000";

        var result = await context.Handlers().Handle(
            new RecordPickupCommand(OwnerId, booking.Id, null, null, null, null, HandoverCode: wrong), CancellationToken.None);

        Assert.Equal("handover.code_invalid", result.Error.Code);
        Assert.Same(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(1, row.FailedAttempts);
        Assert.Null(row.UsedAt);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Five_wrong_codes_lock_it_and_even_the_right_one_then_fails()
    {
        var context = new Context();
        var booking = ConfirmedFor(context);
        var (code, row) = context.GivenCodeFor(booking, HandoverType.Pickup);
        var wrong = code == "000000" ? "000001" : "000000";

        for (var i = 0; i < 5; i++)
            await context.Handlers().Handle(new RecordPickupCommand(OwnerId, booking.Id, null, null, null, null, HandoverCode: wrong), CancellationToken.None);
        var right = await context.Handlers().Handle(new RecordPickupCommand(OwnerId, booking.Id, null, null, null, null, HandoverCode: code), CancellationToken.None);

        Assert.Equal("handover.code_locked", right.Error.Code);
        Assert.Same(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public async Task A_code_opens_only_its_own_booking_and_only_its_own_handover()
    {
        var context = new Context();
        var mine = ConfirmedFor(context);
        var (code, row) = context.GivenCodeFor(mine, HandoverType.Pickup);

        Assert.True(context.CodeService.Matches(row.CodeHash, mine.Id, HandoverType.Pickup, code));
        Assert.False(context.CodeService.Matches(row.CodeHash, Id.New(), HandoverType.Pickup, code));
        Assert.False(context.CodeService.Matches(row.CodeHash, mine.Id, HandoverType.Return, code));

        // Presented on another booking of the same dealership, it is refused there.
        var other = ConfirmedFor(context);
        context.HandoverCodes.GetCurrentAsync(other.Id, HandoverType.Pickup, Arg.Any<CancellationToken>())
            .Returns(HandoverCode.Issue(other.Id, HandoverType.Pickup, context.CodeService.Hash(other.Id, HandoverType.Pickup, "999999"), context.Clock.UtcNow, TimeSpan.FromMinutes(15)));
        var result = await context.Handlers().Handle(
            new RecordPickupCommand(OwnerId, other.Id, null, null, null, null, HandoverCode: code == "999999" ? "999998" : code), CancellationToken.None);

        Assert.Equal("handover.code_invalid", result.Error.Code);
        Assert.Same(BookingStatus.Confirmed, other.Status);
    }

    [Fact]
    public async Task A_scanned_QR_proves_the_handover_like_the_typed_digits()
    {
        var context = new Context();
        var booking = ConfirmedFor(context);
        var (code, row) = context.GivenCodeFor(booking, HandoverType.Pickup);

        var result = await context.Handlers().Handle(
            new RecordPickupCommand(OwnerId, booking.Id, null, null, null, null,
                HandoverCode: $"khadra-handover:v1:{booking.Reference.Value}:{code}"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.NotNull(row.UsedAt);
    }

    [Fact]
    public async Task Typed_digits_with_spaces_are_read_as_the_code()
    {
        var context = new Context();
        var booking = ConfirmedFor(context);
        var (code, _) = context.GivenCodeFor(booking, HandoverType.Pickup);

        var result = await context.Handlers().Handle(
            new RecordPickupCommand(OwnerId, booking.Id, null, null, null, null, HandoverCode: $" {code[..3]} {code[3..]} "), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
    }

    [Fact]
    public async Task A_QR_for_another_booking_counts_as_a_wrong_guess()
    {
        var context = new Context();
        var booking = ConfirmedFor(context);
        var (code, row) = context.GivenCodeFor(booking, HandoverType.Pickup);

        var result = await context.Handlers().Handle(
            new RecordPickupCommand(OwnerId, booking.Id, null, null, null, null,
                HandoverCode: $"khadra-handover:v1:KH-OTHER-1:{code}"), CancellationToken.None);

        Assert.Equal("handover.code_invalid", result.Error.Code);
        Assert.Equal(1, row.FailedAttempts);
        Assert.Same(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public async Task The_guess_that_locks_the_code_is_audited_once()
    {
        var context = new Context();
        var booking = ConfirmedFor(context);
        var (code, _) = context.GivenCodeFor(booking, HandoverType.Pickup);
        var wrong = code == "000000" ? "000001" : "000000";

        for (var i = 0; i < 7; i++)
            await context.Handlers().Handle(new RecordPickupCommand(OwnerId, booking.Id, null, null, null, null, HandoverCode: wrong), CancellationToken.None);

        context.AuditTrail.Received(1).Record(Arg.Is<AuditEntry>(e => e.Action == AuditAction.HandoverCodeLocked && e.EntityId == booking.Id));
    }

    [Fact]
    public async Task An_expired_code_is_refused()
    {
        var context = new Context();
        var booking = ConfirmedFor(context);
        var (code, _) = context.GivenCodeFor(booking, HandoverType.Pickup);
        context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(16);

        var result = await context.Handlers().Handle(new RecordPickupCommand(OwnerId, booking.Id, null, null, null, null, HandoverCode: code), CancellationToken.None);

        Assert.Equal("handover.code_expired", result.Error.Code);
    }

    [Fact]
    public async Task Pressing_it_twice_says_already_picked_up_not_code_used()
    {
        var context = new Context();
        var booking = ConfirmedFor(context);
        var (code, _) = context.GivenCodeFor(booking, HandoverType.Pickup);

        await context.Handlers().Handle(new RecordPickupCommand(OwnerId, booking.Id, null, null, null, null, HandoverCode: code), CancellationToken.None);
        var again = await context.Handlers().Handle(new RecordPickupCommand(OwnerId, booking.Id, null, null, null, null, HandoverCode: code), CancellationToken.None);

        Assert.Equal("booking.not_confirmed", again.Error.Code);
        Assert.Single(booking.Handovers);
    }

    [Fact]
    public async Task Without_a_code_the_dealer_can_record_it_unverified_with_a_reason_and_it_is_audited()
    {
        var context = new Context();
        var booking = ConfirmedFor(context);
        context.HandoverSettings.RequireVerification.Returns(true);

        var result = await context.Handlers().Handle(
            new RecordPickupCommand(OwnerId, booking.Id, null, null, null, null, UnverifiedReason: "Customer's phone battery was dead; ID checked."), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var handover = booking.Handovers.Single();
        Assert.True(handover.IsUnverified);
        Assert.Equal("Customer's phone battery was dead; ID checked.", handover.UnverifiedReason);
        context.AuditTrail.Received(1).Record(Arg.Is<AuditEntry>(e => e.Action == AuditAction.HandoverUnverified && e.Reason == "Customer's phone battery was dead; ID checked."));
    }

    [Fact]
    public async Task When_verification_is_required_nothing_at_all_is_refused_and_a_token_reason_too()
    {
        var context = new Context();
        var booking = ConfirmedFor(context);
        context.HandoverSettings.RequireVerification.Returns(true);

        var nothing = await context.Handlers().Handle(new RecordPickupCommand(OwnerId, booking.Id, null, null, null, null), CancellationToken.None);
        var tooShort = await context.Handlers().Handle(new RecordPickupCommand(OwnerId, booking.Id, null, null, null, null, UnverifiedReason: "ok"), CancellationToken.None);

        Assert.Equal("handover.code_required", nothing.Error.Code);
        Assert.Equal("handover.reason_required", tooShort.Error.Code);
        Assert.Same(BookingStatus.Confirmed, booking.Status);
    }

    [Fact]
    public async Task While_verification_is_not_required_the_old_console_still_works_and_nothing_is_audited()
    {
        var context = new Context();
        var booking = ConfirmedFor(context);

        var result = await context.Handlers().Handle(new RecordPickupCommand(OwnerId, booking.Id, null, null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(HandoverVerification.NotRequired, booking.Handovers.Single().Verification);
        context.AuditTrail.DidNotReceive().Record(Arg.Any<AuditEntry>());
    }

    [Fact]
    public void The_code_never_appears_in_a_commands_text()
    {
        var text = new RecordReturnCommand(OwnerId, Id.New(), null, null, null, null, HandoverCode: "482913").ToString();

        Assert.DoesNotContain("482913", text, StringComparison.Ordinal);
    }

    // ── The approval email ────────────────────────────────────────────────────────────────────
    //
    // The one channel that reaches a customer who is not holding their phone. There is no push
    // (item 73) and the deposit is owed within the frozen payment window, so an approval nobody
    // reads is a lost rental and a car held for nothing (item 90).

    [Fact]
    public async Task An_approval_emails_the_customer_once()
    {
        var context = new Context();
        var booking = context.GivenRequested();
        var customer = context.GivenCustomerFor(booking);

        var result = await context.Handlers().Handle(
            new ApproveBookingCommand(OwnerId, booking.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var sent = Assert.Single(context.Sent);
        Assert.Equal(customer.Email.Value, sent.ToAddress);
        Assert.Contains(booking.Reference.Value, sent.Subject, StringComparison.Ordinal);
    }

    /// <summary>
    /// A mail server having a bad minute does not undo a decision a gallery has made.
    /// </summary>
    /// <remarks>
    /// The approval is committed before the message is composed. Returning a failure here would send
    /// the gallery back to retry, where they would be told the booking is not awaiting a decision —
    /// a confusing lie about something that worked. The dispatcher logs it and the approval stands.
    /// </remarks>
    [Fact]
    public async Task A_failed_email_does_not_undo_the_approval()
    {
        var context = new Context();
        var booking = context.GivenRequested();
        context.GivenCustomerFor(booking);
        context.Sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns<Task<EmailSendReceipt>>(_ => throw new InvalidOperationException("the relay hung up"));

        var result = await context.Handlers().Handle(
            new ApproveBookingCommand(OwnerId, booking.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(BookingStatus.Approved, booking.Status);
        Assert.Equal(Build.Now.AddMinutes(20), booking.PaymentDeadline);
        // And the work was committed exactly once, before the send was ever attempted.
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        // A send that failed is never logged as one the transport accepted.
        Assert.False(context.EmailLog.Logged(1202));
    }

    /// <summary>
    /// An approval email the transport took leaves a line to trace it by: the provider's message id,
    /// worded as ACCEPTED rather than delivered, and with the customer's address left out.
    /// </summary>
    [Fact]
    public async Task An_accepted_approval_email_is_logged_with_its_receipt_and_without_the_address()
    {
        var context = new Context();
        var booking = context.GivenRequested();
        var customer = context.GivenCustomerFor(booking);
        // A request token that CAN be cancelled, as a real one can, so the send below is seen to be
        // handed a different one.
        using var request = new CancellationTokenSource();

        var result = await context.Handlers().Handle(
            new ApproveBookingCommand(OwnerId, booking.Id, null), request.Token);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        // The gallery's browser dropping the connection must not abandon this send half-way: it is the
        // one message whose loss is probably a booking that expires unread.
        Assert.False(Assert.Single(context.SendTokens).CanBeCanceled);
        var accepted = Assert.Single(context.EmailLog.Entries, entry => entry.Id.Id == 1202);
        Assert.Equal(LogLevel.Information, accepted.Level);
        Assert.Contains(TestEmail.Accepted().ProviderMessageId!, accepted.Message, StringComparison.Ordinal);
        Assert.Contains("Accepted is not delivered", accepted.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(customer.Email.Value, context.EmailLog.AllText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An address nobody has proved they can read is not written to.</summary>
    [Fact]
    public async Task An_unverified_address_is_not_emailed()
    {
        var context = new Context();
        var booking = context.GivenRequested();
        var customer = Build.Customer(emailVerified: false);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(customer, booking.CustomerId);
        context.Users.GetByIdAsync(booking.CustomerId, Arg.Any<CancellationToken>()).Returns(customer);

        var result = await context.Handlers().Handle(
            new ApproveBookingCommand(OwnerId, booking.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Empty(context.Sent);
    }

    /// <summary>A refused approval sends nothing. There is nothing to tell the customer about.</summary>
    [Fact]
    public async Task A_refused_approval_emails_nobody()
    {
        var context = new Context();
        var booking = context.GivenRequested();
        context.GivenCustomerFor(booking);
        // Answered inside the payment window, which is now a refusal rather than a short window.
        context.Clock.UtcNow = booking.Period.Start.AddMinutes(-5);

        var result = await context.Handlers().Handle(
            new ApproveBookingCommand(OwnerId, booking.Id, null), CancellationToken.None);

        Assert.Equal("booking.decision_window_elapsed", result.Error.Code);
        Assert.Empty(context.Sent);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Rejection is the customer's business too, but not by email. It costs them nothing.</summary>
    [Fact]
    public async Task A_rejection_sends_no_email()
    {
        var context = new Context();
        var booking = context.GivenRequested();
        context.GivenCustomerFor(booking);

        var result = await context.Handlers().Handle(
            new RejectBookingCommand(OwnerId, booking.Id, "VehicleUnavailable", "Sold last week."),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Empty(context.Sent);
    }
}
