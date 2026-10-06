using Khadra.Application.Bookings;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadBookings;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Dealers;
using Khadra.Application.Payments;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Payments.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Bookings;

/// <summary>
/// The customer does not see the car's plate until the office approves the booking (owner, 2026-10-05; E2E F65).
/// The office's add-vehicle form promises exactly that; until Wave 3 the API sent the plate on every customer path.
/// Once approved the plate stays, whatever happens to the booking next. The office's own copy is never touched.
/// </summary>
public sealed class PlateBeforeApprovalTests
{
    private const string Plate = "12-34567";
    private static readonly Id CustomerId = Id.New();
    private static readonly Id OwnerId = Id.New();

    private static readonly BookingContext WithCar = new(
        new VehicleLabel(Guid.NewGuid(), "Kia", "Sportage", 2024, "White", Plate, null),
        "Petra Wheels", false, null, "Layla Odeh", false, null, null);

    private static BookingListItem Row(DateTimeOffset? approvedAt, bool withCar = true) => new(
        Guid.NewGuid(), "KH-TEST0001", "Requested", Build.Now.AddDays(7), Build.Now.AddDays(10), 3, "SelfPickup",
        120m, "JOD", Build.Now,
        withCar ? WithCar.Vehicle : null,
        "Petra Wheels", false, "Layla Odeh", false, false, Guid.NewGuid(), CustomerId.Value, approvedAt);

    [Fact]
    public void A_request_the_office_has_not_answered_carries_no_plate_to_the_customer()
    {
        var dto = BookingDto.From(Build.Booking(), WithCar, Build.Now);

        Assert.Null(dto.ForCustomer().Vehicle!.PlateNumber);
        // Everything else about the car still travels: the customer booked it and sees which one.
        Assert.Equal("Sportage", dto.ForCustomer().Vehicle!.Model);
        Assert.Equal(Plate, dto.ForDealer().Vehicle!.PlateNumber);
    }

    [Fact]
    public void Once_approved_the_customer_sees_the_plate()
    {
        var dto = BookingDto.From(Build.ApprovedBooking(), WithCar, Build.Now);

        Assert.Equal(Plate, dto.ForCustomer().Vehicle!.PlateNumber);
    }

    [Fact]
    public void A_refused_request_never_carries_it()
    {
        var booking = Build.Booking();
        Assert.True(booking.Reject(Id.New(), BookingRejectionReason.VehicleUnavailable, "The car is in for service that week.", Build.Now).IsSuccess);

        Assert.Null(BookingDto.From(booking, WithCar, Build.Now).ForCustomer().Vehicle!.PlateNumber);
    }

    [Fact]
    public void An_approval_that_lapsed_unpaid_keeps_it()
    {
        var booking = Build.ApprovedBooking();
        Assert.True(booking.ExpireUnpaid(booking.PaymentDeadline!.Value.AddMinutes(1)).IsSuccess);

        Assert.Equal(Plate, BookingDto.From(booking, WithCar, Build.Now).ForCustomer().Vehicle!.PlateNumber);
    }

    [Fact]
    public void A_list_row_follows_the_same_rule()
    {
        Assert.Null(Row(approvedAt: null).ForCustomer().Vehicle!.PlateNumber);
        Assert.Equal(Plate, Row(approvedAt: Build.Now).ForCustomer().Vehicle!.PlateNumber);
        Assert.Null(Row(approvedAt: null, withCar: false).ForCustomer().Vehicle);
    }

    [Fact]
    public async Task The_customers_list_holds_the_plate_back_and_the_offices_does_not()
    {
        var reader = Substitute.For<IBookingReader>();
        var dealers = Substitute.For<IDealerRepository>();
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(dealer);
        reader.ListAsync(Arg.Any<BookingListFilter>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<BookingListItem>([Row(approvedAt: null), Row(approvedAt: Build.Now)], 1, 20, 2));
        var handler = new ListMyBookingsHandler(reader, new DealerMembershipResolver(dealers));

        var customer = await handler.Handle(
            new ListMyBookingsQuery(CustomerId, UserRole.Customer, null, PageRequest.From(1, 20)), CancellationToken.None);
        var office = await handler.Handle(
            new ListMyBookingsQuery(OwnerId, UserRole.DealerOwner, null, PageRequest.From(1, 20)), CancellationToken.None);

        Assert.Equal([null, Plate], customer.Value.Items.Select(item => item.Vehicle!.PlateNumber));
        Assert.Equal([Plate, Plate], office.Value.Items.Select(item => item.Vehicle!.PlateNumber));
        Assert.Equal(2, customer.Value.TotalCount);
    }

    [Fact]
    public async Task The_customers_next_booking_holds_the_plate_back_while_the_office_decides()
    {
        var reader = Substitute.For<IBookingReader>();
        reader.NextForCustomerAsync(CustomerId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new NextBooking(Row(approvedAt: null), NextBookingReason.AwaitingDecision));

        var next = await new GetMyNextBookingHandler(reader, new TestClock(Build.Now))
            .Handle(new GetMyNextBookingQuery(CustomerId, UserRole.Customer), CancellationToken.None);

        Assert.Null(next.Value!.Booking.Vehicle!.PlateNumber);
    }

    [Fact]
    public async Task One_booking_read_by_each_party()
    {
        var bookings = Substitute.For<IBookingRepository>();
        var reader = Substitute.For<IBookingReader>();
        var dealers = Substitute.For<IDealerRepository>();
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(dealer);
        var booking = Build.Booking(customerId: CustomerId, dealerId: dealer.Id);
        bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        reader.ContextAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(WithCar);
        var clock = new TestClock(Build.Now);
        var handler = new GetBookingHandler(
            bookings,
            reader,
            new BookingPartyResolver(dealers),
            new BookingPaymentAvailability(TestPayments.NoProvider(), Substitute.For<IPaymentRepository>(), TestBusinessRules.Provider(), clock),
            clock);

        var customer = await handler.Handle(new GetBookingQuery(CustomerId, booking.Id), CancellationToken.None);
        var office = await handler.Handle(new GetBookingQuery(OwnerId, booking.Id), CancellationToken.None);

        Assert.Null(customer.Value.Vehicle!.PlateNumber);
        Assert.Equal(Plate, office.Value.Vehicle!.PlateNumber);
    }
}
