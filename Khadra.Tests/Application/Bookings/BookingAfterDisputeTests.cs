using Khadra.Application.Bookings;
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Tests.Support;

namespace Khadra.Tests.Application.Bookings;

/// <summary>
/// Where a booking would stand after a dispute decision, read without changing it (Wave 2 C1). A decision's preview
/// reads it, so it must agree with what resolving does: <c>Booking.CloseAfterDisputeResolved</c>.
/// </summary>
public sealed class BookingAfterDisputeTests
{
    [Fact]
    public void A_returned_booking_is_completed_by_the_decision_and_final_at_it()
    {
        var (booking, _) = Build.PaidBooking();
        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start).IsSuccess);
        Assert.True(booking.RecordReturn(BookingParty.Dealer, Id.New(), booking.Period.End).IsSuccess);
        var decidedAt = booking.Period.End.AddHours(3);

        var after = BookingDisputeSettlement.AfterResolution(booking, decidedAt).Value;

        Assert.Same(BookingStatus.Completed, after.Status);
        Assert.Equal(decidedAt, after.FinalAt);
        Assert.Null(after.FurtherDisputesUntil);
        // Read, not changed.
        Assert.Same(BookingStatus.Returned, booking.Status);

        // And resolving agrees.
        Assert.True(BookingDisputeSettlement.CloseAfterDispute(booking, Id.New(), decidedAt).IsSuccess);
        Assert.Same(after.Status, booking.Status);
        Assert.Equal(after.FinalAt, booking.FinishedAt);
    }

    [Fact]
    public void A_cancellation_stays_as_it_is_and_is_final_only_when_its_window_closes()
    {
        var (booking, _) = Build.PaidBooking();
        Assert.True(booking.Cancel(BookingParty.Dealer, Id.New(), "No car.", booking.FreeCancellationDeadline!.Value.AddMinutes(1)).IsSuccess);

        var after = BookingDisputeSettlement.AfterResolution(booking, booking.FinishedAt!.Value.AddHours(1)).Value;

        Assert.Same(BookingStatus.Cancelled, after.Status);
        Assert.Equal(booking.DisputeWindowEndsAt, after.FinalAt);
        Assert.Equal(booking.DisputeWindowEndsAt, after.FurtherDisputesUntil);
    }

    [Fact]
    public void A_booking_still_running_cannot_be_decided()
    {
        var (booking, _) = Build.PaidBooking();
        Assert.True(booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start).IsSuccess);

        var after = BookingDisputeSettlement.AfterResolution(booking, booking.Period.Start.AddHours(1));

        Assert.Equal("booking.not_returned", after.Error.Code);
    }
}
