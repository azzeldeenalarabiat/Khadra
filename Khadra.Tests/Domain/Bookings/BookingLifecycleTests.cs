using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Events;
using Khadra.Domain.Common;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Bookings;

public sealed class BookingCreationTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    [Fact]
    public void A_new_booking_holds_the_vehicle_while_the_dealer_decides()
    {
        var booking = Build.Booking();

        Assert.Same(BookingStatus.Requested, booking.Status);
        Assert.True(booking.OccupiesVehicle);
        Assert.False(booking.CanBeReviewed);
        Assert.StartsWith("KH-", booking.Reference.Value, StringComparison.Ordinal);
        Assert.Equal(Now, booking.RequestedAt);
        Assert.Equal(Now.AddHours(48), booking.DecisionDeadline);
        // Nothing is owed until a dealer says yes, so no payment clock is running yet.
        Assert.Null(booking.PaymentDeadline);
        Assert.Null(booking.DepositPaymentId);
    }

    /// <summary>
    /// A request for a car due out in an hour cannot sit unanswered for two days: the answer window
    /// is capped at the rental start, the same way every other window on a booking is.
    /// </summary>
    [Fact]
    public void The_answer_window_never_runs_past_the_rental_start()
    {
        var start = Now.AddHours(1);
        var period = DateRange.Create(start, start.AddDays(2)).Value;

        var booking = Build.Booking(period: period);

        Assert.Equal(start, booking.DecisionDeadline);
    }

    [Fact]
    public void A_booking_cannot_start_in_the_past()
    {
        var period = DateRange.Create(Now.AddDays(-1), Now.AddDays(2)).Value;

        var booking = Booking.Create(
            Id.New(), Id.New(), Id.New(), period, PickupMethod.SelfPickup, null,
            Build.Pricing(days: 3), Build.Terms(), PaymentOption.DepositOnly, Now);

        Assert.Equal("booking.period_in_past", booking.Error.Code);
    }

    [Fact]
    public void Delivery_needs_a_location_and_self_pickup_forbids_one()
    {
        var period = Build.Period(Now.AddDays(7));

        var deliveryWithout = Booking.Create(
            Id.New(), Id.New(), Id.New(), period, PickupMethod.Delivery, null,
            Build.Pricing(), Build.Terms(), PaymentOption.DepositOnly, Now);
        var pickupWith = Booking.Create(
            Id.New(), Id.New(), Id.New(), period, PickupMethod.SelfPickup, Build.Amman,
            Build.Pricing(), Build.Terms(), PaymentOption.DepositOnly, Now);

        Assert.Equal("booking.delivery_location_required", deliveryWithout.Error.Code);
        Assert.Equal("booking.delivery_location_not_allowed", pickupWith.Error.Code);
    }

    /// <summary>
    /// Pricing that describes a different rental than the one being booked is a bug in the handler,
    /// not something a customer can provoke, so it throws rather than returning an error a screen
    /// would have to explain.
    /// </summary>
    /// <remarks>
    /// The aggregate cannot check this exactly: the frozen dates are LOCAL and the domain has no
    /// time zone to convert the period with. What it can say without one is that no real zone sits
    /// more than a day from UTC, so a frozen date further than that from the same instant's UTC date
    /// can only mean the two were computed from different periods. The exact equality — that the
    /// dates are the period seen through IReportingCalendar — belongs in the handler that has the
    /// calendar, and is tested there.
    /// </remarks>
    [Fact]
    public void Pricing_for_a_different_period_is_a_programming_error()
    {
        var period = Build.Period(Now.AddDays(7), days: 3);

        // Priced for five days against a three-day period: two days adrift, well past the one day
        // any time zone could account for.
        Assert.Throws<DomainException>(() => Booking.Create(
            Id.New(), Id.New(), Id.New(), period, PickupMethod.SelfPickup, null,
            Build.Pricing(days: 5), Build.Terms(), PaymentOption.DepositOnly, Now));
    }

    /// <summary>
    /// A booking claims the car earlier than the customer's period starts, by the turnaround gap
    /// frozen onto its terms — the time the gallery needs to clean and check it between renters.
    /// </summary>
    [Fact]
    public void A_booking_claims_the_car_from_before_the_customer_collects_it()
    {
        var period = Build.Period(Now.AddDays(7), days: 3);

        var booking = Booking.Create(
            Id.New(), Id.New(), Id.New(), period, PickupMethod.SelfPickup, null,
            Build.Pricing(days: 3), Build.Terms(turnaroundBuffer: TimeSpan.FromHours(2)),
            PaymentOption.DepositOnly, Now).Value;

        Assert.Equal(period.Start.AddHours(-2), booking.HoldStart);
        // The customer still pays for, and collects at, the period they chose.
        Assert.Equal(period.Start, booking.Period.Start);
    }

    /// <summary>
    /// An extension carries no gap. It continues a rental the customer never gave back, so there is
    /// no handover to prepare for — and a leading pad would collide with the parent booking it
    /// starts against, making the extension impossible to store at all.
    /// </summary>
    [Fact]
    public void An_extension_does_not_claim_a_turnaround_gap_against_its_own_parent()
    {
        var period = Build.Period(Now.AddDays(7), days: 3);

        var extension = Booking.Create(
            Id.New(), Id.New(), Id.New(), period, PickupMethod.SelfPickup, null,
            Build.Pricing(days: 3), Build.Terms(turnaroundBuffer: TimeSpan.FromHours(2)),
            PaymentOption.DepositOnly, Now, extendedFromBookingId: Id.New()).Value;

        Assert.Equal(period.Start, extension.HoldStart);
    }

    [Fact]
    public void Creation_is_recorded_in_the_status_history_from_the_very_first_state()
    {
        var booking = Build.Booking();

        var entry = Assert.Single(booking.StatusHistory);
        Assert.Null(entry.From);
        Assert.Same(BookingStatus.Requested, entry.To);
    }
}

public sealed class BookingPaymentAndExpiryTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    [Fact]
    public void Paying_the_deposit_confirms_the_booking_and_opens_the_free_window()
    {
        var booking = Build.ApprovedBooking();
        var payment = Id.New();

        Assert.True(booking.ConfirmDepositPaid(payment, Now).IsSuccess);

        Assert.Same(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(payment, booking.DepositPaymentId);
        // Free cancellation runs from PAYMENT, not from approval: at approval nothing had been paid,
        // so there was nothing a penalty could be assessed against.
        Assert.Equal(Now.AddHours(1), booking.FreeCancellationDeadline);
        Assert.Contains(booking.DomainEvents, domainEvent => domainEvent is BookingConfirmed);
    }

    [Fact]
    public void A_retried_gateway_webhook_is_accepted_without_changing_anything()
    {
        var booking = Build.ApprovedBooking();
        var payment = Id.New();
        booking.ConfirmDepositPaid(payment, Now);
        booking.ClearDomainEvents();
        var historyBefore = booking.StatusHistory.Count;

        var retry = booking.ConfirmDepositPaid(payment, Now.AddSeconds(30));

        Assert.True(retry.IsSuccess);
        Assert.Equal(Now.AddHours(1), booking.FreeCancellationDeadline);
        Assert.Empty(booking.DomainEvents);
        Assert.Equal(historyBefore, booking.StatusHistory.Count);
    }

    [Fact]
    public void A_different_payment_cannot_confirm_an_already_paid_booking()
    {
        var booking = Build.ConfirmedBooking();

        Assert.Equal("booking.not_awaiting_payment", booking.ConfirmDepositPaid(Id.New(), Now).Error.Code);
    }

    [Fact]
    public void A_deposit_cannot_be_paid_before_the_dealer_has_approved()
    {
        var booking = Build.Booking();

        Assert.Equal("booking.not_awaiting_payment", booking.ConfirmDepositPaid(Id.New(), Now).Error.Code);
    }

    [Fact]
    public void An_approved_booking_nobody_paid_for_expires_and_releases_the_vehicle()
    {
        var booking = Build.ApprovedBooking();

        Assert.Equal("booking.payment_window_open", booking.ExpireUnpaid(Now.AddMinutes(19)).Error.Code);
        Assert.True(booking.ExpireUnpaid(Now.AddMinutes(20)).IsSuccess);

        Assert.Same(BookingStatus.Expired, booking.Status);
        Assert.False(booking.OccupiesVehicle);
        Assert.True(booking.Status.IsTerminal);
        // No money moved, so there is nothing to assess a penalty against.
        Assert.True(booking.Penalty!.IsNothingOwed);
        Assert.Same(BookingParty.Unattributed, booking.Penalty.AttributedTo);
        var expired = Assert.Single(booking.DomainEvents.OfType<BookingExpired>());
        Assert.Equal("PaymentWindowElapsed", expired.Reason);
    }

    /// <summary>
    /// A request costs nothing now, so the answer window is the only thing between one account and a
    /// car held for the whole booking horizon. Spec 3.1 always promised an answer within it.
    /// </summary>
    [Fact]
    public void A_request_the_dealer_never_answered_expires_at_the_answer_deadline_with_nothing_owed()
    {
        var booking = Build.Booking();
        var deadline = booking.DecisionDeadline;

        Assert.Equal("booking.decision_window_not_elapsed", booking.ExpireUnanswered(deadline.AddMinutes(-1)).Error.Code);
        Assert.True(booking.ExpireUnanswered(deadline).IsSuccess);

        Assert.Same(BookingStatus.Expired, booking.Status);
        Assert.False(booking.OccupiesVehicle);
        Assert.True(booking.Penalty!.IsNothingOwed);
        var expired = Assert.Single(booking.DomainEvents.OfType<BookingExpired>());
        Assert.Equal("DealerDidNotRespond", expired.Reason);
    }

    [Fact]
    public void Expiry_only_applies_to_the_state_it_belongs_to()
    {
        var confirmed = Build.ConfirmedBooking();
        var requested = Build.Booking();
        var approved = Build.ApprovedBooking();

        Assert.Equal("booking.not_awaiting_payment", confirmed.ExpireUnpaid(Now.AddDays(30)).Error.Code);
        Assert.Equal("booking.not_awaiting_decision", confirmed.ExpireUnanswered(Now.AddDays(30)).Error.Code);
        // The two windows are consecutive, never concurrent: whichever clock is running, the other
        // job must leave the booking alone.
        Assert.Equal("booking.not_awaiting_payment", requested.ExpireUnpaid(Now.AddDays(30)).Error.Code);
        Assert.Equal("booking.not_awaiting_decision", approved.ExpireUnanswered(Now.AddDays(30)).Error.Code);
    }
}

