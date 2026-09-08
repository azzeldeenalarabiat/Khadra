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
    // Spec 2: no penalty for cancelling within this window. Spec 5.5 measures it from approval; it
    // has run from PAYMENT since 2026-09-07, because at approval nothing has been paid and there is
    // nothing to be penalised on. The duration is unchanged.
    public TimeSpan FreeCancellationWindow { get; }
    // Spec 2: 8 hours after start with no pickup.
    public TimeSpan NoShowTimeout { get; }
    // How long the customer has to pay the deposit before the held vehicle is released.
    public TimeSpan PaymentWindow { get; }

    /// <summary>How long the dealer has to answer a request before it expires.</summary>
    /// <remarks>
    /// Its own figure rather than the admin SLA it happens to match. They are different clocks
    /// owned by different people -- one is how long an administrator may take over a gallery's
    /// licence, the other how long a gallery may leave a customer waiting -- and sharing a key
    /// would mean the owner could not move one without moving the other.
    ///
    /// It matters more than it used to. A request no longer costs a deposit, so this window is the
    /// only thing between one account and a car held for the whole booking horizon.
    /// </remarks>
    public TimeSpan AnswerWindow { get; }
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
    /// <summary>
    /// How long after the rental was due to start before the customer may report that the gallery
    /// never handed the car over (spec 5.5).
    /// </summary>
    /// <remarks>
    /// The mirror image of <see cref="NoShowTimeout"/>, which is what the gallery waits before it may
    /// say the CUSTOMER never appeared. Both claims are about the same missed handover, and until
    /// 2026-09-08 only one of them was guarded: a customer could report non-delivery at any time
    /// after the deposit cleared, days before the car was due.
    ///
    /// Zero is a legitimate value and is the shipped default -- a gallery that has not handed the car
    /// over at the agreed moment is already late. It is a setting rather than a constant because how
    /// much lateness is worth reporting is a business judgement, and it is frozen here rather than
    /// read live because a grace the owner lengthens tomorrow must not un-report yesterday's claim.
    ///
    /// OPEN OWNER DECISION: the figure itself. Shipped at 0 hours; see docs/spec-amendments.md.
    /// </remarks>
    public TimeSpan NonDeliveryGrace { get; }
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
        TimeSpan answerWindow,
        TimeSpan postReturnSettlementWindow,
        Percentage customerCancellationPenaltyPercent,
        Percentage dealerPenaltyMinPercent,
        Percentage dealerPenaltyMaxPercent,
        TimeSpan turnaroundBuffer,
        TimeSpan nonDeliveryGrace,
        int rulesVersion)
    {
        DepositPercent = depositPercent;
        CommissionPercent = commissionPercent;
        FreeCancellationWindow = freeCancellationWindow;
        NoShowTimeout = noShowTimeout;
        PaymentWindow = paymentWindow;
        AnswerWindow = answerWindow;
        PostReturnSettlementWindow = postReturnSettlementWindow;
        CustomerCancellationPenaltyPercent = customerCancellationPenaltyPercent;
        DealerPenaltyMinPercent = dealerPenaltyMinPercent;
        DealerPenaltyMaxPercent = dealerPenaltyMaxPercent;
        TurnaroundBuffer = turnaroundBuffer;
        NonDeliveryGrace = nonDeliveryGrace;
        RulesVersion = rulesVersion;
    }

    public static Result<BookingTerms, Error> Create(
        Percentage depositPercent,
        Percentage commissionPercent,
        TimeSpan freeCancellationWindow,
        TimeSpan noShowTimeout,
        TimeSpan paymentWindow,
        TimeSpan answerWindow,
        TimeSpan postReturnSettlementWindow,
        Percentage customerCancellationPenaltyPercent,
        Percentage dealerPenaltyMinPercent,
        Percentage dealerPenaltyMaxPercent,
        TimeSpan turnaroundBuffer,
        TimeSpan nonDeliveryGrace,
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
            paymentWindow <= TimeSpan.Zero || answerWindow <= TimeSpan.Zero ||
            postReturnSettlementWindow < TimeSpan.Zero ||
            turnaroundBuffer < TimeSpan.Zero ||
            nonDeliveryGrace < TimeSpan.Zero)
        {
            return Error.Validation("booking.invalid_terms", "Booking terms carry an invalid time window.");
        }

        return new BookingTerms(
            depositPercent,
            commissionPercent,
            freeCancellationWindow,
            noShowTimeout,
            paymentWindow,
            answerWindow,
            postReturnSettlementWindow,
            customerCancellationPenaltyPercent,
            dealerPenaltyMinPercent,
            dealerPenaltyMaxPercent,
            turnaroundBuffer,
            nonDeliveryGrace,
            rulesVersion);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DepositPercent;
        yield return CommissionPercent;
        yield return FreeCancellationWindow;
        yield return NoShowTimeout;
        yield return PaymentWindow;
        yield return AnswerWindow;
        yield return PostReturnSettlementWindow;
        yield return CustomerCancellationPenaltyPercent;
        yield return DealerPenaltyMinPercent;
        yield return DealerPenaltyMaxPercent;
        yield return TurnaroundBuffer;
        yield return NonDeliveryGrace;
        yield return RulesVersion;
    }
}
