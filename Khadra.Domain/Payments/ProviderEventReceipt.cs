using Khadra.Domain.Common;

namespace Khadra.Domain.Payments;

/// <summary>What a provider event was found to be, once this platform had looked at it.</summary>
public sealed class ProviderEventOutcome : Enumeration
{
    /// <summary>It moved a payment forward: a capture applied, a session failed.</summary>
    public static readonly ProviderEventOutcome Acted = new(1, "Acted");

    /// <summary>Money arrived that no booking could take. A refund was recorded.</summary>
    public static readonly ProviderEventOutcome Orphaned = new(2, "Orphaned");

    /// <summary>
    /// The reference named a payment this platform has never issued.
    /// </summary>
    /// <remarks>
    /// Recorded rather than dropped, and this is the whole reason the receipt is not part of the
    /// <see cref="Payment"/> aggregate: an event with no payment behind it has no aggregate to be a
    /// part of, and is exactly the event somebody will need to find later.
    /// </remarks>
    public static readonly ProviderEventOutcome Unknown = new(3, "Unknown");

    /// <summary>A kind this platform does not act on. Seen, acknowledged, nothing done.</summary>
    public static readonly ProviderEventOutcome Ignored = new(4, "Ignored");

    /// <summary>
    /// A refund event for one of our payments that could not be tied to ONE of its refunds: it named
    /// a refund reference the payment never sent, or named none and more than one refund (or none)
    /// could be the one it meant. Recorded and logged, never guessed (Phase 3, 2026-09-26).
    /// </summary>
    public static readonly ProviderEventOutcome Unmatched = new(5, "Unmatched");

    // The five below are a capture notice for a payment that has ALREADY taken its money (Wave 4, B1; E2E F30). A
    // provider sends several events per charge, under new event ids, so the replay index cannot catch them. Before
    // these existed, each such notice was refused as `already_captured`, failed to orphan, and was logged as money
    // "UNACCOUNTED FOR" on a row whose money was right. Each name fits the column's twenty characters.

    /// <summary>The same capture, said again: the same capture reference and the same amount. Nothing changed.</summary>
    public static readonly ProviderEventOutcome Duplicate = new(6, "Duplicate");

    /// <summary>
    /// A capture notice with no capture reference to compare, for the same amount and currency this payment took.
    /// Taken as the same capture said again, and logged as a warning: without a reference, a genuine second charge
    /// of the same amount reads exactly the same (which is why a real adapter must send one, pre-launch item 76).
    /// </summary>
    public static readonly ProviderEventOutcome AssumedDuplicate = new(7, "AssumedDuplicate");

    /// <summary>The same capture reference with a different amount: the provider contradicts itself. An incident.</summary>
    public static readonly ProviderEventOutcome AmountMismatch = new(8, "AmountMismatch");

    /// <summary>A different capture on a payment that already took its money: the card was charged twice. An incident.</summary>
    public static readonly ProviderEventOutcome SecondCapture = new(9, "SecondCapture");

    /// <summary>A capture whose reference another attempt already applied or orphaned. An incident.</summary>
    public static readonly ProviderEventOutcome OtherAttempt = new(10, "OtherAttempt");

    private ProviderEventOutcome(int id, string name) : base(id, name)
    {
    }
}

/// <summary>
/// One provider notification, recorded exactly once.
/// </summary>
/// <remarks>
/// <para>
/// This row IS the replay guard. It carries a unique index on (provider, provider_event_id) and is
/// inserted in the SAME transaction as whatever the event caused, so the two are one atomic fact: a
/// duplicate delivery collides on the index and the transaction that would have double-applied it
/// rolls back whole. A guard that read the table first and wrote afterwards would leave a window
/// between the two in which a concurrent retry passes the read.
/// </para>
/// <para>
/// It deliberately does NOT store the raw payload. A provider body carries the cardholder's name,
/// billing address and often an email, none of which this platform needs and all of which it would
/// then have to protect and expire. What is kept is what an investigation actually needs: whose
/// event, which one, what kind, which payment it resolved to, and what was done.
/// </para>
/// </remarks>
public sealed class ProviderEventReceipt : AggregateRoot
{
    public string Provider { get; private set; } = null!;

    /// <summary>The provider's own id for this delivery. The other half of the unique key.</summary>
    public string ProviderEventId { get; private set; } = null!;

    /// <summary>The session reference the event named, whether or not it resolved to anything.</summary>
    public string? ProviderReference { get; private set; }

    /// <summary>The provider's word for what happened, normalised by the adapter.</summary>
    public string Kind { get; private set; } = null!;

    /// <summary>The payment it resolved to, or null when it named a reference we never issued.</summary>
    public Id? PaymentId { get; private set; }

    public ProviderEventOutcome Outcome { get; private set; } = null!;

    /// <summary>The amount the event carried, in minor units of its own currency. Null when it carried none.</summary>
    public decimal? Amount { get; private set; }

    public string? CurrencyCode { get; private set; }

    /// <summary>
    /// The provider's id for the CAPTURE a capture event reports, when it sent one (Wave 4, B1). Kept so an
    /// investigation can see which capture each notice was about. Null on every other kind.
    /// </summary>
    public string? CaptureReference { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    private ProviderEventReceipt()
    {
    }

    private ProviderEventReceipt(Id id) : base(id)
    {
    }

    public static ProviderEventReceipt Record(
        string provider,
        string providerEventId,
        string? providerReference,
        string kind,
        Id? paymentId,
        ProviderEventOutcome outcome,
        Money? amount,
        DateTimeOffset receivedAt,
        string? captureReference = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(outcome);

        return new ProviderEventReceipt(Id.New())
        {
            Provider = provider,
            ProviderEventId = providerEventId,
            ProviderReference = providerReference,
            Kind = kind,
            PaymentId = paymentId,
            Outcome = outcome,
            Amount = amount?.Amount,
            CurrencyCode = amount?.CurrencyCode,
            CaptureReference = string.IsNullOrWhiteSpace(captureReference) ? null : captureReference.Trim(),
            ReceivedAt = receivedAt
        };
    }
}