public sealed class BookingApprovalTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    [Fact]
    public void Approval_records_the_acting_staff_member_and_opens_the_payment_window()
    {
        var booking = Build.Booking();
        var employee = Id.New();

        Assert.True(booking.Approve(employee, Now).IsSuccess);

        Assert.Same(BookingStatus.Approved, booking.Status);
        Assert.Equal(employee, booking.ActedByUserId);
        Assert.Equal(Now.AddMinutes(20), booking.PaymentDeadline);
        // Approval commits the dealer, not the customer. Nothing is free to cancel yet, because
        // nothing has been paid.
        Assert.Null(booking.FreeCancellationDeadline);
        Assert.Contains(booking.DomainEvents, domainEvent => domainEvent is BookingApproved);
    }

    [Fact]
    public void The_payment_window_never_runs_past_the_rental_start()
    {
        // Approved ten minutes before pickup: a full 20-minute window would leave the deposit falling
        // due after the car was already meant to be collected.
        var start = Now.AddMinutes(10);
        var period = DateRange.Create(start, start.AddDays(2)).Value;
        var booking = Build.Booking(period: period);

        booking.Approve(Id.New(), Now);

        Assert.Equal(start, booking.PaymentDeadline);
    }

    [Fact]
    public void The_free_cancellation_window_never_runs_past_the_rental_start()
    {
        // Paid 20 minutes before pickup: a full hour of free cancellation would let the customer
        // walk away after the car was already due to be collected.
        var start = Now.AddMinutes(20);
        var period = DateRange.Create(start, start.AddDays(2)).Value;
        var booking = Build.Booking(period: period);
        booking.Approve(Id.New(), Now);

        booking.ConfirmDepositPaid(Id.New(), Now);

        Assert.Equal(start, booking.FreeCancellationDeadline);
    }

    [Fact]
    public void Rejection_needs_a_reason_and_costs_the_customer_nothing()
    {
        var booking = Build.Booking();

        Assert.Equal("booking.reason_required", booking.Reject(Id.New(), " ", Now).Error.Code);
        Assert.True(booking.Reject(Id.New(), "The car is in for service.", Now).IsSuccess);

        Assert.Same(BookingStatus.Rejected, booking.Status);
        Assert.True(booking.Penalty!.IsNothingOwed);
        Assert.False(booking.OccupiesVehicle);
    }

    [Fact]
    public void A_decision_can_only_be_made_while_the_booking_is_awaiting_one()
    {
        var booking = Build.ApprovedBooking();

        Assert.Equal("booking.not_awaiting_decision", booking.Approve(Id.New(), Now).Error.Code);
        Assert.Equal("booking.not_awaiting_decision", booking.Reject(Id.New(), "no", Now).Error.Code);
    }

    /// <summary>
    /// Past the answer window the catalogue has already released the car, so approving now can only
    /// collide with whoever took it. Rejecting late costs nobody anything and closes the record
    /// honestly, so it is still allowed.
    /// </summary>
    [Fact]
    public void A_dealer_who_missed_the_window_may_still_reject_but_can_no_longer_approve()
    {
        var toApprove = Build.Booking();
        var toReject = Build.Booking();
        var late = toApprove.DecisionDeadline;

        Assert.Equal("booking.decision_window_elapsed", toApprove.Approve(Id.New(), late).Error.Code);
        Assert.True(toReject.Reject(Id.New(), "Sorry, we missed this.", late).IsSuccess);
    }
}

