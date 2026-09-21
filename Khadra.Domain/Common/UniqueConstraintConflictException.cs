namespace Khadra.Domain.Common;

/// <summary>
/// Raised by the unit of work when a UNIQUE index refuses a write — SQLSTATE 23505.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <see cref="ExclusiveHoldConflictException"/>, and here for the same reason: a
/// unique index is the floor under a check-then-act, and only the database can settle the race. Two
/// requests both find no receipt for a provider event, both insert one, and exactly one loses.
/// </para>
/// <para>
/// The constraint's NAME travels with it because the exception is general and the meaning is not.
/// A handler that expects a particular race checks the name and translates it into the answer its
/// own callers understand; anything unrecognised stays an exception and becomes a 409 with a generic
/// code, which is what it was before this type existed and the honest answer for a conflict nobody
/// anticipated.
/// </para>
/// <para>
/// It was an anonymous <c>DbUpdateException</c> until 2026-09-21, and for the webhook that was a
/// defect with a delay fuse. A provider redelivering an event it already sent hit the receipt's
/// unique index, the violation escaped untranslated, and the API answered 409 — which every payment
/// provider reads as "retry". The effect was applied exactly once, so no money moved twice; the
/// endpoint simply went on refusing that delivery for as long as the provider kept offering it, and
/// providers disable an endpoint that keeps failing. The next real capture would then never arrive.
/// </para>
/// </remarks>
public sealed class UniqueConstraintConflictException : Exception
{
    public string? ConstraintName { get; }

    public UniqueConstraintConflictException()
    {
    }

    public UniqueConstraintConflictException(string message) : base(message)
    {
    }

    public UniqueConstraintConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public UniqueConstraintConflictException(string message, string? constraintName, Exception innerException)
        : base(message, innerException) => ConstraintName = constraintName;

    /// <summary>
    /// The index that makes a provider event exactly once, whatever the provider does.
    /// </summary>
    /// <remarks>
    /// Named here rather than in the handler so the two ends of the race — the mapping that creates
    /// the index and the code that expects to lose to it — say the same word. See
    /// <c>ProviderEventReceiptConfiguration</c>.
    /// </remarks>
    public const string ProviderEventReceiptConstraint =
        "ix_payment_provider_events_provider_provider_event_id";

    public bool IsProviderEventReceipt =>
        string.Equals(ConstraintName, ProviderEventReceiptConstraint, StringComparison.Ordinal);
}
