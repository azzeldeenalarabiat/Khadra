using Khadra.Application.Bookings;
using Khadra.Application.Payments.Financials;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Disputes.Repositories;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
using Khadra.Tests.Support;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Khadra.Tests.Application.Payments;

/// <summary>
/// Who reads which projection of a booking's financial state (payments Phase 4). The party rule is
/// <c>BookingPartyResolver</c>'s, pinned in its own tests; these pin that the financials endpoint uses
/// it — the customer's projection for the customer, the office's for its owner and active employees,
/// and "not found" for everyone else — and that the administrator reads everything.
/// </summary>
public sealed class BookingFinancialsQueryTests
{
    private static readonly Id CustomerId = Id.New();
    private static readonly Id OwnerId = Id.New();
    private static readonly Id EmployeeId = Id.New();

    private readonly IBookingRepository _bookings = Substitute.For<IBookingRepository>();
    private readonly IPaymentRepository _payments = Substitute.For<IPaymentRepository>();
    private readonly IDisputeTicketRepository _tickets = Substitute.For<IDisputeTicketRepository>();
    private readonly IDealerRepository _dealers = Substitute.For<IDealerRepository>();
    private readonly RecordingLogger<BookingFinancialsHandlers> _logger = new();

    private BookingFinancialsHandlers Handlers() =>
        new(_bookings, _payments, _tickets, new BookingPartyResolver(_dealers), new TestClock(Build.Now), _logger);

    private (Booking Booking, Payment Payment) Given(Id dealerId)
    {
        var (booking, payment) = Build.PaidBooking(inFull: true, fee: 4.5m, customerId: CustomerId, dealerId: dealerId);
        _bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        _payments.ListForBookingAsync(booking.Id, Arg.Any<CancellationToken>()).Returns([payment]);
        _tickets.ListResolvedForBookingAsync(booking.Id, Arg.Any<CancellationToken>()).Returns([]);
        return (booking, payment);
    }

    [Fact]
    public async Task The_customer_reads_the_customers_projection()
    {
        var (booking, _) = Given(Id.New());

        var result = await Handlers().Handle(new GetBookingFinancialsQuery(CustomerId, booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Null(result.Value.Commission);
        Assert.Equal(4.5m, result.Value.Summary.ProcessingFees!.Amount);
        Assert.Null(result.Value.Issues);
    }

    [Fact]
    public async Task The_office_owner_and_an_active_employee_read_the_offices_projection()
    {
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        var employee = dealer.HireEmployee(EmployeeId, canViewReports: false, Build.Now).Value;
        var (booking, _) = Given(dealer.Id);
        _dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(dealer);
        _dealers.GetByStaffUserIdAsync(EmployeeId, Arg.Any<CancellationToken>()).Returns(dealer);

        var owner = await Handlers().Handle(new GetBookingFinancialsQuery(OwnerId, booking.Id), CancellationToken.None);
        var staff = await Handlers().Handle(new GetBookingFinancialsQuery(EmployeeId, booking.Id), CancellationToken.None);
        dealer.DeactivateEmployee(employee.Id, Build.Now.AddDays(1));
        var former = await Handlers().Handle(new GetBookingFinancialsQuery(EmployeeId, booking.Id), CancellationToken.None);

        Assert.NotNull(owner.Value.Commission);
        Assert.Null(owner.Value.Summary.ProcessingFees);
        Assert.NotNull(staff.Value.Commission);
        Assert.Equal("booking.not_found", former.Error.Code);
    }

    [Fact]
    public async Task A_stranger_and_an_unknown_booking_are_both_not_found()
    {
        var (booking, _) = Given(Id.New());

        var stranger = await Handlers().Handle(new GetBookingFinancialsQuery(Id.New(), booking.Id), CancellationToken.None);
        var unknown = await Handlers().Handle(new GetBookingFinancialsQuery(CustomerId, Id.New()), CancellationToken.None);

        Assert.Equal("booking.not_found", stranger.Error.Code);
        Assert.Equal(ErrorKind.NotFound, stranger.Error.Kind);
        Assert.Equal("booking.not_found", unknown.Error.Code);
        await _payments.DidNotReceiveWithAnyArgs().ListForBookingAsync(default, default);
    }

    [Fact]
    public async Task The_administrator_reads_everything_and_contradictions_are_logged()
    {
        var (booking, _) = Given(Id.New());
        // Cancelled inside the free window with no refund recorded: the records contradict each other.
        Assert.True(booking.Cancel(BookingParty.Customer, CustomerId, null, Build.Now).IsSuccess);

        var result = await Handlers().Handle(new GetAnyBookingFinancialsQuery(booking.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.NotNull(result.Value.Commission);
        Assert.Equal(94.5m, result.Value.Summary.ChargedOnline!.Amount);
        Assert.Contains(FinancialIssues.EndingRefundMissing, result.Value.Issues!);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains(booking.Id.Value.ToString(), StringComparison.Ordinal));
    }
}