public sealed class BookingCancellationTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    /// <summary>
    /// Nothing has been paid before the deposit clears, so there is no money a penalty could bite
    /// on. That covers both states now: a request nobody has answered, and an approval the customer
    /// has not paid for.
    /// </summary>
    [Fact]
    public void Cancelling_before_the_deposit_is_paid_is_always_free()
    {
        var requested = Build.Booking();
        var approved = Build.ApprovedBooking();

        Assert.True(requested.Cancel(BookingParty.Customer, Id.New(), "changed plans", Now).IsSuccess);
        Assert.True(approved.Cancel(BookingParty.Customer, Id.New(), "changed plans", Now).IsSuccess);

        Assert.True(requested.Penalty!.IsNothingOwed);
        Assert.True(approved.Penalty!.IsNothingOwed);
    }

    [Fact]
    public void Cancelling_inside_the_free_window_after_paying_costs_nothing()
    {
        var booking = Build.ConfirmedBooking();

        Assert.True(booking.Cancel(BookingParty.Customer, Id.New(), "changed plans", Now.AddMinutes(59)).IsSuccess);

        Assert.True(booking.Penalty!.IsNothingOwed);
        Assert.Same(BookingStatus.Cancelled, booking.Status);
    }

    [Fact]
    public void A_customer_cancelling_after_the_window_is_assessed_against_their_deposit()
    {
        var booking = Build.ConfirmedBooking();

        booking.Cancel(BookingParty.Customer, Id.New(), "changed plans", Now.AddHours(2));

        var penalty = booking.Penalty!;
        Assert.Same(BookingParty.Customer, penalty.AttributedTo);
        // 3 days at 30 JOD is 90; the 20% deposit is 18, and the whole deposit is at stake.
        Assert.Equal(Money.Jod(18m), penalty.MaxAmount);
        Assert.False(penalty.IsRange);
        // Spec 3.3: assessed only. Nothing is charged unless someone opens a ticket.
        Assert.True(penalty.RequiresTicketToEnforce);
    }

    [Fact]
    public void A_dealer_cancelling_after_the_window_is_assessed_as_a_range_for_an_admin_to_settle()
    {
        var booking = Build.ConfirmedBooking();

        booking.Cancel(BookingParty.Dealer, Id.New(), "double booked", Now.AddHours(2));

        var penalty = booking.Penalty!;
        Assert.Same(BookingParty.Dealer, penalty.AttributedTo);
        Assert.True(penalty.IsRange);
        // 25% to 50% of the 90 JOD rental value.
        Assert.Equal(Money.Jod(22.5m), penalty.MinAmount);
        Assert.Equal(Money.Jod(45m), penalty.MaxAmount);
    }

    [Fact]
    public void Reporting_dealer_non_delivery_cancels_the_booking_against_the_dealer()
    {
        var booking = Build.ConfirmedBooking();
        var customer = Id.New();

        Assert.True(booking.ReportDealerNonDelivery(customer, "Nobody showed up with the car.", Now.AddHours(3)).IsSuccess);

        Assert.Same(BookingStatus.Cancelled, booking.Status);
        Assert.Same(BookingParty.Dealer, booking.Penalty!.AttributedTo);
        Assert.True(booking.Penalty.IsRange);
        var cancelled = Assert.Single(booking.DomainEvents.OfType<BookingCancelled>());
        Assert.Equal("Dealer", cancelled.AttributedToParty);
        Assert.True(cancelled.RequiresTicketToEnforce);
    }

    [Fact]
    public void Non_delivery_can_only_be_reported_on_a_confirmed_booking_and_needs_a_reason()
    {
        var unpaid = Build.ApprovedBooking();
        var confirmed = Build.ConfirmedBooking();

        // A dealer who never turned up with a car nobody paid for owes nothing: the booking had not
        // committed either party yet, and it expires on its own.
        Assert.Equal("booking.not_confirmed", unpaid.ReportDealerNonDelivery(Id.New(), "nothing", Now).Error.Code);
        Assert.Equal("booking.reason_required", confirmed.ReportDealerNonDelivery(Id.New(), " ", Now).Error.Code);
    }

    [Fact]
    public void A_finished_booking_can_no_longer_be_cancelled()
    {
        var booking = Build.ConfirmedBooking();
        booking.Cancel(BookingParty.Customer, Id.New(), "changed plans", Now);

        Assert.Equal("booking.cannot_cancel", booking.Cancel(BookingParty.Dealer, Id.New(), "again", Now).Error.Code);
    }

    [Fact]
    public void Cancellation_terms_come_from_the_booking_not_from_todays_settings()
    {
        // Booked when the free window was a generous six hours.
        var generous = Build.Terms(freeCancellationWindow: TimeSpan.FromHours(6));
        var booking = Build.ConfirmedBooking(terms: generous);

        // Even though the platform may since have tightened the window, this booking keeps its own.
        booking.Cancel(BookingParty.Customer, Id.New(), "changed plans", Now.AddHours(5));

        Assert.True(booking.Penalty!.IsNothingOwed);
    }
}

