using CSharpFunctionalExtensions;
// BookingParty and PenaltyAssessment are shared vocabulary with Bookings, not a boundary leak:
// DisputeTicket already speaks in BookingParty, and a resolution is meaningless without the
// assessment it is choosing inside. The reference stays one-way and by value.
using Khadra.Domain.Bookings;
using Khadra.Domain.Common;
using ValueObject = Khadra.Domain.Common.ValueObject;

namespace Khadra.Domain.Disputes;

// What happens to the deposit the platform is holding, expressed as money rather than as a label.
//
// Labels like "apply penalty" or "refund deposit" overlap: applying a penalty to a customer IS a
// disposition of their deposit. Splitting the held amount three ways instead makes the outcome
// unambiguous, forces it to balance, and lets Payments act on it without interpreting anything.
public sealed class DepositDisposition : ValueObject
{
    // The basis the three legs were split from, kept rather than discarded after validation.
    //
    // Payments will read this decision as a standalone document, long after it was made. Without the
    // basis it would have to re-derive what was held by summing the legs -- which assumes the very
    // thing the sum is meant to prove -- or by re-reading a booking whose pricing may since have been
    // superseded. A financial instruction should be checkable on its own terms.
    public Money DepositHeld { get; }
    public Money RefundToCustomer { get; }
    public Money RetainedByPlatform { get; }
    public Money TransferredToDealer { get; }

#pragma warning disable CS8618 // EF materialises this value object by writing its backing fields;
    // the public factories remain the only way application code can create one.
    private DepositDisposition()
    {
    }
#pragma warning restore CS8618

    private DepositDisposition(
        Money depositHeld,
        Money refundToCustomer,
        Money retainedByPlatform,
        Money transferredToDealer)
    {
        DepositHeld = depositHeld;
        RefundToCustomer = refundToCustomer;
        RetainedByPlatform = retainedByPlatform;
        TransferredToDealer = transferredToDealer;
    }

    public static Result<DepositDisposition, Error> Create(
        Money depositHeld,
        Money refundToCustomer,
        Money retainedByPlatform,
        Money transferredToDealer)
    {
        ArgumentNullException.ThrowIfNull(depositHeld);
        ArgumentNullException.ThrowIfNull(refundToCustomer);
        ArgumentNullException.ThrowIfNull(retainedByPlatform);
        ArgumentNullException.ThrowIfNull(transferredToDealer);

        var currency = depositHeld.CurrencyCode;
        if (!string.Equals(refundToCustomer.CurrencyCode, currency, StringComparison.Ordinal) ||
            !string.Equals(retainedByPlatform.CurrencyCode, currency, StringComparison.Ordinal) ||
            !string.Equals(transferredToDealer.CurrencyCode, currency, StringComparison.Ordinal))
        {
            return DisputeErrors.DispositionCurrencyMismatch;
        }

        // Every fils of the held deposit must be accounted for. Anything else leaves money stranded.
        var total = refundToCustomer.Add(retainedByPlatform).Add(transferredToDealer);
        if (total != depositHeld)
            return DisputeErrors.DispositionDoesNotBalance;

        return new DepositDisposition(depositHeld, refundToCustomer, retainedByPlatform, transferredToDealer);
    }

    public static Result<DepositDisposition, Error> RefundEverything(Money depositHeld)
    {
        ArgumentNullException.ThrowIfNull(depositHeld);

        // A FRESH instance for every leg, including the basis and the refund, which are equal in value
        // but must not be the same object. EF tracks an owned value object by reference, so handing one
        // instance to two mapped properties makes it believe a single object is in two places and the
        // save fails with a severed-association error. Percentage.Zero and PenaltyAssessment.Fixed
        // already carry this scar; this factory was written before it was learned.
        return Create(
            Money.Create(depositHeld.Amount, depositHeld.CurrencyCode),
            Money.Create(depositHeld.Amount, depositHeld.CurrencyCode),
            Money.ZeroIn(depositHeld.CurrencyCode),
            Money.ZeroIn(depositHeld.CurrencyCode));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DepositHeld;
        yield return RefundToCustomer;
        yield return RetainedByPlatform;
        yield return TransferredToDealer;
    }
}

// The Admin's decision on a ticket, recorded against the booking as the source of truth if the
// disagreement escalates further (spec 3.3).
public sealed class DisputeResolution : ValueObject
{
    public DepositDisposition Deposit { get; }
    // Money the DEALER owes as a result, for example a non-delivery penalty. Settled off the deposit
    // rail entirely, because at the confirmed 20/20 numbers the platform holds no dealer funds.
    public Money? DealerCharge { get; }
    public string Note { get; }
    public Id ResolvedByAdminId { get; }
    public DateTimeOffset ResolvedAt { get; }

#pragma warning disable CS8618 // EF materialises this value object by writing its backing fields;
    // the public factories remain the only way application code can create one.
    private DisputeResolution()
    {
    }
#pragma warning restore CS8618

    private DisputeResolution(
        DepositDisposition deposit,
        Money? dealerCharge,
        string note,
        Id resolvedByAdminId,
        DateTimeOffset resolvedAt)
    {
        Deposit = deposit;
        DealerCharge = dealerCharge;
        Note = note;
        ResolvedByAdminId = resolvedByAdminId;
        ResolvedAt = resolvedAt;
    }

    /// <summary>
    /// Records the Admin's decision.
    /// </summary>
    /// <param name="assessedPenalty">
    /// The penalty the BOOKING assessed, or null if it assessed none. A dealer charge is only ever a
    /// choice made inside a range the booking already fixed at the time of the event: the aggregate
    /// says "an Admin picks inside it on a ticket", and until now nothing enforced the "inside it"
    /// part, leaving DealerCharge a free-form amount on a money decision.
    /// </param>
    public static Result<DisputeResolution, Error> Create(
        DepositDisposition deposit,
        Money? dealerCharge,
        PenaltyAssessment? assessedPenalty,
        string? note,
        Id resolvedByAdminId,
        DateTimeOffset resolvedAt)
    {
        ArgumentNullException.ThrowIfNull(deposit);
        if (string.IsNullOrWhiteSpace(note))
            return DisputeErrors.ResolutionNoteRequired;
        if (resolvedByAdminId.IsEmpty)
            throw new DomainException("A resolution requires the admin who made it.");

        if (dealerCharge is not null)
        {
            // Nothing to pick inside: either the booking blamed nobody, or it blamed the customer.
            if (assessedPenalty is null || assessedPenalty.AttributedTo != BookingParty.Dealer)
                return DisputeErrors.DealerChargeWithoutAssessment;

            if (!string.Equals(
                    dealerCharge.CurrencyCode,
                    assessedPenalty.MinAmount.CurrencyCode,
                    StringComparison.Ordinal))
            {
                return DisputeErrors.DealerChargeCurrencyMismatch;
            }

            // A flat penalty is simply a range whose ends are equal, so the owner's still-open tier
            // decision does not change this check.
            if (dealerCharge.Amount < assessedPenalty.MinAmount.Amount ||
                dealerCharge.Amount > assessedPenalty.MaxAmount.Amount)
            {
                return DisputeErrors.DealerChargeOutsideAssessment;
            }
        }

        return new DisputeResolution(deposit, dealerCharge, note.Trim(), resolvedByAdminId, resolvedAt);
    }

    public bool WaivesEverything =>
        Deposit.RetainedByPlatform.IsZero && Deposit.TransferredToDealer.IsZero && (DealerCharge?.IsZero ?? true);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Deposit;
        yield return DealerCharge;
        yield return Note;
        yield return ResolvedByAdminId;
        yield return ResolvedAt;
    }
}
