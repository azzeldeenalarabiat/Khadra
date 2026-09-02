using Khadra.Domain.Common;

namespace Khadra.Application.Common.Ports;

// The confirmed business numbers from the spec (section 2). Configuration today; an admin-editable
// PlatformSettings aggregate later. Consumers only ever see this snapshot.
//
// A booking freezes these values onto itself at creation (BookingTerms), so tuning a number here
// never changes the terms of a booking that was already made.
public sealed record BusinessRules(
    decimal CommissionPercent,
    decimal DepositPercent,
    int NoShowTimeoutHours,
    Money DeliveryFee,
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
    int PostReturnSettlementHours);

public interface IBusinessRulesProvider
{
    Task<BusinessRules> GetAsync(CancellationToken cancellationToken = default);
}
