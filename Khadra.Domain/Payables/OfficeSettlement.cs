using System.Globalization;
using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;
using Entity = Khadra.Domain.Common.Entity;

namespace Khadra.Domain.Payables;

/// <summary>
/// An administrator's record that money moved between Khadra and one rental office, by hand, outside the platform
/// (payments Phase 8; owner, 2026-09-24): the amount, the day it moved, who recorded it, and exactly which payables
/// it closed. There is no payout rail — the platform records a payment it did not make, and never makes one.
/// </summary>
/// <remarks>
/// <para>
/// It settles EVERY payable due to or from the office in one currency and one kind of money at once, netted (owner,
/// 2026-09-29): what the office owes is taken from what it is owed. The amount is signed from Khadra's side —
/// above zero Khadra paid the office, below zero the office paid Khadra, and zero means the two cancelled out and
/// nothing moved — and its direction says the same in a word.
/// </para>
/// <para>
/// Append-only. A settlement recorded wrongly is voided (<see cref="OfficeSettlementVoid"/>), never edited: its
/// payables open again and are due in the next one, and this row keeps saying what was recorded.
/// </para>
/// </remarks>
public sealed class OfficeSettlement : AggregateRoot, IAppendOnly
{
    public const int NumberMaxLength = FinancialDocumentNumbers.MaxLength;
    public const int ReferenceMaxLength = 100;
    public const int NoteMaxLength = 500;

    /// <summary>The prefix of a settlement's number, beside a document's <c>PAY</c>, <c>RFD</c> and <c>STM</c>.</summary>
    public const string NumberPrefix = "SET";

    private readonly List<OfficeSettlementLine> _lines = [];

    /// <summary><c>SET-2026-000001</c>, or <c>TEST-SET-2026-000001</c> for sandbox money: gapless, per year.</summary>
    public string Number { get; private set; } = null!;

    public Id DealerId { get; private set; }
    public string Currency { get; private set; } = null!;

    /// <summary>The provider of every payable it closed: the one marker of test money.</summary>
    public string Provider { get; private set; } = null!;

    public SettlementDirection Direction { get; private set; } = null!;

    /// <summary>What moved, signed from Khadra's side: the sum of the nets it closed.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The day the money moved, in Amman, as the administrator recorded it. Never in the future.</summary>
    public DateOnly PaidOn { get; private set; }

    /// <summary>The payment's own reference, when there is one.</summary>
    public string? Reference { get; private set; }

    /// <summary>The administrator's note. Administrators see it; the office does not.</summary>
    public string? Note { get; private set; }

    public Id RecordedByAdminId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public IReadOnlyCollection<OfficeSettlementLine> Lines => _lines.AsReadOnly();

    public bool IsTest => PaymentProviders.IsSandbox(Provider);

    private OfficeSettlement()
    {
    }

    private OfficeSettlement(Id id) : base(id)
    {
    }

