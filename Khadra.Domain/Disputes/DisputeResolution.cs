using CSharpFunctionalExtensions;
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
    public Money RefundToCustomer { get; }
    public Money RetainedByPlatform { get; }
    public Money TransferredToDealer { get; }

#pragma warning disable CS8618 // EF materialises this value object by writing its backing fields;
    // the public factories remain the only way application code can create one.
    private DepositDisposition()
    {
    }
#pragma warning restore CS8618

    private DepositDisposition(Money refundToCustomer, Money retainedByPlatform, Money transferredToDealer)
    {
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

        return new DepositDisposition(refundToCustomer, retainedByPlatform, transferredToDealer);
    }

    public static Result<DepositDisposition, Error> RefundEverything(Money depositHeld)
    {
        ArgumentNullException.ThrowIfNull(depositHeld);
        var zero = Money.ZeroIn(depositHeld.CurrencyCode);
        return Create(depositHeld, depositHeld, zero, zero);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
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

    public static Result<DisputeResolution, Error> Create(
        DepositDisposition deposit,
        Money? dealerCharge,
        string? note,
        Id resolvedByAdminId,
        DateTimeOffset resolvedAt)
    {
        ArgumentNullException.ThrowIfNull(deposit);
        if (string.IsNullOrWhiteSpace(note))
            return DisputeErrors.ResolutionNoteRequired;
        if (resolvedByAdminId.IsEmpty)
            throw new DomainException("A resolution requires the admin who made it.");

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
