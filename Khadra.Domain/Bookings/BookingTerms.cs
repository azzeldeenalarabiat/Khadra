using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.Bookings;

// The business rules AS THEY STOOD when the booking was made, frozen onto the booking.
//
// This exists because rules are admin-editable. If cancellation were judged against the current
// settings, an admin shortening the free window would retroactively penalise customers who booked
// under the old one, and no past decision could be reproduced. Every later judgement on this booking
// reads these values, never the live settings.
public sealed class BookingTerms : ValueObject
{
    public Percentage DepositPercent { get; }
    // Recorded for traceability. Commission itself is calculated in the Payments context.
    public Percentage CommissionPercent { get; }
    // Spec 2: no penalty for cancelling within this window after dealer approval.
    public TimeSpan FreeCancellationWindow { get; }
    // Spec 2: 8 hours after start with no pickup.
    public TimeSpan NoShowTimeout { get; }
    // How long the customer has to pay the deposit before the held vehicle is released.
    public TimeSpan PaymentWindow { get; }
    // Quiet period after return; if nobody disputes, the booking completes on its own.
    public TimeSpan PostReturnSettlementWindow { get; }
    // Spec 2: penalty on a customer who cancels after the free window.
    public Percentage CustomerCancellationPenaltyPercent { get; }
    // Spec 2.2: 25%-50%, tier undecided, so the range travels with the booking and an admin picks
    // inside it when a ticket is actually opened.
    public Percentage DealerPenaltyMinPercent { get; }
    public Percentage DealerPenaltyMaxPercent { get; }
    // The gap a gallery needs between one rental coming back and the next going out, to clean,
    // refuel and inspect. Settled by the owner at two hours on 2026-09-07.
    //
    // Frozen like every other rule here, and for the same reason — but this one also has a physical
    // consequence, because it is what Booking.HoldStart is derived from and the database enforces
    // that hold. Zero is a legitimate value meaning back-to-back rentals are allowed.
    public TimeSpan TurnaroundBuffer { get; }
    public int RulesVersion { get; }

#pragma warning disable CS8618 // EF materialises this value object by writing its backing fields;
    // the public factories remain the only way application code can create one.
    private BookingTerms()
    {
    }
#pragma warning restore CS8618

    private BookingTerms(
        Percentage depositPercent,
        Percentage commissionPercent,
        TimeSpan freeCancellationWindow,
        TimeSpan noShowTimeout,
        TimeSpan paymentWindow,
        TimeSpan postReturnSettlementWindow,
        Percentage customerCancellationPenaltyPercent,
        Percentage dealerPenaltyMinPercent,
        Percentage dealerPenaltyMaxPercent,
        TimeSpan turnaroundBuffer,
        int rulesVersion)
    {
        DepositPercent = depositPercent;
        CommissionPercent = commissionPercent;
        FreeCancellationWindow = freeCancellationWindow;
        NoShowTimeout = noShowTimeout;
        PaymentWindow = paymentWindow;
        PostReturnSettlementWindow = postReturnSettlementWindow;
        CustomerCancellationPenaltyPercent = customerCancellationPenaltyPercent;
        DealerPenaltyMinPercent = dealerPenaltyMinPercent;
        DealerPenaltyMaxPercent = dealerPenaltyMaxPercent;
        TurnaroundBuffer = turnaroundBuffer;
        RulesVersion = rulesVersion;
    }

    public static Result<BookingTerms, Error> Create(
        Percentage depositPercent,
        Percentage commissionPercent,
        TimeSpan freeCancellationWindow,
        TimeSpan noShowTimeout,
        TimeSpan paymentWindow,
        TimeSpan postReturnSettlementWindow,
        Percentage customerCancellationPenaltyPercent,
        Percentage dealerPenaltyMinPercent,
        Percentage dealerPenaltyMaxPercent,
        TimeSpan turnaroundBuffer,
        int rulesVersion)
    {
        ArgumentNullException.ThrowIfNull(depositPercent);
        ArgumentNullException.ThrowIfNull(commissionPercent);
        ArgumentNullException.ThrowIfNull(customerCancellationPenaltyPercent);
        ArgumentNullException.ThrowIfNull(dealerPenaltyMinPercent);
        ArgumentNullException.ThrowIfNull(dealerPenaltyMaxPercent);

        if (commissionPercent.IsGreaterThan(depositPercent))
        {
            return Error.Validation(
                "booking.commission_exceeds_deposit",
                "The commission percentage cannot exceed the deposit percentage, because commission is collected from the deposit.");
        }

        if (dealerPenaltyMinPercent.IsGreaterThan(dealerPenaltyMaxPercent))
        {
            return Error.Validation(
                "booking.penalty_range_inverted",
                "The maximum dealer penalty must be at least the minimum.");
        }

        if (freeCancellationWindow < TimeSpan.Zero || noShowTimeout <= TimeSpan.Zero ||
            paymentWindow <= TimeSpan.Zero || postReturnSettlementWindow < TimeSpan.Zero ||
            turnaroundBuffer < TimeSpan.Zero)
        {
            return Error.Validation("booking.invalid_terms", "Booking terms carry an invalid time window.");
        }

        return new BookingTerms(
            depositPercent,
            commissionPercent,
            freeCancellationWindow,
            noShowTimeout,
            paymentWindow,
            postReturnSettlementWindow,
            customerCancellationPenaltyPercent,
            dealerPenaltyMinPercent,
            dealerPenaltyMaxPercent,
            turnaroundBuffer,
            rulesVersion);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DepositPercent;
        yield return CommissionPercent;
        yield return FreeCancellationWindow;
        yield return NoShowTimeout;
        yield return PaymentWindow;
        yield return PostReturnSettlementWindow;
        yield return CustomerCancellationPenaltyPercent;
        yield return DealerPenaltyMinPercent;
        yield return DealerPenaltyMaxPercent;
        yield return TurnaroundBuffer;
        yield return RulesVersion;
    }
}
