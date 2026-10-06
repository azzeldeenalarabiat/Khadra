using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Bookings;

/// <summary>
/// The pickup and return windows (owner, 2026-10-05; E2E F51, pre-launch item 225): a pickup may be
/// recorded from the booking's HoldStart — the rental start less the frozen turnaround — and a return
/// from the rental start, never earlier, whatever proved the handover.
/// </summary>
public sealed class HandoverWindowTests
{
    [Fact]
    public void The_pickup_window_opens_when_the_booking_claims_the_car()
    {
        var booking = Build.ConfirmedBooking();

        Assert.Equal(booking.HoldStart, booking.PickupAvailableFrom);
        Assert.Equal(booking.Period.Start.Subtract(Build.TurnaroundBuffer), booking.PickupAvailableFrom);
        Assert.Equal(booking.Period.Start, booking.ReturnAvailableFrom);
    }

    [Fact]
    public void A_pickup_before_the_window_is_refused_and_says_when_it_opens()
    {
        var booking = Build.ConfirmedBooking();
        var early = booking.PickupAvailableFrom.AddMinutes(-1);

        var refused = booking.RecordPickup(BookingParty.Dealer, Id.New(), early);

        Assert.True(refused.IsFailure);
        Assert.Equal("booking.pickup_too_early", refused.Error.Code);
        Assert.Equal(booking.PickupAvailableFrom, refused.Error.Extensions!["availableFrom"]);
        Assert.Same(BookingStatus.Confirmed, booking.Status);
        Assert.Empty(booking.Handovers);
        Assert.Null(booking.PickedUpAt);
    }

    [Fact]
    public void Days_early_is_refused_too()
    {
        var booking = Build.ConfirmedBooking();

        Assert.Equal(
            "booking.pickup_too_early",
            booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start.AddDays(-3)).Error.Code);
    }

    [Fact]
    public void A_pickup_is_recorded_from_the_moment_the_window_opens()
    {
        var booking = Build.ConfirmedBooking();

        var recorded = booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.PickupAvailableFrom);

        Assert.True(recorded.IsSuccess);
        Assert.Same(BookingStatus.PickedUp, booking.Status);
        Assert.Equal(booking.PickupAvailableFrom, booking.PickedUpAt);
    }

    [Fact]
    public void A_pickup_between_the_window_and_the_start_is_recorded()
    {
        var booking = Build.ConfirmedBooking();

        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start.AddMinutes(-30)).IsSuccess);
    }

    [Fact]
    public void A_return_before_the_rental_starts_is_refused_and_says_when()
    {
        var booking = Build.ConfirmedBooking();
        booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.PickupAvailableFrom);

        var refused = booking.RecordReturn(BookingParty.Dealer, Id.New(), booking.Period.Start.AddMinutes(-1));

        Assert.Equal("booking.return_too_early", refused.Error.Code);
        Assert.Equal(booking.Period.Start, refused.Error.Extensions!["availableFrom"]);
        Assert.Same(BookingStatus.PickedUp, booking.Status);
        Assert.Null(booking.ReturnedAt);
        Assert.Single(booking.Handovers);
    }

    [Fact]
    public void A_return_is_recorded_from_the_rental_start()
    {
        var booking = Build.ConfirmedBooking();
        booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.PickupAvailableFrom);

        var recorded = booking.RecordReturn(BookingParty.Dealer, Id.New(), booking.Period.Start);

        Assert.True(recorded.IsSuccess);
        Assert.Same(BookingStatus.Returned, booking.Status);
    }

    public static TheoryData<string> Proofs => ["code", "unverified", "notRequired"];

    [Theory]
    [MemberData(nameof(Proofs))]
    public void The_window_holds_whatever_proved_the_handover(string kind)
    {
        var booking = Build.ConfirmedBooking();
        var proof = kind switch
        {
            "code" => HandoverProof.ByCode(Id.New()),
            "unverified" => HandoverProof.Unverified("The customer's phone was flat at the counter.").Value,
            _ => HandoverProof.NotRequired,
        };

        var early = booking.RecordPickup(
            BookingParty.Dealer, Id.New(), booking.PickupAvailableFrom.AddSeconds(-1), proof: proof);
        Assert.Equal("booking.pickup_too_early", early.Error.Code);

        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.PickupAvailableFrom, proof: proof).IsSuccess);

        var earlyReturn = booking.RecordReturn(
            BookingParty.Dealer, Id.New(), booking.Period.Start.AddSeconds(-1), proof: proof);
        Assert.Equal("booking.return_too_early", earlyReturn.Error.Code);
    }

    [Fact]
    public void The_window_follows_the_turnaround_frozen_on_the_booking_not_todays_setting()
    {
        var booking = Build.ConfirmedBooking(terms: Build.Terms(turnaroundBuffer: TimeSpan.FromMinutes(30)));

        Assert.Equal(booking.Period.Start.AddMinutes(-30), booking.PickupAvailableFrom);
        Assert.Equal("booking.pickup_too_early",
            booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start.AddMinutes(-31)).Error.Code);
        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start.AddMinutes(-30)).IsSuccess);
    }

    [Fact]
    public void An_extension_carries_no_turnaround_so_it_may_be_collected_from_its_own_start()
    {
        var period = Build.Period(Build.Now.AddDays(7));
        var extension = Booking.Create(
            Id.New(), Id.New(), Id.New(),
            period,
            PickupMethod.SelfPickup,
            null,
            Build.Pricing(days: 3, pickupDate: DateOnly.FromDateTime(period.Start.UtcDateTime)),
            Build.Terms(),
            PaymentOption.DepositOnly,
            Build.Now,
            extendedFromBookingId: Id.New()).Value;

        Assert.Equal(period.Start, extension.PickupAvailableFrom);
    }

    [Fact]
    public void The_windows_answer_without_recording_anything()
    {
        var booking = Build.ConfirmedBooking();

        Assert.True(booking.PickupWindowOpenAt(booking.PickupAvailableFrom.AddTicks(-1)).IsFailure);
        Assert.True(booking.PickupWindowOpenAt(booking.PickupAvailableFrom).IsSuccess);
        Assert.True(booking.ReturnWindowOpenAt(booking.Period.Start.AddTicks(-1)).IsFailure);
        Assert.True(booking.ReturnWindowOpenAt(booking.Period.Start).IsSuccess);
        Assert.Same(BookingStatus.Confirmed, booking.Status);
    }
}
