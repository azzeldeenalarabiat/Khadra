using System.Text.Json;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
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

    private static BookingContext Context(bool dealerRemoved, bool customerAccountClosed) =>
        new(
            new VehicleLabel(Guid.NewGuid(), "Kia", "Sportage", 2024, "White", "12-34567", null),
            dealerRemoved ? "Dealer no longer on the platform" : "Rami Haddad Rentals",
            DealerRemoved: dealerRemoved,
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

    private static void AssertParties(JsonElement root, bool dealerRemoved, bool customerAccountClosed)
    {
        Assert.Equal(JsonValueKind.String, root.GetProperty("dealerName").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("customerName").ValueKind);
        Assert.Equal(dealerRemoved, root.GetProperty("dealerRemoved").GetBoolean());
        Assert.Equal(customerAccountClosed, root.GetProperty("customerAccountClosed").GetBoolean());
    }
}
