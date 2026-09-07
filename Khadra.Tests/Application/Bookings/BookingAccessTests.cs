using Khadra.Application.Bookings;
using Khadra.Application.Bookings.ReadBookings;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Dealers;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.IdentityAccess;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Bookings;

// Who may see a booking. The rule is written once, in BookingPartyResolver, and these tests are the
// reason it can stay there: four handlers will lean on it, and this is where its edges are pinned.
public sealed class BookingPartyResolverTests
{
    private static readonly Id CustomerId = Id.New();
    private static readonly Id OwnerId = Id.New();
    private static readonly Id EmployeeId = Id.New();
    private static readonly Id StrangerId = Id.New();

    private readonly IDealerRepository _dealers = Substitute.For<IDealerRepository>();

    private BookingPartyResolver Resolver() => new(_dealers);

    [Fact]
    public async Task The_customer_who_made_the_booking_is_the_customer_party()
    {
        var booking = Build.Booking(customerId: CustomerId);

        var party = await Resolver().ResolveAsync(booking, CustomerId);

        Assert.True(party.IsSuccess);
        Assert.Same(BookingParty.Customer, party.Value);
    }

    [Fact]
    public async Task The_dealer_owner_speaks_for_the_dealer()
    {
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        var booking = Build.Booking(dealerId: dealer.Id);
        _dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(dealer);

        var party = await Resolver().ResolveAsync(booking, OwnerId);

        Assert.Same(BookingParty.Dealer, party.Value);
    }

    [Fact]
    public async Task An_active_employee_speaks_for_the_dealer_a_deactivated_one_does_not()
    {
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        var employee = dealer.HireEmployee(EmployeeId, canViewReports: false, Build.Now).Value;
        var booking = Build.Booking(dealerId: dealer.Id);
        _dealers.GetByStaffUserIdAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(dealer);

        Assert.Same(BookingParty.Dealer, (await Resolver().ResolveAsync(booking, EmployeeId)).Value);

        dealer.DeactivateEmployee(employee.Id, Build.Now.AddDays(1));

        var afterwards = await Resolver().ResolveAsync(booking, EmployeeId);
        Assert.True(afterwards.IsFailure);
        Assert.Equal("booking.not_found", afterwards.Error.Code);
    }

    [Fact]
    public async Task A_suspended_dealer_still_sees_the_bookings_it_already_has()
    {
        // Deliberately not Dealer.CanActOnBookings: losing the right to trade is not losing the
        // right to see -- or dispute -- what was already booked.
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        dealer.Suspend(Id.New(), "Complaints.", Build.Now);
        var booking = Build.Booking(dealerId: dealer.Id);
        _dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(dealer);

        var party = await Resolver().ResolveAsync(booking, OwnerId);

        Assert.Same(BookingParty.Dealer, party.Value);
    }

    [Fact]
    public async Task Another_dealers_owner_is_told_the_booking_does_not_exist()
    {
        var otherDealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        var booking = Build.Booking(dealerId: Id.New());
        _dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(otherDealer);

        var party = await Resolver().ResolveAsync(booking, OwnerId);

        // 404, not 403: a 403 would confirm the id is real.
        Assert.Equal("booking.not_found", party.Error.Code);
        Assert.Equal(ErrorKind.NotFound, party.Error.Kind);
    }

    [Fact]
    public async Task A_stranger_is_told_the_booking_does_not_exist()
    {
        var booking = Build.Booking();

        var party = await Resolver().ResolveAsync(booking, StrangerId);

        Assert.Equal("booking.not_found", party.Error.Code);
    }
}

public sealed class ReadBookingsTests
{
    private static readonly Id CustomerId = Id.New();
    private static readonly Id OwnerId = Id.New();

    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly IBookingReader _reader = Substitute.For<IBookingReader>();
    private readonly IDealerRepository _dealers = Substitute.For<IDealerRepository>();
    private readonly TestClock _clock = new(Build.Now);

    private static readonly BookingContext EmptyContext = new(null, "Petra Wheels", "Layla Odeh", null, null);

