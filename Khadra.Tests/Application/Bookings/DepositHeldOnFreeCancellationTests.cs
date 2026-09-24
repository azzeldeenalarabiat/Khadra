using Khadra.Application.Bookings;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Tests.Support;

namespace Khadra.Tests.Application.Bookings;

/// <summary>
/// What a dispute on a cancelled booking may split (pre-launch item 78, owner decision 2026-09-24).
/// </summary>
/// <remarks>
/// A cancelled booking stays disputable for its settlement window. Once a free cancellation has sent
/// the deposit back, a resolution splitting "the deposit held" would be splitting money that is gone —
/// and it must stay gone even while the provider is refusing the refund, because the sweep is still
/// re-sending it.
/// </remarks>
public sealed class DepositHeldOnFreeCancellationTests
{
    private static Booking Paid() => Build.ConfirmedBooking(Build.Now);

    [Fact]
    public void A_deposit_returned_by_a_free_cancellation_is_not_held()
    {
        var booking = Paid();
        Assert.True(booking.Cancel(BookingParty.Customer, Id.New(), null, Build.Now).IsSuccess);

        Assert.True(booking.ReturnsDepositOnCancellation);
        Assert.True(BookingDisputeSettlement.DepositHeldFor(booking).IsZero);
        // Still disputable — the window is about the rental, not the money — but with nothing to split.
        Assert.True(booking.CanBeDisputed(Build.Now));
    }

    [Fact]
    public void A_paid_booking_cancelled_after_the_window_still_holds_its_deposit()
    {
        var booking = Paid();
        var late = booking.FreeCancellationDeadline!.Value.AddMinutes(1);
        Assert.True(booking.Cancel(BookingParty.Customer, Id.New(), null, late).IsSuccess);

        Assert.False(booking.ReturnsDepositOnCancellation);
        Assert.Equal(booking.Pricing.DepositAmount.Amount, BookingDisputeSettlement.DepositHeldFor(booking).Amount);
    }

    /// <summary>
    /// The owner's rule names the customer. A gallery cancelling inside the window is assessed nothing
    /// and the deposit stays held until somebody decides — an open question put to the owner.
    /// </summary>
    [Fact]
    public void A_gallery_cancelling_inside_the_window_does_not_return_the_deposit_automatically()
    {
        var booking = Paid();
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "Car damaged", Build.Now).IsSuccess);

        Assert.False(booking.ReturnsDepositOnCancellation);
        Assert.Equal(booking.Pricing.DepositAmount.Amount, BookingDisputeSettlement.DepositHeldFor(booking).Amount);
    }

    [Fact]
    public void An_unpaid_booking_holds_nothing_and_returns_nothing()
    {
        var booking = Build.ApprovedBooking(Build.Now);
        Assert.True(booking.Cancel(BookingParty.Customer, Id.New(), null, Build.Now).IsSuccess);

        Assert.False(booking.ReturnsDepositOnCancellation);
        Assert.True(BookingDisputeSettlement.DepositHeldFor(booking).IsZero);
    }
}
