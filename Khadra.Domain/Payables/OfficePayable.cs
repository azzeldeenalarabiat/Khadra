using System.Globalization;
using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Entity = Khadra.Domain.Common.Entity;

namespace Khadra.Domain.Payables;

/// <summary>One line of a payable as the calculator drafted it: a kind, a positive amount, and what it came from.</summary>
/// <param name="SourceId">The dispute ticket a share or a charge came from; null for the booking's own lines.</param>
public sealed record PayableLineDraft(PayableLineKind Kind, decimal Amount, Id? SourceId = null);

/// <summary>Everything a payable is recorded from: the booking's final outcome, in the one calculator's lines.</summary>
/// <param name="Provider">The provider of the payment that confirmed the booking: SANDBOX marks test money.</param>
/// <param name="FinalAt">When the outcome became final: the booking's completion, or its dispute window's close.</param>
public sealed record PayableDraft(
    Id BookingId,
    Id DealerId,
    string BookingReference,
    string Currency,
    string Provider,
    PayableOutcome Outcome,
    DateTimeOffset FinalAt,
    int CalculatorVersion,
    IReadOnlyList<PayableLineDraft> Lines);

/// <summary>
/// What Khadra owes one rental office — or is owed by it — for ONE paid booking whose outcome is final (payments
/// Phase 8; owner, 2026-09-24 and 2026-09-29). Recorded once per booking, from the one calculator's answer, the
/// net-zero ones included, so every final paid booking says in the ledger what it came to.
/// </summary>
/// <remarks>
/// <para>
/// Its figures and lines are FROZEN: nothing in the application changes them, and a database trigger refuses
/// it too. The only thing that moves is which settlement, if any, has closed it — a settlement records it, and a
/// voided settlement opens it again. Its figures are derived here from its lines, never passed beside them, so
/// the two cannot disagree: the office's money is what went towards it, less Khadra's commission (never more
/// than that money) and any charge a dispute assessed, and the net is signed — below zero, the office owes.
/// </para>
/// <para>
/// The kind of money is the confirming payment's provider, frozen here as a document freezes it: the one marker
/// of test money, never a second flag.
/// </para>
/// </remarks>
public sealed class OfficePayable : AggregateRoot
{
    public const int BookingReferenceMaxLength = 20;
    public const int ProviderMaxLength = 30;

    /// <summary>The scale the ledger's columns hold: an amount finer than a fils would be rounded on save.</summary>
    public const int AmountScale = 3;

    private readonly List<OfficePayableLine> _lines = [];

    public Id BookingId { get; private set; }
    public Id DealerId { get; private set; }

    /// <summary>The booking's reference, as screens and the audit trail name the payable. Never the customer.</summary>
    public string BookingReference { get; private set; } = null!;

    public string Currency { get; private set; } = null!;
    public string Provider { get; private set; } = null!;
    public PayableOutcome Outcome { get; private set; } = null!;
    public DateTimeOffset FinalAt { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>What went towards the office on the booking: the lines that are its money.</summary>
    public decimal OfficeMoney { get; private set; }

    /// <summary>Khadra's commission: the booking's frozen figure, capped at <see cref="OfficeMoney"/>.</summary>
    public decimal Commission { get; private set; }

    /// <summary>What resolved disputes charged the office.</summary>
    public decimal OfficeCharges { get; private set; }

    /// <summary>What the office is owed: signed, and below zero when its charges are more than its money.</summary>
    public decimal Net { get; private set; }

    /// <summary>The calculator's version when the payable was recorded.</summary>
    public int CalculatorVersion { get; private set; }

    /// <summary>The settlement that closed it; null while it is open.</summary>
    public Id? SettlementId { get; private set; }

    public DateTimeOffset? SettledAt { get; private set; }

    public IReadOnlyCollection<OfficePayableLine> Lines => _lines.OrderBy(line => line.Position).ToList();

    public bool IsTest => PaymentProviders.IsSandbox(Provider);

    public bool IsSettled => SettlementId is not null;

    private OfficePayable()
    {
    }

    private OfficePayable(Id id) : base(id)
    {
    }

    /// <summary>
    /// Records a booking's payable. The draft is the calculator's, never a person's, so a draft that breaks a rule
    /// is a defect upstream and is thrown, not refused.
    /// </summary>
    public static OfficePayable Record(PayableDraft draft, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(draft.Outcome);
        ArgumentNullException.ThrowIfNull(draft.Lines);
        if (draft.BookingId.IsEmpty || draft.DealerId.IsEmpty)
            throw new DomainException("A payable names its booking and its office.");
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.BookingReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Provider);
        if (draft.BookingReference.Length > BookingReferenceMaxLength || draft.Provider.Length > ProviderMaxLength)
            throw new DomainException("A payable's reference or provider is longer than its column.");
        if (draft.Currency is not { Length: 3 })
            throw new DomainException("A payable's currency is a three-letter code.");
        if (draft.CalculatorVersion < 1)
            throw new DomainException("A payable names the calculator version it was recorded by.");

        var payable = new OfficePayable(Id.New())
        {
            BookingId = draft.BookingId,
            DealerId = draft.DealerId,
            BookingReference = draft.BookingReference,
            Currency = draft.Currency,
            Provider = draft.Provider,
            Outcome = draft.Outcome,
            FinalAt = draft.FinalAt,
            RecordedAt = now,
            CalculatorVersion = draft.CalculatorVersion,
        };

        var position = 0;
        foreach (var line in draft.Lines)
        {
            ArgumentNullException.ThrowIfNull(line);
            ArgumentNullException.ThrowIfNull(line.Kind);
            if (line.Amount <= 0m || decimal.Round(line.Amount, AmountScale) != line.Amount)
            {
                throw new DomainException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"A payable line is a positive amount of at most {AmountScale} decimals, not {line.Amount}."));
            }

