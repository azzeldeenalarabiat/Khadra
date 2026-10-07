using Khadra.Domain.Common;

namespace Khadra.Domain.Payments;

/// <summary>
/// How long a refused refund waits before it is sent again, and when its refusals need a person (Wave 4, B4;
/// checklist 157).
/// </summary>
/// <remarks>
/// <para>
/// Every figure comes from configuration (<c>Payments:RefundRetry…</c>), never from here: the first wait, doubling
/// with each refusal, up to a ceiling. A refund is never abandoned — past the ceiling it is sent again at the
/// ceiling's interval for as long as it is owed — and from <see cref="RefusalsBeforeAlert"/> refusals on it is put in
/// front of an administrator.
/// </para>
/// <para>
/// It used to be sent again on every sweep, once a minute, with an Error each time: a card closed for good became a
/// log flood and an unbounded stream of calls to the provider.
/// </para>
/// </remarks>
public sealed record RefundRetryPolicy
{
    public RefundRetryPolicy(TimeSpan firstDelay, TimeSpan maxDelay, int refusalsBeforeAlert)
    {
        if (firstDelay <= TimeSpan.Zero)
            throw new DomainException("A refused refund waits some time before it is sent again.");
        if (maxDelay < firstDelay)
            throw new DomainException("A refund retry's ceiling cannot be shorter than its first wait.");
        if (refusalsBeforeAlert < 1)
            throw new DomainException("A refund needs a person after at least one refusal.");

        FirstDelay = firstDelay;
        MaxDelay = maxDelay;
        RefusalsBeforeAlert = refusalsBeforeAlert;
    }

    /// <summary>The wait after the first refusal.</summary>
    public TimeSpan FirstDelay { get; }

    /// <summary>The longest wait between two sends, however many refusals came before.</summary>
    public TimeSpan MaxDelay { get; }

    /// <summary>From this many refusals on, the refund is put in front of an administrator.</summary>
    public int RefusalsBeforeAlert { get; }

    /// <summary>
    /// The wait after the <paramref name="refusals"/>-th refusal: the first wait, doubled for each refusal before it,
    /// never more than the ceiling.
    /// </summary>
    public TimeSpan DelayAfter(int refusals)
    {
        if (refusals < 1)
            throw new DomainException("A retry is scheduled only after a refusal.");

        // Doubled step by step and stopped at the ceiling, so no power of two can overflow however long a refund
        // has been refused.
        var delay = FirstDelay;
        for (var refusal = 1; refusal < refusals && delay < MaxDelay; refusal++)
            delay += delay;
        return delay < MaxDelay ? delay : MaxDelay;
    }

    /// <summary>Whether a refund refused this many times needs a person.</summary>
    public bool NeedsAPerson(int refusals) => refusals >= RefusalsBeforeAlert;
}