    public ReadBookingsTests()
    {
        _reader.ContextAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>()).Returns(EmptyContext);
        _reader.ListAsync(Arg.Any<BookingListFilter>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(PagedResult.Empty<BookingListItem>(1, 20));
    }

    private GetBookingHandler Get() => new(_bookings, _reader, new BookingPartyResolver(_dealers), _clock);

    private ListMyBookingsHandler List() => new(_reader, new DealerMembershipResolver(_dealers));

    [Fact]
    public async Task A_customer_reads_their_own_booking_with_its_frozen_terms()
    {
        var booking = Build.Booking(customerId: CustomerId, terms: Build.Terms(commissionPercent: 17.5m));
        _bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var result = await Get().Handle(new GetBookingQuery(CustomerId, booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(booking.Reference.Value, result.Value.Reference);
        // The terms on the wire are the booking's own, never the current configuration.
        Assert.Equal(17.5m, result.Value.Terms.CommissionPercent);
        Assert.Equal("Petra Wheels", result.Value.DealerName);
        Assert.Null(result.Value.Vehicle);
    }

    [Fact]
    public async Task A_customer_cannot_read_another_customers_booking()
    {
        var booking = Build.Booking(customerId: Id.New());
        _bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);

        var result = await Get().Handle(new GetBookingQuery(CustomerId, booking.Id), CancellationToken.None);

        Assert.Equal("booking.not_found", result.Error.Code);
    }

    [Fact]
    public async Task A_missing_booking_and_a_forbidden_one_are_indistinguishable()
    {
        var missing = await Get().Handle(new GetBookingQuery(CustomerId, Id.New()), CancellationToken.None);

        Assert.Equal("booking.not_found", missing.Error.Code);
    }

    [Fact]
    public async Task A_customers_list_is_filtered_to_their_own_bookings()
    {
        await List().Handle(
            new ListMyBookingsQuery(CustomerId, UserRole.Customer, null, PageRequest.From(1, 20)),
            CancellationToken.None);

        await _reader.Received().ListAsync(
            Arg.Is<BookingListFilter>(filter => filter.CustomerId == CustomerId && filter.DealerId == null),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_dealer_owners_list_is_filtered_to_their_dealership()
    {
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        _dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(dealer);

        await List().Handle(
            new ListMyBookingsQuery(OwnerId, UserRole.DealerOwner, "Requested", PageRequest.From(1, 20)),
            CancellationToken.None);

        await _reader.Received().ListAsync(
            Arg.Is<BookingListFilter>(filter =>
                filter.DealerId == dealer.Id && filter.CustomerId == null && filter.Status == "Requested"),
            Arg.Any<PageRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Staff_without_a_dealership_have_no_list()
    {
        var result = await List().Handle(
            new ListMyBookingsQuery(OwnerId, UserRole.DealerOwner, null, PageRequest.From(1, 20)),
            CancellationToken.None);

        Assert.Equal("dealer.not_registered", result.Error.Code);
    }

    [Fact]
    public async Task An_administrator_has_no_bookings_of_their_own()
    {
        var result = await List().Handle(
            new ListMyBookingsQuery(Id.New(), UserRole.Admin, null, PageRequest.From(1, 20)),
            CancellationToken.None);

        Assert.Equal("booking.not_a_party", result.Error.Code);
        Assert.Equal(ErrorKind.Forbidden, result.Error.Kind);
    }

    [Fact]
    public void An_unknown_status_filter_is_refused_before_it_reaches_the_reader()
    {
        var validator = new ListMyBookingsQueryValidator();

        var unknown = validator.Validate(
            new ListMyBookingsQuery(CustomerId, UserRole.Customer, "Teleported", PageRequest.From(1, 20)));
        var known = validator.Validate(
            new ListMyBookingsQuery(CustomerId, UserRole.Customer, "returned", PageRequest.From(1, 20)));

        Assert.False(unknown.IsValid);
        Assert.True(known.IsValid);
    }
}