public sealed class BookingNoShowTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    [Fact]
    public void The_no_show_timer_only_fires_after_the_configured_window()
    {
        var booking = Build.ConfirmedBooking();
        var start = booking.Period.Start;

        Assert.Equal("booking.no_show_too_early", booking.MarkNoShow(start.AddHours(7)).Error.Code);
        Assert.True(booking.MarkNoShow(start.AddHours(8)).IsSuccess);
        Assert.Same(BookingStatus.NoShow, booking.Status);
    }

    [Fact]
    public void A_self_pickup_no_show_is_attributed_to_the_customer_for_the_whole_deposit()
    {
        var booking = Build.ConfirmedBooking(pickupMethod: PickupMethod.SelfPickup);

        booking.MarkNoShow(booking.Period.Start.AddHours(8));

        var penalty = booking.Penalty!;
        Assert.Same(BookingParty.Customer, penalty.AttributedTo);
        Assert.Equal(Money.Jod(18m), penalty.MaxAmount);
        Assert.True(penalty.RequiresTicketToEnforce);
    }

    [Fact]
    public void A_delivery_no_show_blames_nobody_because_the_dealer_was_the_one_travelling()
    {
        var booking = Build.ConfirmedBooking(pickupMethod: PickupMethod.Delivery);

        booking.MarkNoShow(booking.Period.Start.AddHours(8));

        var penalty = booking.Penalty!;
        Assert.Same(BookingParty.Unattributed, penalty.AttributedTo);
        Assert.True(penalty.IsNothingOwed);
        var marked = Assert.Single(booking.DomainEvents.OfType<BookingMarkedNoShow>());
        Assert.Equal("Unattributed", marked.AttributedToParty);
    }

    [Fact]
    public void A_collected_vehicle_can_never_be_marked_a_no_show()
    {
        var booking = Build.ConfirmedBooking();
        booking.RecordPickup(BookingParty.Dealer, Id.New(), booking.Period.Start);

        Assert.Equal("booking.not_confirmed", booking.MarkNoShow(booking.Period.Start.AddHours(8)).Error.Code);
    }
}

public sealed class BookingHandoverAndSettlementTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    [Fact]
    public void Pickup_and_return_record_optional_evidence_and_the_cash_actually_collected()
    {
        var booking = Build.ConfirmedBooking();
        var start = booking.Period.Start;

        var pickup = booking.RecordPickup(
            BookingParty.Dealer, Id.New(), start,
            photoStorageKeys: ["handover/1.jpg", "handover/2.jpg"],
            odometerKm: 45_000,
            fuelLevel: 1m,
            cashCollected: Money.Jod(72m));

        Assert.True(pickup.IsSuccess);
        Assert.Same(BookingStatus.PickedUp, booking.Status);
        Assert.Equal(2, pickup.Value.PhotoStorageKeys.Count);
        Assert.Equal(Money.Jod(72m), pickup.Value.CashCollected);

        var returned = booking.RecordReturn(BookingParty.Customer, Id.New(), start.AddDays(3), odometerKm: 45_600);
        Assert.True(returned.IsSuccess);
        Assert.Same(BookingStatus.Returned, booking.Status);
        Assert.Equal(2, booking.Handovers.Count);
    }

    [Fact]
    public void Each_handover_can_only_be_recorded_once_and_only_in_the_right_state()
    {
        var booking = Build.ConfirmedBooking();
        var start = booking.Period.Start;

        Assert.Equal("booking.not_picked_up", booking.RecordReturn(BookingParty.Dealer, Id.New(), start).Error.Code);
        booking.RecordPickup(BookingParty.Dealer, Id.New(), start);
        Assert.Equal("booking.not_confirmed", booking.RecordPickup(BookingParty.Dealer, Id.New(), start).Error.Code);

        booking.RecordReturn(BookingParty.Dealer, Id.New(), start.AddDays(3));
        Assert.Equal("booking.not_picked_up", booking.RecordReturn(BookingParty.Dealer, Id.New(), start.AddDays(3)).Error.Code);
    }

    [Fact]
    public void Implausible_handover_readings_are_rejected()
    {
        var booking = Build.ConfirmedBooking();
        var start = booking.Period.Start;

        Assert.Equal("handover.invalid_odometer",
            booking.RecordPickup(BookingParty.Dealer, Id.New(), start, odometerKm: -1).Error.Code);
        Assert.Equal("handover.invalid_fuel",
            booking.RecordPickup(BookingParty.Dealer, Id.New(), start, fuelLevel: 1.5m).Error.Code);
    }

    [Fact]
    public void A_returned_booking_completes_itself_once_the_quiet_period_passes()
    {
        var booking = Build.ConfirmedBooking();
        var start = booking.Period.Start;
        booking.RecordPickup(BookingParty.Dealer, Id.New(), start);
        var returnedAt = start.AddDays(3);
        booking.RecordReturn(BookingParty.Dealer, Id.New(), returnedAt);

        Assert.Equal("booking.settlement_too_early", booking.Settle(returnedAt.AddHours(47), hasOpenDispute: false).Error.Code);
        Assert.True(booking.Settle(returnedAt.AddHours(48), hasOpenDispute: false).IsSuccess);

        Assert.Same(BookingStatus.Completed, booking.Status);
        Assert.True(booking.CanBeReviewed);
        Assert.False(booking.OccupiesVehicle);
        Assert.Contains(booking.DomainEvents, domainEvent => domainEvent is BookingCompleted);
    }

    [Fact]
    public void An_open_dispute_holds_the_booking_open_until_an_admin_resolves_it()
    {
        var booking = Build.ConfirmedBooking();
        var start = booking.Period.Start;
        booking.RecordPickup(BookingParty.Dealer, Id.New(), start);
        var returnedAt = start.AddDays(3);
        booking.RecordReturn(BookingParty.Dealer, Id.New(), returnedAt);

        var blocked = booking.Settle(returnedAt.AddDays(10), hasOpenDispute: true);
        Assert.True(blocked.IsFailure);
        // Not "already finished": the settlement job's log has to say which of the two happened.
        Assert.Equal("booking.dispute_open", blocked.Error.Code);
        Assert.Same(BookingStatus.Returned, booking.Status);

        // Resolving the dispute closes the booking immediately, without waiting out the window again.
        Assert.True(booking.CloseAfterDisputeResolved(Id.New(), returnedAt.AddHours(2)).IsSuccess);
        Assert.Same(BookingStatus.Completed, booking.Status);
    }

    [Fact]
    public void Settlement_requires_the_vehicle_to_have_come_back()
    {
        var booking = Build.ConfirmedBooking();

        Assert.Equal("booking.not_returned", booking.Settle(Now.AddDays(30), false).Error.Code);
        Assert.Equal("booking.not_returned", booking.CloseAfterDisputeResolved(Id.New(), Now).Error.Code);
    }

    [Fact]
    public void Closing_a_terminal_booking_after_a_dispute_succeeds_without_changing_it()
    {
        // Most disputes are opened on cancellations and no-shows, which are already terminal. There is
        // nothing to transition, and failing would make the resolve handler's outcome depend on how the
        // booking happened to end -- which is not the Admin's problem.
        var booking = Build.ConfirmedBooking();
        booking.Cancel(BookingParty.Customer, Id.New(), "Plans changed.", Now.AddDays(1));

        var result = booking.CloseAfterDisputeResolved(Id.New(), Now.AddDays(2));

        Assert.True(result.IsSuccess);
        Assert.Same(BookingStatus.Cancelled, booking.Status);
    }

    [Fact]
    public void A_booking_is_disputable_only_while_its_own_frozen_window_is_open()
    {
        var terms = Build.Terms(settlementWindow: TimeSpan.FromDays(7));
        var booking = Build.ConfirmedBooking(terms: terms);
        var start = booking.Period.Start;
        booking.RecordPickup(BookingParty.Dealer, Id.New(), start);

        Assert.False(booking.CanBeDisputed(start));

        var returnedAt = start.AddDays(3);
        booking.RecordReturn(BookingParty.Dealer, Id.New(), returnedAt);

        Assert.True(booking.CanBeDisputed(returnedAt.AddDays(6)));
        Assert.False(booking.CanBeDisputed(returnedAt.AddDays(8)));
    }

    [Fact]
    public void A_cancelled_booking_is_disputable_from_when_it_finished()
    {
        var terms = Build.Terms(settlementWindow: TimeSpan.FromDays(7));
        var booking = Build.ConfirmedBooking(terms: terms);
        var cancelledAt = Now.AddDays(1);
        booking.Cancel(BookingParty.Customer, Id.New(), "Plans changed.", cancelledAt);

        Assert.True(booking.CanBeDisputed(cancelledAt.AddDays(6)));
        Assert.False(booking.CanBeDisputed(cancelledAt.AddDays(8)));
    }

    [Fact]
    public void A_completed_booking_can_no_longer_be_disputed()
    {
        // Reaching Completed IS the window having elapsed, so a dispute afterwards would reopen a
        // closed financial record.
        var booking = Build.ConfirmedBooking();
        var start = booking.Period.Start;
        booking.RecordPickup(BookingParty.Dealer, Id.New(), start);
        var returnedAt = start.AddDays(3);
        booking.RecordReturn(BookingParty.Dealer, Id.New(), returnedAt);
        booking.Settle(returnedAt.AddDays(30), hasOpenDispute: false);

        Assert.Same(BookingStatus.Completed, booking.Status);
        Assert.False(booking.CanBeDisputed(returnedAt.AddDays(30)));
    }

    [Fact]
    public void The_whole_journey_is_written_to_the_status_history()
    {
        var booking = Build.Booking();
        var start = booking.Period.Start;
        booking.Approve(Id.New(), Now);
        booking.ConfirmDepositPaid(Id.New(), Now);
        booking.RecordPickup(BookingParty.Dealer, Id.New(), start);
        booking.RecordReturn(BookingParty.Dealer, Id.New(), start.AddDays(3));
        booking.Settle(start.AddDays(5), false);

        var journey = booking.StatusHistory.Select(change => change.To.Name).ToArray();

        Assert.Equal(
            ["Requested", "Approved", "Confirmed", "PickedUp", "Returned", "Completed"],
            journey);
        Assert.All(booking.StatusHistory, change => Assert.NotNull(change.ActorParty));
    }
}
