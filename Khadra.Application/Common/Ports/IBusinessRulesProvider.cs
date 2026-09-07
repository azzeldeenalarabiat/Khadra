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
    // How long a customer has to pay the deposit before the held vehicle is released to others.
    int PaymentWindowMinutes,
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
    // The oldest model year a dealer may list. A guard against a mistyped year, not a statement
    // about what is worth renting; the console builds its year list from it so the two cannot drift.
    int EarliestVehicleModelYear);

public interface IBusinessRulesProvider
{
    Task<BusinessRules> GetAsync(CancellationToken cancellationToken = default);
}
