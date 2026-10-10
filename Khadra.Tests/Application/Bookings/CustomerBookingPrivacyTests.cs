using System.Text.Json;
using System.Text.Json.Nodes;
using Khadra.Application.Bookings;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadBookings;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Dealers;
using Khadra.Application.Payments;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.Payments.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.Bookings;

/// <summary>
/// What the customer's copy of a booking may carry about the people behind it (pre-launch item 245).
/// </summary>
/// <remarks>
/// <para>
/// A handover tells the customer that the office recorded it (<c>recordedBy</c>), never which of its people:
/// <c>recordedByUserId</c>, the staff member's account id, travels as null to the customer and unchanged to the office
/// and the administrator, who use it. Until Wave 7's Staging verification it reached the customer on every path,
/// because <see cref="BookingDto.ForCustomer"/> removed only the fields it named and nobody had named this one.
/// </para>
/// <para>
/// That is the weakness of shaping by removal, so this file also pins the SHAPE of the customer copy: every JSON path
/// it carries, as an allow-list. A field added anywhere in the booking now fails here until someone decides whether
/// the customer may have it, and adds the path below or shapes it away.
/// </para>
/// </remarks>
public sealed class CustomerBookingPrivacyTests
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);
    private static readonly Id CustomerId = Id.New();
    private static readonly Id OwnerId = Id.New();

    private static readonly BookingContext Context = new(
        new VehicleLabel(Guid.NewGuid(), "Kia", "Sportage", 2024, "White", "12-34567", null),
        "Petra Wheels", false, null, "Layla Odeh", false, null, null);

    private static Booking HandedOver(Id pickupStaff, Id returnStaff, Id? dealerId = null)
    {
        var booking = Build.ConfirmedBooking(customerId: CustomerId, dealerId: dealerId);
        Assert.True(booking.RecordPickup(BookingParty.Dealer, pickupStaff, booking.Period.Start).IsSuccess);
        Assert.True(booking.RecordReturn(BookingParty.Dealer, returnStaff, booking.Period.End).IsSuccess);
        booking.ClearDomainEvents();
        return booking;
    }

    [Fact]
    public void The_customer_is_told_the_office_recorded_a_handover_never_which_of_its_people()
    {
        var pickupStaff = Id.New();
        var returnStaff = Id.New();
        var dto = BookingDto.From(HandedOver(pickupStaff, returnStaff), Context, Build.Now);

        var customer = dto.ForCustomer();

        Assert.Equal(["Pickup", "Return"], customer.Handovers.Select(handover => handover.Type));
        Assert.All(customer.Handovers, handover =>
        {
            Assert.Equal("Dealer", handover.RecordedBy);
            Assert.Null(handover.RecordedByUserId);
        });
        // The office's copy and the administrator's (unshaped) keep the staff account.
        Assert.Equal(new Guid?[] { pickupStaff.Value, returnStaff.Value }, dto.ForDealer().Handovers.Select(h => h.RecordedByUserId));
        Assert.Equal(new Guid?[] { pickupStaff.Value, returnStaff.Value }, dto.Handovers.Select(h => h.RecordedByUserId));
    }

    [Fact]
    public void No_staff_account_id_is_anywhere_in_what_the_customer_is_sent()
    {
        var pickupStaff = Id.New();
        var returnStaff = Id.New();
        var dto = BookingDto.From(HandedOver(pickupStaff, returnStaff), Context, Build.Now);

        var customerWire = JsonSerializer.Serialize(dto.ForCustomer(), WireOptions);
        var officeWire = JsonSerializer.Serialize(dto.ForDealer(), WireOptions);

        Assert.DoesNotContain(pickupStaff.Value.ToString(), customerWire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(returnStaff.Value.ToString(), customerWire, StringComparison.OrdinalIgnoreCase);
        // The same booking, sent to the office, still carries them: the sentinel is looking at the right thing.
        Assert.Contains(pickupStaff.Value.ToString(), officeWire, StringComparison.OrdinalIgnoreCase);
        // The field itself stays on the wire as null: no installed client reads it, and none breaks on it.
        Assert.Contains("\"recordedByUserId\":null", customerWire, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_handed_over_booking_read_by_each_party_through_the_handler()
    {
        var pickupStaff = Id.New();
        var returnStaff = Id.New();
        var bookings = Substitute.For<IBookingRepository>();
        var reader = Substitute.For<IBookingReader>();
        var dealers = Substitute.For<IDealerRepository>();
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(dealer);
        var booking = HandedOver(pickupStaff, returnStaff, dealer.Id);
        bookings.GetByIdAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(booking);
        reader.ContextAsync(booking.Id, Arg.Any<CancellationToken>()).Returns(Context);
        var clock = new TestClock(Build.Now);
        var handler = new GetBookingHandler(
            bookings,
            reader,
            new BookingPartyResolver(dealers),
            new BookingPaymentAvailability(TestPayments.NoProvider(), Substitute.For<IPaymentRepository>(), TestBusinessRules.Provider(), clock),
            clock);

        var customer = await handler.Handle(new GetBookingQuery(CustomerId, booking.Id), CancellationToken.None);
        var office = await handler.Handle(new GetBookingQuery(OwnerId, booking.Id), CancellationToken.None);

        Assert.All(customer.Value.Handovers, handover => Assert.Null(handover.RecordedByUserId));
        Assert.Equal(new Guid?[] { pickupStaff.Value, returnStaff.Value }, office.Value.Handovers.Select(h => h.RecordedByUserId));
    }

    /// <summary>
    /// Every JSON path in the customer's copy of a handed-over booking. Adding a path here is a decision that the
    /// customer may have that field: shape it away in <see cref="BookingDto.ForCustomer"/> instead when they may not.
    /// </summary>
    private static readonly string[] CustomerCopyPaths =
    [
        "approvedAt", "bookingId", "canBeDisputed", "canBeReviewed", "canReportNonDelivery", "cancellation",
        "cancellation.canCancel", "cancellation.isFree", "cancellation.penalty", "cancellation.penalty.assessedAt",
        "cancellation.penalty.attributedTo", "cancellation.penalty.isNothingOwed", "cancellation.penalty.isRange",
        "cancellation.penalty.maxAmount", "cancellation.penalty.maxAmount.amount",
        "cancellation.penalty.maxAmount.currency", "cancellation.penalty.maxPercent",
        "cancellation.penalty.minAmount", "cancellation.penalty.minAmount.amount",
        "cancellation.penalty.minAmount.currency", "cancellation.penalty.minPercent", "cancellation.penalty.reason",
        "cancellation.penalty.reasonCode", "cancellation.penalty.requiresTicketToEnforce",
        "cancellation.penalty.state", "cancellation.refundAmount", "cancellation.willRefundDeposit",
        "cancellationReason", "cancellationReasonCode", "cancelledBy", "commissionAmount", "confirmingPayment",
        "createdAt", "customerAccountClosed", "customerId", "customerName", "dealerCityId", "dealerId", "dealerName",
        "dealerRemoved", "decisionDeadline", "deliveryLocation", "depositPaid", "depositRefund",
        "disputeWindowEndsAt", "disputes", "finishedAt", "freeCancellationDeadline", "handovers",
        "handovers[].cashCollected", "handovers[].fuelLevel", "handovers[].notes", "handovers[].odometerKm",
        "handovers[].photoCount", "handovers[].recordedAt", "handovers[].recordedBy", "handovers[].recordedByUserId",
        "handovers[].type", "handovers[].unverifiedReason", "handovers[].verification", "history",
        "history[].actorParty", "history[].actorUserId", "history[].fromStatus", "history[].occurredAt",
        "history[].reason", "history[].reasonCode", "history[].toStatus", "isAwaitingDecision", "isAwaitingPayment",
        "isPaidInFull", "isTerminal", "liveDisputeId", "myReviewId", "nonDeliveryReportableFrom", "onlinePaid",
        "onlinePaid.amount", "onlinePaid.currency", "payment", "paymentDeadline", "paymentOption", "penalty",
        "periodEnd", "periodStart", "pickedUpAt", "pickupAvailableFrom", "pickupMethod", "pricing",
        "pricing.balanceDue", "pricing.balanceDue.amount", "pricing.balanceDue.currency", "pricing.dailyRate",
        "pricing.dailyRate.amount", "pricing.dailyRate.currency", "pricing.days", "pricing.deliveryFee",
        "pricing.deliveryFee.amount", "pricing.deliveryFee.currency", "pricing.depositAmount",
        "pricing.depositAmount.amount", "pricing.depositAmount.currency", "pricing.depositPercent",
        "pricing.fuelPolicy", "pricing.mileageDailyLimitKm", "pricing.mileageExcessFeePerKm",
        "pricing.mileageUnlimited", "pricing.pickupDate", "pricing.rentalTotal", "pricing.rentalTotal.amount",
        "pricing.rentalTotal.currency", "pricing.returnDate", "pricing.securityDeposit",
        "pricing.securityDeposit.amount", "pricing.securityDeposit.currency", "pricing.totalPrice",
        "pricing.totalPrice.amount", "pricing.totalPrice.currency", "reference", "refundOutstandingAmount",
        "refundOutstandingAmount.amount", "refundOutstandingAmount.currency", "refundedAmount",
        "refundedAmount.amount", "refundedAmount.currency", "refunds", "requestedAt", "returnAvailableFrom",
        "returnedAt", "status", "terms", "terms.answerWindowHours", "terms.commissionBasis",
        "terms.commissionPercent", "terms.customerCancellationPenaltyPercent", "terms.dealerPenaltyMaxPercent",
        "terms.dealerPenaltyMinPercent", "terms.depositPercent", "terms.freeCancellationWindowHours",
        "terms.noShowTimeoutHours", "terms.paymentWindowHours", "terms.postReturnSettlementWindowHours",
        "terms.rulesVersion", "vehicle", "vehicle.color", "vehicle.coverImageUrl", "vehicle.make", "vehicle.model",
        "vehicle.plateNumber", "vehicle.vehicleId", "vehicle.year", "vehicleId",
    ];

    [Fact]
    public void The_customer_copy_carries_exactly_the_fields_decided_for_it()
    {
        var dto = BookingDto.From(HandedOver(Id.New(), Id.New()), Context, Build.Now);

        var paths = PathsOf(JsonNode.Parse(JsonSerializer.Serialize(dto.ForCustomer(), WireOptions))!);

        Assert.Equal(CustomerCopyPaths.Order(StringComparer.Ordinal), paths.Order(StringComparer.Ordinal));
    }

    /// <summary>Each property path, with array elements as <c>[]</c>, so a list of any length has one shape.</summary>
    private static SortedSet<string> PathsOf(JsonNode node, string prefix = "")
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, child) in obj)
                {
                    var path = prefix.Length == 0 ? name : $"{prefix}.{name}";
                    paths.Add(path);
                    if (child is not null)
                        paths.UnionWith(PathsOf(child, path));
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not null)
                        paths.UnionWith(PathsOf(item, $"{prefix}[]"));
                }
                break;
        }
        return paths;
    }
}
