using Khadra.Domain.Common;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.Bookings;

// What a penalty WOULD be, if anyone asks for one.
//
// Spec 3.3 and 5.5 are explicit: when no dispute ticket is opened after a cancellation, a no-show or
// a late delivery, no penalty is applied at all. So the booking records the assessment and stops.
// Money moves when an Admin resolves a ticket — and, since payments Phase 8, in ONE other case the
// owner decided on 2026-09-29 (pre-launch item 164, amending spec 3.3 for it): a CUSTOMER's fixed
// penalty on a paid booking that never ran is kept from the held deposit when the booking's own
// dispute window closes with no dispute, for the office, less Khadra's commission. The office
// payables ledger records that, and only that; every other assessment is still no charge at all, and
// treating one as a charge would silently break the "amicable resolution by default" promise.
public sealed class PenaltyAssessment : ValueObject
{
    public BookingParty AttributedTo { get; }
    public Percentage MinPercent { get; }
    public Percentage MaxPercent { get; }
    public Money MinAmount { get; }
    public Money MaxAmount { get; }
    /// <summary>The sentence this platform wrote when the penalty was assessed, frozen on the record.</summary>
    public string Reason { get; }

    /// <summary>
    /// The stable code behind <see cref="Reason"/>, so a client can say it in its own language.
    /// </summary>
    /// <remarks>
    /// Null on bookings assessed before codes existed, and only then: those records keep their
    /// sentence and are never rewritten. A client words the code when there is one and falls back to
    /// the sentence when there is not.
    /// </remarks>
    public PenaltyReason? ReasonCode { get; }

    public DateTimeOffset AssessedAt { get; }

#pragma warning disable CS8618 // EF materialises this value object by writing its backing fields;
    // the public factories remain the only way application code can create one.
    private PenaltyAssessment()
    {
    }
#pragma warning restore CS8618

    private PenaltyAssessment(
        BookingParty attributedTo,
        Percentage minPercent,
        Percentage maxPercent,
        Money minAmount,
        Money maxAmount,
        PenaltyReason reason,
        DateTimeOffset assessedAt)
    {
        AttributedTo = attributedTo;
        MinPercent = minPercent;
        MaxPercent = maxPercent;
        MinAmount = minAmount;
        MaxAmount = maxAmount;
        // The sentence AND the code: the sentence is what older clients print and what the record has
        // always held; the code is what a client translates.
        Reason = reason.Sentence;
        ReasonCode = reason;
        AssessedAt = assessedAt;
    }

    // Nothing is owed by anyone: a free cancellation, or an expiry nobody caused.
    public static PenaltyAssessment None(PenaltyReason reason, string currencyCode, DateTimeOffset assessedAt)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new PenaltyAssessment(
            BookingParty.Unattributed,
            Percentage.Zero,
            Percentage.Zero,
            Money.ZeroIn(currencyCode),
            Money.ZeroIn(currencyCode),
            reason,
            assessedAt);
    }

    public static PenaltyAssessment Fixed(
        BookingParty attributedTo,
        Percentage percent,
        Money basis,
        PenaltyReason reason,
        DateTimeOffset assessedAt)
    {
        ArgumentNullException.ThrowIfNull(attributedTo);
        ArgumentNullException.ThrowIfNull(percent);
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(reason);

        // A fixed penalty is a range whose ends happen to be equal, so both ends are computed
        // separately rather than the same instance being put in both slots: two mapped properties
        // must never hold one object (see Percentage.Zero).
        return new PenaltyAssessment(
            attributedTo,
            Percentage.FromValidated(percent.Value),
            Percentage.FromValidated(percent.Value),
            percent.Of(basis),
            percent.Of(basis),
            reason,
            assessedAt);
    }

    // Spec 2.2 leaves the dealer non-delivery penalty as a 25%-50% range. The range travels with the
    // booking and the Admin picks a figure inside it when resolving the ticket.
    public static PenaltyAssessment Range(
        BookingParty attributedTo,
        Percentage minPercent,
        Percentage maxPercent,
        Money basis,
        PenaltyReason reason,
        DateTimeOffset assessedAt)
    {
        ArgumentNullException.ThrowIfNull(attributedTo);
        ArgumentNullException.ThrowIfNull(minPercent);
        ArgumentNullException.ThrowIfNull(maxPercent);
        ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(reason);

        if (minPercent.IsGreaterThan(maxPercent))
            throw new DomainException("A penalty range cannot have a minimum above its maximum.");

        return new PenaltyAssessment(
            attributedTo,
            minPercent,
            maxPercent,
            minPercent.Of(basis),
            maxPercent.Of(basis),
            reason,
            assessedAt);
    }

    public bool IsNothingOwed => MaxAmount.IsZero;

    public bool IsRange => MinPercent != MaxPercent;

    // Whether nothing can come of this assessment without a dispute, and deliberately an instance
    // member: reading `assessment.RequiresTicketToEnforce` at a call site is what stops someone treating
    // an assessment as a charge. False for exactly the case the owner decided on 2026-09-29 (pre-launch
    // item 164): a fixed penalty on the CUSTOMER of the whole of its basis — every customer penalty is
    // assessed on the deposit — which a paid booking keeps from its deposit when the dispute window
    // closes with no dispute. A range, a penalty on the office, nothing owed, and a customer's penalty of
    // less than the whole deposit, which nothing can keep yet (pre-launch item 205), all still need a
    // ticket (spec 3.3).
    public bool RequiresTicketToEnforce =>
        AttributedTo != BookingParty.Customer || IsNothingOwed || IsRange || MaxPercent.Value != 100m;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return AttributedTo;
        yield return MinPercent;
        yield return MaxPercent;
        yield return MinAmount;
        yield return MaxAmount;
        yield return Reason;
        yield return ReasonCode;
        yield return AssessedAt;
    }
}
