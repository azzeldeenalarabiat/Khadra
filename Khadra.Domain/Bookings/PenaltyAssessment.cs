using Khadra.Domain.Common;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.Bookings;

// What a penalty WOULD be, if anyone asks for one.
//
// Spec 3.3 and 5.5 are explicit: when no dispute ticket is opened after a cancellation, a no-show or
// a late delivery, no penalty is applied at all. So the booking records the assessment and stops.
// Money only ever moves when an Admin resolves a ticket. Treating this record as a charge would
// silently break the platform's "amicable resolution by default" promise.
public sealed class PenaltyAssessment : ValueObject
{
    public BookingParty AttributedTo { get; }
    public Percentage MinPercent { get; }
    public Percentage MaxPercent { get; }
    public Money MinAmount { get; }
    public Money MaxAmount { get; }
    public string Reason { get; }
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
        string reason,
        DateTimeOffset assessedAt)
    {
        AttributedTo = attributedTo;
        MinPercent = minPercent;
        MaxPercent = maxPercent;
        MinAmount = minAmount;
        MaxAmount = maxAmount;
        Reason = reason;
        AssessedAt = assessedAt;
    }

    // Nothing is owed by anyone: a free cancellation, or an expiry nobody caused.
    public static PenaltyAssessment None(string reason, string currencyCode, DateTimeOffset assessedAt) =>
        new(
            BookingParty.Unattributed,
            Percentage.Zero,
            Percentage.Zero,
            Money.ZeroIn(currencyCode),
            Money.ZeroIn(currencyCode),
            Describe(reason),
            assessedAt);

    public static PenaltyAssessment Fixed(
        BookingParty attributedTo,
        Percentage percent,
        Money basis,
        string reason,
        DateTimeOffset assessedAt)
    {
        ArgumentNullException.ThrowIfNull(attributedTo);
        ArgumentNullException.ThrowIfNull(percent);
        ArgumentNullException.ThrowIfNull(basis);

        // A fixed penalty is a range whose ends happen to be equal, so both ends are computed
        // separately rather than the same instance being put in both slots: two mapped properties
        // must never hold one object (see Percentage.Zero).
        return new PenaltyAssessment(
            attributedTo,
            Percentage.FromValidated(percent.Value),
            Percentage.FromValidated(percent.Value),
            percent.Of(basis),
            percent.Of(basis),
            Describe(reason),
            assessedAt);
    }

    // Spec 2.2 leaves the dealer non-delivery penalty as a 25%-50% range. The range travels with the
    // booking and the Admin picks a figure inside it when resolving the ticket.
    public static PenaltyAssessment Range(
        BookingParty attributedTo,
        Percentage minPercent,
        Percentage maxPercent,
        Money basis,
        string reason,
        DateTimeOffset assessedAt)
    {
        ArgumentNullException.ThrowIfNull(attributedTo);
        ArgumentNullException.ThrowIfNull(minPercent);
        ArgumentNullException.ThrowIfNull(maxPercent);
        ArgumentNullException.ThrowIfNull(basis);

        if (minPercent.IsGreaterThan(maxPercent))
            throw new DomainException("A penalty range cannot have a minimum above its maximum.");

        return new PenaltyAssessment(
            attributedTo,
            minPercent,
            maxPercent,
            minPercent.Of(basis),
            maxPercent.Of(basis),
            Describe(reason),
            assessedAt);
    }

    public bool IsNothingOwed => MaxAmount.IsZero;

    public bool IsRange => MinPercent != MaxPercent;

    // Always true, and deliberately an instance member: reading `assessment.RequiresTicketToEnforce`
    // at a call site is what stops someone treating an assessment as a charge. A static member would
    // not appear where the decision is actually made.
#pragma warning disable CA1822
    public bool RequiresTicketToEnforce => true;
#pragma warning restore CA1822

    private static string Describe(string reason) =>
        string.IsNullOrWhiteSpace(reason) ? "Unspecified" : reason.Trim();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return AttributedTo;
        yield return MinPercent;
        yield return MaxPercent;
        yield return MinAmount;
        yield return MaxAmount;
        yield return Reason;
        yield return AssessedAt;
    }
}
