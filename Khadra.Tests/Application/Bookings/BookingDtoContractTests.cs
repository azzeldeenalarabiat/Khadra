using System.Text.Json;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common.Dtos;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Tests.Support;

namespace Khadra.Tests.Application.Bookings;

/// <summary>
/// The party names on a booking, as they leave the API.
/// </summary>
/// <remarks>
/// <para>
/// Two clients read these, and they read them differently. Shipped customer apps print
/// <c>dealerName</c> as it arrives, so it must stay a string — an English stand-in when the dealership
/// is gone — with the same JSON name. The consoles word the case in the reader's language, so they need
/// the fact on its own: <c>dealerRemoved</c> and <c>customerAccountClosed</c>.
/// </para>
/// <para>
/// These pin the wire names the TypeScript models declare. A renamed property compiles on both sides
/// and fails only at runtime, as <c>undefined</c> on a screen.
/// </para>
/// </remarks>
public sealed class BookingDtoContractTests
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The dealership's city, as a lookup id: what the app resolves a name from.</summary>
    private static readonly Guid CityId = Guid.Parse("01a07e16-de7b-7673-b1a5-c47bc1d437f4");

    private static BookingContext Context(bool dealerRemoved, bool customerAccountClosed) =>
        new(
            new VehicleLabel(Guid.NewGuid(), "Kia", "Sportage", 2024, "White", "12-34567", null),
            dealerRemoved ? "Dealer no longer on the platform" : "Rami Haddad Rentals",
            DealerRemoved: dealerRemoved,
            DealerCityId: CityId,
            customerAccountClosed ? "Customer account closed" : "Nour Al-Masri",
            CustomerAccountClosed: customerAccountClosed,
            null,
            null);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void A_booking_carries_each_party_flag_from_its_context(bool dealerRemoved, bool customerAccountClosed)
    {
        var context = Context(dealerRemoved, customerAccountClosed);

        var dto = BookingDto.From(Build.Booking(), context, Build.Now);

        Assert.Equal(dealerRemoved, dto.DealerRemoved);
        Assert.Equal(customerAccountClosed, dto.CustomerAccountClosed);
        // The names travel unchanged, stand-in included: an older app has nothing else to print.
        Assert.Equal(context.DealerName, dto.DealerName);
        Assert.Equal(context.CustomerName, dto.CustomerName);
        // The city travels as an ID. The customer app names it from /api/v1/cities in the reader's
        // own language, which is why nothing here composes one.
        Assert.Equal(CityId, dto.DealerCityId);
    }

    /// <summary>
    /// When the booking's dispute window closes (Wave 3 C4): a screen offering "Open a dispute" names the booking's own
    /// moment. Null while there is no window: a request, a booking not yet ended.
    /// </summary>
    [Fact]
    public void A_booking_says_when_its_dispute_window_closes_and_nothing_before_it_has_one()
    {
        Assert.Null(BookingDto.From(Build.Booking(), Context(false, false), Build.Now).DisputeWindowEndsAt);

        var booking = Build.ConfirmedBooking();
        var cancelledAt = booking.FreeCancellationDeadline!.Value.AddMinutes(1);
        Assert.True(booking.Cancel(BookingParty.Customer, booking.CustomerId, "Changed plans.", cancelledAt).IsSuccess);
        var cancelled = BookingDto.From(booking, Context(false, false), cancelledAt);

        Assert.NotNull(cancelled.DisputeWindowEndsAt);
        Assert.Equal(booking.DisputeWindowEndsAt, cancelled.DisputeWindowEndsAt);
    }

    [Fact]
    public void The_dealer_city_is_on_the_wire_as_an_id_under_its_own_name()
    {
        var dto = BookingDto.From(Build.Booking(), Context(false, false), Build.Now);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, WireOptions));
        var city = json.RootElement.GetProperty("dealerCityId");

        Assert.Equal(JsonValueKind.String, city.ValueKind);
        Assert.Equal(CityId, city.GetGuid());
    }

    [Fact]
    public void A_dealership_with_no_city_sends_null_rather_than_an_empty_id()
    {
        var context = Context(false, false) with { DealerCityId = null };

        var dto = BookingDto.From(Build.Booking(), context, Build.Now);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, WireOptions));
        // Null, not Guid.Empty: the app's city provider answers null for "no city", and an empty
        // guid would be a lookup id that resolves to nothing and looks like a bug on the screen.
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("dealerCityId").ValueKind);
        Assert.Null(dto.DealerCityId);
    }

    [Fact]
    public void A_booking_keeps_its_name_fields_as_strings_and_adds_the_flags_beside_them()
    {
        var dto = BookingDto.From(Build.Booking(), Context(dealerRemoved: true, customerAccountClosed: true), Build.Now);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, WireOptions));

        AssertParties(json.RootElement, dealerRemoved: true, customerAccountClosed: true);
    }

    [Fact]
    public void A_list_row_has_the_same_four_fields_under_the_same_names()
    {
        var row = new BookingListItem(
            Guid.NewGuid(),
            "KH-AAA111",
            "Requested",
            Build.Now.AddDays(7),
            Build.Now.AddDays(10),
            3,
            "SelfPickup",
            165m,
            "JOD",
            Build.Now,
            null,
            "Rami Haddad Rentals",
            DealerRemoved: false,
            "Customer account closed",
            CustomerAccountClosed: true,
            HasLiveDispute: false,
            Guid.NewGuid(),
            Guid.NewGuid());

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(row, WireOptions));

        AssertParties(json.RootElement, dealerRemoved: false, customerAccountClosed: true);
    }

    /// <summary>
    /// A penalty travels as a sentence AND a code: the sentence for the clients that print it, the
    /// code for the ones that word it. `reason` keeps its name and type, so nothing that reads it
    /// today notices the addition.
    /// </summary>
    [Fact]
    public void A_penalty_carries_its_stable_code_beside_the_frozen_sentence()
    {
        var booking = Build.ConfirmedBooking();
        Assert.True(booking
            .Cancel(BookingParty.Customer, Id.New(), "Changed plans.", booking.FreeCancellationDeadline!.Value.AddMinutes(1))
            .IsSuccess);

        var dto = BookingDto.From(booking, Context(dealerRemoved: false, customerAccountClosed: false), Build.Now);

        Assert.Equal("CustomerCancelledAfterFreeWindow", dto.Penalty!.ReasonCode);
        Assert.Equal(PenaltyReason.CustomerCancelledAfterFreeWindow.Sentence, dto.Penalty.Reason);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, WireOptions));
        var penalty = json.RootElement.GetProperty("penalty");
        Assert.Equal(JsonValueKind.String, penalty.GetProperty("reason").ValueKind);
        Assert.Equal("CustomerCancelledAfterFreeWindow", penalty.GetProperty("reasonCode").GetString());
    }

    /// <summary>
    /// Where a penalty stands travels with it (pre-launch item 173, owner 2026-09-26): assessed and not
    /// charged until a dispute on the booking is RESOLVED, then resolved through that dispute. The
    /// server reads its own dispute records; no client works it out for itself.
    /// </summary>
    [Theory]
    [InlineData(false, "Assessed")]
    [InlineData(true, "ResolvedByDispute")]
    public void A_penalty_says_whether_a_dispute_has_resolved_it(bool resolved, string state)
    {
        var booking = Build.ConfirmedBooking();
        Assert.True(booking
            .Cancel(BookingParty.Customer, Id.New(), "Changed plans.", booking.FreeCancellationDeadline!.Value.AddMinutes(1))
            .IsSuccess);

        var dto = BookingDto.From(booking, Context(false, false) with { HasResolvedDispute = resolved }, Build.Now);

        Assert.Equal(state, dto.Penalty!.State);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, WireOptions));
        Assert.Equal(state, json.RootElement.GetProperty("penalty").GetProperty("state").GetString());
    }

    private static MoneyDto Jod(decimal amount) => new(amount, "JOD");

    /// <summary>
    /// The rental office's copy of a booking carries no money the office is not shown (owner decisions
    /// 3 and 8, 2026-09-26): no refund list — a dispute decision's share is on it — and no processing fee
    /// anywhere, in the confirming payment or in the cancellation preview. Its money comes from the
    /// financial state's office projection. The customer's copy keeps all of it.
    /// </summary>
    [Fact]
    public void The_offices_copy_carries_no_refund_rows_and_no_processing_fee()
    {
        var booking = Build.ConfirmedBooking();
        var context = Context(false, false) with
        {
            ConfirmingPayment = new ConfirmingPaymentDto("FullPayment", Jod(94.5m), Jod(4.5m), Jod(90m), Build.Now, Jod(94.5m), Jod(4.5m)),
            Refunds = [new RefundDto(Guid.NewGuid(), Guid.NewGuid(), "DisputeResolution", Jod(9m), "Requested", Build.Now, null, null, null, Guid.NewGuid())],
            DepositRefund = new DepositRefundDto("Requested", Jod(9m), Build.Now, null, null, null),
        };
        var full = BookingDto.From(booking, context, Build.Now);
        Assert.NotNull(full.Cancellation.RefundAmount);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(full.ForDealer(), WireOptions));
        var root = json.RootElement;

        foreach (var withheld in new[] { "refunds", "refundedAmount", "refundOutstandingAmount", "depositRefund" })
            Assert.Equal(JsonValueKind.Null, root.GetProperty(withheld).ValueKind);
        var confirming = root.GetProperty("confirmingPayment");
        Assert.Equal("FullPayment", confirming.GetProperty("purpose").GetString());
        Assert.Equal(90m, confirming.GetProperty("appliedToBooking").GetProperty("amount").GetDecimal());
        foreach (var fee in new[] { "amountCharged", "processingFee", "refundOnFreeCancellation", "refundableFee" })
            Assert.Equal(JsonValueKind.Null, confirming.GetProperty(fee).ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("cancellation").GetProperty("refundAmount").ValueKind);
        // What the office IS shown stays: the frozen commission and the booking money paid online.
        Assert.Equal(JsonValueKind.Object, root.GetProperty("commissionAmount").ValueKind);
        Assert.Equal(JsonValueKind.Object, root.GetProperty("onlinePaid").ValueKind);

        var customer = full.ForCustomer();
        Assert.Single(customer.Refunds!);
        Assert.Equal(4.5m, customer.ConfirmingPayment!.ProcessingFee!.Amount);
    }

    /// <summary>
    /// A cancellation PREVIEW's penalty is not assessed yet, so it carries no state — and a booking with
    /// no penalty sends none, whatever its disputes.
    /// </summary>
    [Fact]
    public void A_cancellation_preview_carries_no_penalty_state_and_no_penalty_sends_none()
    {
        var booking = Build.ConfirmedBooking();
        var afterFreeWindow = booking.FreeCancellationDeadline!.Value.AddMinutes(1);

        var dto = BookingDto.From(booking, Context(false, false) with { HasResolvedDispute = true }, afterFreeWindow);

        Assert.Null(dto.Penalty);
        Assert.False(dto.Cancellation.IsFree);
        Assert.Null(dto.Cancellation.Penalty.State);
    }

    /// <summary>
    /// How a booking was paid, on the wire (owner, 2026-09-25). The website, the app and the console
    /// all read these names; a renamed one would compile everywhere and quietly put deposit wording
    /// back on a booking paid in full.
    /// </summary>
    [Fact]
    public void A_fully_paid_booking_says_so_and_names_the_payment_that_confirmed_it()
    {
        var booking = Build.ApprovedBooking();
        Assert.True(booking.ConfirmPayment(Id.New(), booking.Pricing.TotalPrice, Build.Now).IsSuccess);
        var total = booking.Pricing.TotalPrice.Amount;
        var confirming = new ConfirmingPaymentDto(
            "FullPayment",
            new MoneyDto(total, "JOD"),
            new MoneyDto(0m, "JOD"),
            new MoneyDto(total, "JOD"),
            Build.Now,
            new MoneyDto(total, "JOD"));

        var dto = BookingDto.From(booking, Context(false, false) with { ConfirmingPayment = confirming }, Build.Now).ForCustomer();

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, WireOptions));
        var root = json.RootElement;
        Assert.True(root.GetProperty("isPaidInFull").GetBoolean());
        Assert.Equal(0m, root.GetProperty("pricing").GetProperty("balanceDue").GetProperty("amount").GetDecimal());
        var payment = root.GetProperty("confirmingPayment");
        Assert.Equal("FullPayment", payment.GetProperty("purpose").GetString());
        Assert.Equal(total, payment.GetProperty("amountCharged").GetProperty("amount").GetDecimal());
        Assert.Equal("JOD", payment.GetProperty("amountCharged").GetProperty("currency").GetString());
        Assert.Equal(0m, payment.GetProperty("processingFee").GetProperty("amount").GetDecimal());
        Assert.Equal(total, payment.GetProperty("appliedToBooking").GetProperty("amount").GetDecimal());
        Assert.Equal(total, payment.GetProperty("refundOnFreeCancellation").GetProperty("amount").GetDecimal());
        Assert.Equal(JsonValueKind.String, payment.GetProperty("paidAt").ValueKind);
    }

    [Fact]
    public void A_booking_nothing_has_confirmed_sends_false_and_null()
    {
        var dto = BookingDto.From(Build.Booking(), Context(false, false), Build.Now);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, WireOptions));

        Assert.False(json.RootElement.GetProperty("isPaidInFull").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("confirmingPayment").ValueKind);
    }

    private static void AssertParties(JsonElement root, bool dealerRemoved, bool customerAccountClosed)
    {
        Assert.Equal(JsonValueKind.String, root.GetProperty("dealerName").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("customerName").ValueKind);
        Assert.Equal(dealerRemoved, root.GetProperty("dealerRemoved").GetBoolean());
        Assert.Equal(customerAccountClosed, root.GetProperty("customerAccountClosed").GetBoolean());
    }
}