            if (line.Kind.TowardsOffice && !draft.Outcome.CarriesOfficeMoney)
                throw new DomainException($"A {draft.Outcome.Name} payable carries no office money.");
            if ((line.Kind == PayableLineKind.DisputeShare || line.Kind == PayableLineKind.DisputeCharge) &&
                line.SourceId is not { IsEmpty: false })
            {
                throw new DomainException("A dispute's line names its ticket.");
            }

            payable._lines.Add(new OfficePayableLine(payable.Id, ++position, line.Kind, line.Amount, line.SourceId));
        }

        if (payable._lines.Count(line => line.Kind == PayableLineKind.Commission) > 1)
            throw new DomainException("A payable carries one commission.");

        payable.OfficeMoney = payable._lines.Where(line => line.Kind.TowardsOffice).Sum(line => line.Amount);
        payable.Commission = payable._lines.Where(line => line.Kind == PayableLineKind.Commission).Sum(line => line.Amount);
        payable.OfficeCharges = payable._lines.Where(line => line.Kind == PayableLineKind.DisputeCharge).Sum(line => line.Amount);
        if (payable.Commission > payable.OfficeMoney)
            throw new DomainException("Khadra's commission is never more than the office's money on the booking.");

        payable.Net = payable.OfficeMoney - payable.Commission - payable.OfficeCharges;
        return payable;
    }

    /// <summary>A settlement closed the payable. Only an open one can be closed.</summary>
    public UnitResult<Error> SettleUnder(Id settlementId, DateTimeOffset now)
    {
        if (settlementId.IsEmpty)
            throw new DomainException("A settlement has an id.");
        if (SettlementId is not null)
            return PayableErrors.AlreadySettled;

        SettlementId = settlementId;
        SettledAt = now;
        return UnitResult.Success<Error>();
    }

    /// <summary>
    /// The settlement that closed the payable was voided: it is open again, and due in the next one. Its figures
    /// never change; the voided settlement keeps its line naming it.
    /// </summary>
    public void ReopenAfterVoid(Id settlementId)
    {
        if (SettlementId is not { } current || current != settlementId)
            throw new DomainException($"Payable {Id} was not closed by settlement {settlementId}.");

        SettlementId = null;
        SettledAt = null;
    }
}

/// <summary>
/// One line of a payable: append-only, like the figures it adds up to. The amount is positive; the kind says which
/// way it goes.
/// </summary>
public sealed class OfficePayableLine : Entity, IAppendOnly
{
    public Id PayableId { get; private set; }

    /// <summary>Its place in the payable, from 1: the order the calculator wrote it in.</summary>
    public int Position { get; private set; }

    public PayableLineKind Kind { get; private set; } = null!;
    public decimal Amount { get; private set; }

    /// <summary>The dispute ticket a share or a charge came from.</summary>
    public Id? SourceId { get; private set; }

    /// <summary>The line as a signed amount: + for the office's money, − for what is taken from it.</summary>
    public decimal SignedAmount => Kind.TowardsOffice ? Amount : -Amount;

    private OfficePayableLine()
    {
    }

    internal OfficePayableLine(Id payableId, int position, PayableLineKind kind, decimal amount, Id? sourceId)
        : base(Id.New())
    {
        PayableId = payableId;
        Position = position;
        Kind = kind;
        Amount = amount;
        SourceId = sourceId;
    }
}
