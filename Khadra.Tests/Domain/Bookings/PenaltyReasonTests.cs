using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Bookings;

/// <summary>
/// The stable codes behind a penalty's sentence (owner, 2026-09-17).
/// </summary>
/// <remarks>
/// <para>
/// A penalty's reason used to be an English sentence and nothing else, so the consoles printed
/// English on an Arabic screen and could not have done otherwise. Each reason now carries a code that
/// a client can look up, and the sentence it always wrote is still written beside it.
/// </para>
/// <para>
/// Two things are pinned here because getting them wrong is expensive and silent: the sentences,
/// which are already stored on real bookings and must not drift; and the names, which are the
/// persisted form of the code — renaming one would make every booking holding it unloadable.
/// </para>
/// </remarks>
public sealed class PenaltyReasonTests
{
    /// <summary>
    /// The names and sentences as they are already stored on real bookings, byte for byte.
    /// </summary>
    /// <remarks>
    /// Written as LITERALS, deliberately. With <c>nameof</c> an IDE rename would change the member, the
    /// name persisted in every bookings.penalty document, and this table in one keystroke — and the
    /// suite would stay green while every booking holding the old name became unloadable. A literal is
    /// the only form of this table that can fail.
    /// </remarks>
    private static readonly (string Name, string Sentence)[] Frozen =
    [
        ("PaymentWindowLapsed", "The deposit was not paid within the payment window."),
        ("DealerAnswerWindowLapsed", "The dealer did not answer within the agreed window."),
        ("DealerRejected", "The dealer rejected the request."),
        ("DealerDidNotHandOver", "The dealer did not hand over the vehicle after approving the booking."),
        ("CustomerNoShow", "The customer did not collect the vehicle within the no-show window."),
        ("DeliveryNoShowUndetermined", "The vehicle was never handed over on a delivery booking; responsibility is undetermined."),
        ("CancelledBeforeDeposit", "Cancelled before the deposit was paid."),
        ("CancelledInFreeWindow", "Cancelled inside the free cancellation window."),
        ("CustomerCancelledAfterFreeWindow", "The customer cancelled after the free cancellation window."),
        ("DealerCancelledAfterFreeWindow", "The dealer cancelled after the free cancellation window."),
        ("CancelledByPlatform", "Cancelled by the platform."),
        ("NotCancellable", "This booking can no longer be cancelled."),
    ];

    [Fact]
    public void Every_reason_still_writes_the_sentence_it_always_wrote()
    {
        foreach (var (name, sentence) in Frozen)
            Assert.Equal(sentence, Enumeration.FromName<PenaltyReason>(name).Sentence);
    }

    /// <summary>
    /// The set is closed and complete: a new reason is a new member with its own sentence, and a
    /// member that quietly disappeared would take every booking holding its name with it.
    /// </summary>
    [Fact]
    public void The_twelve_system_reasons_are_all_there_and_nothing_else_is()
    {
        var all = Enumeration.GetAll<PenaltyReason>();

        Assert.Equal(Frozen.Select(reason => reason.Name).OrderBy(name => name), all.Select(reason => reason.Name).OrderBy(name => name));
        Assert.All(all, reason => Assert.False(string.IsNullOrWhiteSpace(reason.Sentence)));
        // Ids are the persisted contract's second half; two members sharing one would resolve wrongly.
        Assert.Equal(all.Count, all.Select(reason => reason.Id).Distinct().Count());
    }

    [Fact]
    public void A_code_this_build_does_not_know_is_refused_rather_than_guessed()
    {
        Assert.Throws<DomainException>(() => Enumeration.FromName<PenaltyReason>("SomethingElse"));
    }

    // ── The codes each transition records ────────────────────────────────────────────────────────

    [Fact]
    public void An_unanswered_request_expires_with_the_dealer_answer_window_code()
    {
        var booking = Build.Booking();

        booking.ExpireUnanswered(booking.DecisionDeadline.AddMinutes(1));

        AssertReason(booking, PenaltyReason.DealerAnswerWindowLapsed);
    }

