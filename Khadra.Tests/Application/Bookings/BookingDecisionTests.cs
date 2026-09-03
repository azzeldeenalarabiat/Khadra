using Khadra.Application.Bookings.DecideBooking;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Dealers;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Tests.Support;
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
        public TestClock Clock { get; } = new(Build.Now);
        public Dealer Dealer { get; }

        public Context()
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            Reader.ContextAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>())
                .Returns(new BookingContext(null, "Al-Nadeem Rentals", "Layla Odeh", null));
            Dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
            Dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(Dealer);
        }

        public Booking GivenRequested()
        {
            var booking = Build.Booking(dealerId: Dealer.Id);
            booking.ConfirmDepositPaid(Id.New(), Build.Now);
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

        public BookingDecisionHandlers Handlers() =>
            new(Bookings, new DealerMembershipResolver(Dealers), Reader, Clock, UnitOfWork);
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

    [Fact]
    public async Task Rejection_composes_a_reason_written_for_the_customer()
    {
        var context = new Context();
        var booking = context.GivenRequested();

        var result = await context.Handlers().Handle(
            new RejectBookingCommand(OwnerId, booking.Id, "DatesConflict", "The car is out until the 12th."),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Same(BookingStatus.Rejected, booking.Status);
        var last = booking.StatusHistory.OrderBy(change => change.OccurredAt).Last();
        Assert.Equal("The dates conflict with another booking: The car is out until the 12th.", last.Reason);
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
        foreign.ConfirmDepositPaid(Id.New(), Build.Now);
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
        var outOnRental = Build.ApprovedBooking(terms: Build.Terms());
        var start = outOnRental.Period.Start;
        outOnRental.RecordPickup(BookingParty.Dealer, OwnerId, start);
        context.Bookings.GetByIdAsync(outOnRental.Id, Arg.Any<CancellationToken>()).Returns(outOnRental);
        // Re-home it under this dealer: the factory picks a random dealer id.
        var mine = Build.Booking(dealerId: context.Dealer.Id, period: Build.Period(Build.Now.AddDays(1), 3), pricing: Build.Pricing(days: 3));
        mine.ConfirmDepositPaid(Id.New(), Build.Now);
        mine.Approve(OwnerId, Build.Now);
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
        booking.ConfirmDepositPaid(Id.New(), Build.Now);
        booking.Approve(OwnerId, Build.Now);
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
}
