using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Bookings;

/// <summary>
/// Three verdicts the booking's financial state reads (payments Phase 4), each the ONE statement of a
/// rule that used to live inline beside another: where the dispute window ends, whether a penalty is a
/// claim on the customer's deposit, and whether the booking ended without the rental taking place.
/// </summary>
public sealed class BookingFinancialVerdictsTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    [Fact]
    public void The_dispute_window_runs_from_the_return_or_from_the_ending()
    {
        var returned = Build.ConfirmedBooking();
        returned.RecordPickup(BookingParty.Dealer, Id.New(), returned.Period.Start);
        returned.RecordReturn(BookingParty.Dealer, Id.New(), returned.Period.End);
        var cancelled = Build.ConfirmedBooking();
        Assert.True(cancelled.Cancel(BookingParty.Dealer, Id.New(), "No car.", Now.AddHours(3)).IsSuccess);

        Assert.Equal(returned.Period.End.Add(returned.Terms.PostReturnSettlementWindow), returned.DisputeWindowEndsAt);
        Assert.Equal(Now.AddHours(3).Add(cancelled.Terms.PostReturnSettlementWindow), cancelled.DisputeWindowEndsAt);
        // CanBeDisputed reads the same instant, so the two can never disagree.
        Assert.True(cancelled.CanBeDisputed(cancelled.DisputeWindowEndsAt!.Value.AddTicks(-1)));
        Assert.False(cancelled.CanBeDisputed(cancelled.DisputeWindowEndsAt.Value));
    }

    [Fact]
    public void No_window_runs_while_the_booking_is_live_or_after_it_completed()
    {
        var confirmed = Build.ConfirmedBooking();
        var completed = Build.ConfirmedBooking();
        completed.RecordPickup(BookingParty.Dealer, Id.New(), completed.Period.Start);
        completed.RecordReturn(BookingParty.Dealer, Id.New(), completed.Period.End);
        Assert.True(completed.Settle(completed.DisputeWindowEndsAt!.Value, hasOpenDispute: false).IsSuccess);

        Assert.Null(confirmed.DisputeWindowEndsAt);
        Assert.Null(completed.DisputeWindowEndsAt);
    }

    [Fact]
    public void Only_a_penalty_the_customer_owes_is_a_claim_on_their_deposit()
    {
        var late = Build.ConfirmedBooking();
        Assert.True(late.Cancel(BookingParty.Customer, late.CustomerId, null, late.FreeCancellationDeadline!.Value.AddMinutes(1)).IsSuccess);
        var free = Build.ConfirmedBooking();
        Assert.True(free.Cancel(BookingParty.Customer, free.CustomerId, null, Now).IsSuccess);
        var byOffice = Build.ConfirmedBooking();
        Assert.True(byOffice.Cancel(BookingParty.Dealer, Id.New(), "No car.", Now.AddHours(3)).IsSuccess);
        var deliveryNoShow = Build.ConfirmedBooking(pickupMethod: PickupMethod.Delivery);
        Assert.True(deliveryNoShow.MarkNoShow(deliveryNoShow.Period.Start.Add(deliveryNoShow.Terms.NoShowTimeout)).IsSuccess);

        Assert.True(late.HasPenaltyAgainstCustomer);
        Assert.False(free.HasPenaltyAgainstCustomer);
        Assert.False(byOffice.HasPenaltyAgainstCustomer);
        Assert.False(deliveryNoShow.HasPenaltyAgainstCustomer);
    }

    [Fact]
    public void A_booking_ended_before_pickup_unless_the_rental_took_place()
    {
        var cancelled = Build.ConfirmedBooking();
        Assert.True(cancelled.Cancel(BookingParty.Customer, cancelled.CustomerId, null, Now).IsSuccess);
        var expired = Build.ApprovedBooking();
        Assert.True(expired.ExpireUnpaid(expired.PaymentDeadline!.Value.AddMinutes(1)).IsSuccess);
        var noShow = Build.ConfirmedBooking();
        Assert.True(noShow.MarkNoShow(noShow.Period.Start.Add(noShow.Terms.NoShowTimeout)).IsSuccess);
        var pickedUp = Build.ConfirmedBooking();
        pickedUp.RecordPickup(BookingParty.Dealer, Id.New(), pickedUp.Period.Start);

        Assert.True(cancelled.EndedBeforePickup);
        Assert.True(expired.EndedBeforePickup);
        Assert.True(noShow.EndedBeforePickup);
        Assert.False(pickedUp.EndedBeforePickup);
        Assert.False(Build.ConfirmedBooking().EndedBeforePickup);
    }
}
