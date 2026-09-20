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
    // How long a customer has to pay the deposit AFTER the dealer approves. TWO hours, settled by
    // the owner on 2026-09-11, replacing the twenty-four they set on 2026-09-07 with the
    // reserve-now-pay-later reordering.
    //
    // It is NOT MinimumBookingLeadTimeMinutes, which is also 120 and means something else entirely:
    // that one is how far ahead of NOW a rental may start, and this one is how long after an
    // APPROVAL the deposit may go unpaid. They are two clocks that happen to be the same length
    // today, and moving one must never move the other.
    //
    // The shorter window costs something the owner has accepted: there is no push channel yet
    // (pre-launch item 73), so a customer learns of an approval by opening the app, and two hours is
    // easy to miss. What it buys is a car released back to the market in two hours instead of a day.
    // See pre-launch item 90.
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
    // owner at 120 minutes on 2026-09-07 and raised to 240 on 2026-09-11.
    //
    // It exists because every window on a booking ends at the rental start: without a floor, a
    // request made twenty minutes before pickup gives the dealer twenty minutes to answer, the
    // customer whatever is left to pay, and no free cancellation at all -- while the platform is
    // telling that same customer they have a payment window and the gallery that it has an answer
    // window. A lead time is what makes those promises keepable.
    //
    // It had to GROW when the owner ruled that an approval must leave the customer their whole
    // payment window. The two are no longer independent: a gallery may answer only up to
    // `rental start - PaymentWindow`, so the DIFFERENCE between this number and that one is the
    // entire time a gallery has to answer a request made at the earliest a customer may book for.
    // Equal values give it zero and every such request is born unapprovable, which is why startup
    // refuses a configuration where this does not strictly exceed PaymentWindowHours.
    //
    // 240 is two hours of payment window plus two hours for a rental office to notice and answer.
    // SETTLED by the owner on 2026-09-11, both halves: the customer keeps a full two hours to pay,
    // and a gallery gets roughly two hours to decide on a last-minute request.
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
    // Settled by the owner at 15 MINUTES on 2026-09-08. The asymmetry with the gallery's 8-hour wait
    // is deliberate: a customer standing at a counter knows within minutes that nobody is coming,
    // while a gallery holding a car cannot tell a late renter from an absent one for hours. Minutes
    // rather than hours because the decision is not expressible in hours at all.
    // Frozen onto each booking like every other rule.
    int NonDeliveryGraceMinutes,
    // How long after a rental finishes either party may review it, and -- the same number -- how long
    // a first review stays hidden waiting for the second.
    //
    // One number rather than two, because they are the same instant seen from both ends: the second
    // party's deadline to submit IS the first review's reveal, which is what makes "nobody sees the
    // counterpart before submitting" true by construction instead of by checking.
    //
    // A PROPOSAL at 14 days, not a decision. The owner has not been asked; nothing in the spec names
    // a figure. See pre-launch item 80.
    int ReviewWindowDays,
    // The oldest model year a dealer may list. A guard against a mistyped year, not a statement
    // about what is worth renting; the console builds its year list from it so the two cannot drift.
    int EarliestVehicleModelYear,
    // How many cars one customer may keep on their shortlist. A guard against a list nobody can read
    // and a table one account can grow without bound -- not a judgement about how many cars are worth
    // comparing. Configured rather than constant because it is a figure a screen states, and this
    // project's rule is that such a figure is the owner's to move.
    //
    // SETTLED at 100 by the owner on 2026-09-11, replacing the 50 that was proposed when the context
    // was built. The app never holds a copy: the refusal carries the figure and the screen repeats
    // what it was told.
    int MaxShortlistEntries);

public interface IBusinessRulesProvider
{
    Task<BusinessRules> GetAsync(CancellationToken cancellationToken = default);
}