    [Fact]
    public void An_unpaid_approval_expires_with_the_payment_window_code()
    {
        var booking = Build.ApprovedBooking();

        booking.ExpireUnpaid(booking.PaymentDeadline!.Value.AddMinutes(1));

        AssertReason(booking, PenaltyReason.PaymentWindowLapsed);
    }

    [Fact]
    public void A_rejection_records_that_the_dealer_said_no()
    {
        var booking = Build.Booking();

        Assert.True(booking.Reject(Id.New(), BookingRejectionReason.VehicleUnavailable, "The car is in for service.", Build.Now).IsSuccess);

        AssertReason(booking, PenaltyReason.DealerRejected);
    }

    [Fact]
    public void A_cancellation_records_which_window_it_fell_in_and_who_cancelled()
    {
        var beforeDeposit = Build.ApprovedBooking();
        beforeDeposit.Cancel(BookingParty.Customer, Id.New(), "Changed plans.", Build.Now);
        AssertReason(beforeDeposit, PenaltyReason.CancelledBeforeDeposit);

        var inFreeWindow = Build.ConfirmedBooking();
        inFreeWindow.Cancel(BookingParty.Customer, Id.New(), "Changed plans.", Build.Now);
        AssertReason(inFreeWindow, PenaltyReason.CancelledInFreeWindow);

        var byCustomer = Build.ConfirmedBooking();
        byCustomer.Cancel(BookingParty.Customer, Id.New(), "Changed plans.", PastTheFreeWindow(byCustomer));
        AssertReason(byCustomer, PenaltyReason.CustomerCancelledAfterFreeWindow);

        var byDealer = Build.ConfirmedBooking();
        byDealer.Cancel(BookingParty.Dealer, Id.New(), "Car off the road.", PastTheFreeWindow(byDealer));
        AssertReason(byDealer, PenaltyReason.DealerCancelledAfterFreeWindow);

        var byPlatform = Build.ConfirmedBooking();
        byPlatform.Cancel(BookingParty.Admin, Id.New(), "Platform decision.", PastTheFreeWindow(byPlatform));
        AssertReason(byPlatform, PenaltyReason.CancelledByPlatform);
    }

    [Fact]
    public void A_no_show_records_the_customer_on_self_pickup_and_blames_nobody_on_delivery()
    {
        var selfPickup = Build.ConfirmedBooking();
        selfPickup.MarkNoShow(selfPickup.Period.Start.Add(selfPickup.Terms.NoShowTimeout).AddMinutes(1));
        AssertReason(selfPickup, PenaltyReason.CustomerNoShow);

        var delivery = Build.ConfirmedBooking(pickupMethod: PickupMethod.Delivery);
        delivery.MarkNoShow(delivery.Period.Start.Add(delivery.Terms.NoShowTimeout).AddMinutes(1));
        AssertReason(delivery, PenaltyReason.DeliveryNoShowUndetermined);
    }

    /// <summary>A preview is not an assessment: this code is answered, never stored.</summary>
    [Fact]
    public void A_booking_past_cancelling_previews_the_not_cancellable_code()
    {
        var booking = Build.ConfirmedBooking();
        booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start);

        var preview = booking.PreviewCancellation(BookingParty.Customer, booking.Period.Start.AddHours(1));

        Assert.False(preview.CanCancel);
        Assert.Same(PenaltyReason.NotCancellable, preview.Penalty.ReasonCode);
        Assert.Equal(PenaltyReason.NotCancellable.Sentence, preview.Penalty.Reason);
        Assert.Null(booking.Penalty);
    }

    private static DateTimeOffset PastTheFreeWindow(Khadra.Domain.Bookings.Booking booking) =>
        booking.FreeCancellationDeadline!.Value.AddMinutes(1);

    /// <summary>The code AND the sentence: a client reading either one must see the same reason.</summary>
    private static void AssertReason(Khadra.Domain.Bookings.Booking booking, PenaltyReason expected)
    {
        Assert.NotNull(booking.Penalty);
        Assert.Same(expected, booking.Penalty!.ReasonCode);
        Assert.Equal(expected.Sentence, booking.Penalty.Reason);
    }
}
