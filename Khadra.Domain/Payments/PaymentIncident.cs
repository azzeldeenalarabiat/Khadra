using CSharpFunctionalExtensions;
using Khadra.Domain.Common;

namespace Khadra.Domain.Payments;

/// <summary>What a capture incident is (Wave 4, B1; E2E F30).</summary>
public sealed class PaymentIncidentKind : Enumeration
{
    /// <summary>A different capture landed on a payment that had already taken its money: the card was charged twice.</summary>
    public static readonly PaymentIncidentKind SecondCapture = new(1, "SecondCapture");

    /// <summary>The provider reported the same capture again with a different amount: it contradicts itself.</summary>
    public static readonly PaymentIncidentKind AmountMismatch = new(2, "AmountMismatch");

    /// <summary>A capture named a reference another attempt had already applied or orphaned.</summary>
    public static readonly PaymentIncidentKind CaptureOnAnotherAttempt = new(3, "CaptureOnAnotherAttempt");

    private PaymentIncidentKind(int id, string name) : base(id, name)
    {
    }

    /// <summary>The kind a repeat capture's outcome raises, or null for an outcome that is no incident.</summary>
    public static PaymentIncidentKind? For(ProviderEventOutcome outcome) =>
        outcome == ProviderEventOutcome.SecondCapture ? SecondCapture
        : outcome == ProviderEventOutcome.AmountMismatch ? AmountMismatch
        : outcome == ProviderEventOutcome.OtherAttempt ? CaptureOnAnotherAttempt
        : null;
}

/// <summary>
/// A capture notice that money may have moved in a way no booking accounts for, waiting for a person (Wave 4, B1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Never refunded by code.</b> A second charge is real money the provider took; returning it automatically
/// would be a refund the platform cannot prove is owed, and keeping it is not the platform's to decide either. So
/// the incident records what was seen and stays open on the administrator's attention queue until somebody has
/// dealt with the money at the provider and says so with "Mark as handled", which is audited.
/// </para>
/// <para>
/// One per provider event: the receipt that recorded the notice is unique here, so a notice that is redelivered
/// (and refused at the receipt's own index) cannot raise a second incident. Its figures and kind never change;
/// only the handled fields are written once, later.
/// </para>
/// </remarks>
public sealed class PaymentIncident : AggregateRoot
{
    public const int MaxNoteLength = 500;

    public PaymentIncidentKind Kind { get; private set; } = null!;

    /// <summary>The payment the notice named by its session reference.</summary>
    public Id PaymentId { get; private set; }

    /// <summary>The provider event's receipt: the record of the notice that raised this.</summary>
    public Id ReceiptId { get; private set; }

    public string Provider { get; private set; } = null!;

    /// <summary>The capture reference the notice carried, if any.</summary>
    public string? CaptureReference { get; private set; }

    /// <summary>What the notice said was captured.</summary>
    public Money Reported { get; private set; } = null!;

    /// <summary>What this payment had already taken, or, for a capture on another attempt, what it asked for.</summary>
    public Money Expected { get; private set; } = null!;

    /// <summary>For <see cref="PaymentIncidentKind.CaptureOnAnotherAttempt"/>: the attempt that holds the reference.</summary>
    public Id? OtherPaymentId { get; private set; }

    public DateTimeOffset DetectedAt { get; private set; }

    public DateTimeOffset? HandledAt { get; private set; }
    public Id? HandledByAdminId { get; private set; }
    public string? HandledNote { get; private set; }

    public bool IsHandled => HandledAt is not null;

    private PaymentIncident()
    {
    }

    private PaymentIncident(Id id) : base(id)
    {
    }

    public static PaymentIncident Raise(
        PaymentIncidentKind kind,
        Id paymentId,
        Id receiptId,
        string provider,
        string? captureReference,
        Money reported,
        Money expected,
        Id? otherPaymentId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(reported);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        if (paymentId.IsEmpty || receiptId.IsEmpty)
            throw new DomainException("A payment incident names its payment and the notice that raised it.");
        if ((kind == PaymentIncidentKind.CaptureOnAnotherAttempt) != (otherPaymentId is { IsEmpty: false }))
            throw new DomainException("Only a capture on another attempt names the other attempt, and it always does.");

        return new PaymentIncident(Id.New())
        {
            Kind = kind,
            PaymentId = paymentId,
            ReceiptId = receiptId,
            Provider = provider,
            CaptureReference = string.IsNullOrWhiteSpace(captureReference) ? null : captureReference.Trim(),
            // Fresh instances: EF tracks owned values by reference, and one owned by two rows is forbidden.
            Reported = Money.Create(reported.Amount, reported.CurrencyCode),
            Expected = Money.Create(expected.Amount, expected.CurrencyCode),
            OtherPaymentId = otherPaymentId,
            DetectedAt = now
        };
    }

    /// <summary>
    /// An administrator has dealt with the money at the provider, and says how. Once; the note is required.
    /// </summary>
    public UnitResult<Error> MarkHandled(Id adminUserId, string note, DateTimeOffset now)
    {
        if (adminUserId.IsEmpty)
            throw new DomainException("A payment incident is marked handled by an administrator.");
        ArgumentException.ThrowIfNullOrWhiteSpace(note);
        if (IsHandled)
            return UnitResult.Failure(PaymentErrors.IncidentAlreadyHandled);

        var trimmed = note.Trim();
        if (trimmed.Length > MaxNoteLength)
            throw new DomainException($"A handled note is at most {MaxNoteLength} characters; the validator says so first.");

        HandledAt = now;
        HandledByAdminId = adminUserId;
        HandledNote = trimmed;
        return UnitResult.Success<Error>();
    }
}
