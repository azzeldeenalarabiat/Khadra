using Khadra.Domain.Common;

namespace Khadra.Application.Common.Ports;

// The confirmed business numbers from the spec (section 2). Configuration today; an admin-editable
// PlatformSettings aggregate later. Consumers only ever see this snapshot.
//
// A booking freezes these values onto itself at creation (BookingTerms), so tuning a number here
// never changes the terms of a booking that was already made.
// The delivery fee is deliberately NOT here. It used to be: one configured figure every gallery had
// to charge. The owner moved it onto the dealership that performs the delivery, so it now lives on
// `DeliverySettings` and varies by gallery — which means there is no platform-wide answer to “what
// does delivery cost”, and a caller that wants one has to name whose delivery it is asking about.
public sealed record BusinessRules(
    decimal CommissionPercent,
    decimal DepositPercent,
    int NoShowTimeoutHours,
    decimal DealerNonDeliveryPenaltyMinPercent,
    decimal DealerNonDeliveryPenaltyMaxPercent,
    int FreeCancellationWindowMinutes,
    int AdminSlaHours,
    // Spec 5.5 says a penalty applies to a customer who cancels late but never names the figure.
    // 100% of the deposit is the reading consistent with "deposit is forfeited" on a no-show.
    // Awaiting the owner's confirmation.
    decimal CustomerCancellationPenaltyPercent,
    // How long a customer has to pay the deposit AFTER the dealer approves. Twenty-four hours,
    // settled by the owner on 2026-09-07 with the reserve-now-pay-later reordering. Hours rather
    // than minutes because 1440 reads like a typo and is one.
    int PaymentWindowHours,
    // How long the dealer has to answer a request before it expires and the car returns to the
    // market. Spec 3.1 always promised 48 hours; nothing enforced it, because a deposit gated the
    // hold. Its own key rather than AdminSlaHours: that is the ADMIN's clock over a gallery's
    // licence, and the owner must be able to move one without the other.
    int BookingAnswerWindowHours,
    // Quiet period after the vehicle comes back; with no dispute the booking completes on its own.
    int PostReturnSettlementHours,
    // Spec 5.1: enforced at registration. Null means the owner has not set one, and nobody is
    // refused on age -- a real shipping state, not a missing value.
    int? MinimumRenterAge,
    // The gap a gallery needs between one rental ending and the next starting, to clean, refuel and
    // check the car. Settled by the owner at 120 minutes on 2026-09-07. Zero means back-to-back
    // rentals are allowed. A booking freezes this like every other rule, and also derives its
    // HoldStart from it, which is the figure the database enforces.
    int TurnaroundMinutes,
    // How far ahead a customer may pick a rental date. Settled by the owner at 180 days on
    // 2026-09-07. It is a real business trade-off rather than a UI bound: a booking FREEZES the
    // price it was made under, so a long horizon means honouring a rate the gallery set months ago.
    int MaxAdvanceBookingDays,
    // The soonest a rental may start, counted from the moment the request is made. Settled by the
    // owner at 120 minutes on 2026-09-07.
    //
    // It exists because every window on a booking is capped at the rental start: without a floor, a
    // request made twenty minutes before pickup gives the dealer twenty minutes to answer, the
    // customer whatever is left to pay, and no free cancellation at all -- while the platform is
    // telling that same customer, on /app-config, that they have 24 hours to pay. A lead time is
    // what makes those promises keepable.
    int MinimumBookingLeadTimeMinutes,
    // The longest a single rental may run, in Amman calendar days -- the same days the rental is
    // BILLED in, so the number a customer is refused on is the number they were quoted.
    //
    // It exists because nothing else bounds the far end. BookingWindowPolicy bounds when a rental may
    // START; without this, a five-year request is accepted, holds the car for the whole answer
    // window, and lands in a gallery's queue. Long hires are a real business and this is not a
    // judgement about them: it is a bound the owner can move, and a gallery that wants a six-month
    // hire can have one by moving it.
    int MaxRentalDays,
    // How long after the rental was due to start before the customer may report that the gallery
    // never handed the car over (spec 5.5). The mirror of NoShowTimeoutHours, which is the gallery's
    // wait before it may say the customer never appeared.
    //
    // OPEN OWNER DECISION, raised 2026-09-08. Shipped at 0 -- a gallery that has not handed the car
    // over at the agreed moment is already late -- but how much lateness is worth reporting is a
    // business judgement, not a developer's. Frozen onto each booking like every other rule.
    int NonDeliveryGraceHours,
    // The oldest model year a dealer may list. A guard against a mistyped year, not a statement
    // about what is worth renting; the console builds its year list from it so the two cannot drift.
    int EarliestVehicleModelYear);

public interface IBusinessRulesProvider
{
    Task<BusinessRules> GetAsync(CancellationToken cancellationToken = default);
}