    /// <summary>The series a settlement's number is taken from: one per kind of money and Amman year.</summary>
    public static string SeriesKey(int year, bool isTest)
    {
        if (year is < 2000 or > 9999)
            throw new ArgumentOutOfRangeException(nameof(year), year, "A year has four digits.");

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(isTest ? FinancialDocumentNumbers.TestPrefix : string.Empty)}{NumberPrefix}-{year:D4}");
    }

    /// <summary>
    /// Records a settlement closing <paramref name="payables"/>, which the caller found due: all open, all the
    /// office's, in one currency and one kind of money.
    /// </summary>
    /// <param name="today">Today in Amman: the day the money moved can be no later.</param>
    public static Result<OfficeSettlement, Error> Record(
        Id id,
        string number,
        Id dealerId,
        IReadOnlyList<OfficePayable> payables,
        DateOnly paidOn,
        DateOnly today,
        string? reference,
        string? note,
        Id adminUserId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(payables);
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        if (id.IsEmpty || dealerId.IsEmpty || adminUserId.IsEmpty)
            throw new DomainException("A settlement names itself, its office and its administrator.");
        if (number.Length > NumberMaxLength)
            throw new DomainException("A settlement number is longer than its column.");
        if (payables.Count == 0)
            return PayableErrors.NothingDue;

        var first = payables[0];
        foreach (var payable in payables)
        {
            if (payable.DealerId != dealerId ||
                !string.Equals(payable.Currency, first.Currency, StringComparison.Ordinal) ||
                !string.Equals(payable.Provider, first.Provider, StringComparison.Ordinal))
            {
                throw new DomainException("A settlement closes one office's payables in one currency and one kind of money.");
            }

            if (payable.IsSettled)
                return PayableErrors.AlreadySettled;
        }

        if (payables.Select(payable => payable.Id).Distinct().Count() != payables.Count)
            throw new DomainException("A settlement names each payable once.");
        if (paidOn > today)
            return PayableErrors.PaidOnInFuture;

        var trimmedReference = Trimmed(reference);
        var trimmedNote = Trimmed(note);
        if (trimmedReference?.Length > ReferenceMaxLength || trimmedNote?.Length > NoteMaxLength)
            throw new DomainException("A settlement's reference or note is longer than its column; the validator allows neither.");

        var amount = payables.Sum(payable => payable.Net);
        var settlement = new OfficeSettlement(id)
        {
            Number = number,
            DealerId = dealerId,
            Currency = first.Currency,
            Provider = first.Provider,
            Direction = SettlementDirection.Of(amount),
            Amount = amount,
            PaidOn = paidOn,
            Reference = trimmedReference,
            Note = trimmedNote,
            RecordedByAdminId = adminUserId,
            RecordedAt = now,
        };

        foreach (var payable in payables)
            settlement._lines.Add(new OfficeSettlementLine(id, payable.Id, payable.Net));

        return settlement;
    }

    private static string? Trimmed(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>One payable a settlement closed, with the net it closed it at. Append-only.</summary>
public sealed class OfficeSettlementLine : Entity, IAppendOnly
{
    public Id SettlementId { get; private set; }
    public Id PayableId { get; private set; }

    /// <summary>The payable's net when the settlement closed it: signed.</summary>
    public decimal Net { get; private set; }

    private OfficeSettlementLine()
    {
    }

    internal OfficeSettlementLine(Id settlementId, Id payableId, decimal net) : base(Id.New())
    {
        SettlementId = settlementId;
        PayableId = payableId;
        Net = net;
    }
}

/// <summary>
/// An administrator's record that a settlement was recorded wrongly (payments Phase 8). Its payables open again,
/// in the same transaction, and are due in the next settlement; the settlement stays readable, marked void.
/// </summary>
/// <remarks>
/// Its id IS the settlement's, so a settlement is voided at most once: two administrators voiding it at once collide
/// on the key. Append-only.
/// </remarks>
public sealed class OfficeSettlementVoid : AggregateRoot, IAppendOnly
{
    public const int MaxReasonLength = 500;

    public Id SettlementId => Id;
    public DateTimeOffset VoidedAt { get; private set; }
    public Id VoidedByAdminId { get; private set; }

    /// <summary>Why, in the administrator's words. Administrators see it; the office does not.</summary>
    public string Reason { get; private set; } = null!;

    private OfficeSettlementVoid()
    {
    }

    private OfficeSettlementVoid(Id settlementId) : base(settlementId)
    {
    }

    public static Result<OfficeSettlementVoid, Error> Record(Id settlementId, Id adminUserId, string? reason, DateTimeOffset now)
    {
        if (settlementId.IsEmpty || adminUserId.IsEmpty)
            throw new DomainException("A void names the settlement and the administrator.");

        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return PayableErrors.VoidReasonRequired;
        if (trimmed.Length > MaxReasonLength)
            return PayableErrors.VoidReasonTooLong;

        return new OfficeSettlementVoid(settlementId)
        {
            VoidedAt = now,
            VoidedByAdminId = adminUserId,
            Reason = trimmed,
        };
    }
}
