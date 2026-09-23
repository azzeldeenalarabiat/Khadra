using CSharpFunctionalExtensions;
using Khadra.Domain.Common;

namespace Khadra.Domain.Bookings;

/// <summary>How the dealer knew the person at the counter was the customer.</summary>
public sealed class HandoverVerification : Enumeration
{
    /// <summary>The customer's one-time handover code (typed PIN or scanned QR) was checked.</summary>
    public static readonly HandoverVerification Code = new(1, "Code");

    /// <summary>
    /// No code: the customer could not show one (phone dead, old app). The dealer checked identity
    /// their own way and wrote down why. Audited and flagged for the platform.
    /// </summary>
    public static readonly HandoverVerification Unverified = new(2, "Unverified");

    /// <summary>Recorded while the platform did not yet ask for verification (Handover:RequireVerification off).</summary>
    public static readonly HandoverVerification NotRequired = new(3, "NotRequired");

    private HandoverVerification(int id, string name) : base(id, name)
    {
    }
}

/// <summary>What a handover record says about how it was verified.</summary>
public sealed class HandoverProof : Common.ValueObject
{
    public const int MinReasonLength = 10;
    public const int MaxReasonLength = 500;

    private HandoverProof(HandoverVerification method, Id? codeId, string? reason)
    {
        Method = method;
        CodeId = codeId;
        Reason = reason;
    }

    public HandoverVerification Method { get; }

    /// <summary>The code that was used, when one was. Never the code itself.</summary>
    public Id? CodeId { get; }

    /// <summary>Why the dealer went ahead without a code. Required for, and only for, Unverified.</summary>
    public string? Reason { get; }

    public static HandoverProof ByCode(Id codeId) => new(HandoverVerification.Code, codeId, null);

    public static HandoverProof NotRequired { get; } = new(HandoverVerification.NotRequired, null, null);

    public static Result<HandoverProof, Error> Unverified(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length < MinReasonLength)
            return HandoverErrors.ReasonRequired;
        return new HandoverProof(
            HandoverVerification.Unverified,
            null,
            trimmed.Length <= MaxReasonLength ? trimmed : trimmed[..MaxReasonLength]);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Method;
        yield return CodeId;
        yield return Reason;
    }
}

/// <summary>
/// A short-lived, one-time code the customer shows at the counter to prove the booking is theirs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only a hash is stored</b> — an HMAC keyed with a server secret, over the booking, the handover
/// type and the code. A six-digit code has a million values, so an unkeyed hash of one is reversed
/// by trying them all; the key is what makes a copy of this table useless on its own. The code is
/// shown to the customer once, when issued, and exists nowhere else.
/// </para>
/// <para>
/// <b>Bound to one booking and one handover.</b> The booking and type are inside the HMAC, so a code
/// cannot be replayed against another booking or turned from a pickup into a return even by someone
/// who can read the table.
/// </para>
/// <para>
/// <b>Short-lived, one attempt budget, one use.</b> It expires (configuration, fifteen minutes), dies
/// after a small number of wrong guesses, is spent by the handover it proves, and is superseded when
/// the customer asks for a new one. Wrong guesses are COMMITTED before the refusal is returned, so the
/// budget is real rather than decorative.
/// </para>
/// <para>
/// <b>Its own aggregate, not a child of <see cref="Booking"/>.</b> Issuing and failing codes must not
/// dirty the booking's concurrency token; otherwise a customer refreshing their code at the moment
/// the dealer submits would make the dealer's handover fail. The booking's own token still arbitrates
/// two handovers of the same booking.
/// </para>
/// </remarks>
public sealed class HandoverCode : AggregateRoot
{
    public const int HashLength = 64;

    public Id BookingId { get; private set; }
    public HandoverType Type { get; private set; } = null!;
    public string CodeHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public Id? UsedByUserId { get; private set; }
    public DateTimeOffset? SupersededAt { get; private set; }

    private HandoverCode()
    {
    }

    private HandoverCode(Id id) : base(id)
    {
    }

    public static HandoverCode Issue(Id bookingId, HandoverType type, string codeHash, DateTimeOffset now, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (bookingId.IsEmpty)
            throw new DomainException("A handover code belongs to a booking.");
        if (string.IsNullOrWhiteSpace(codeHash) || codeHash.Length != HashLength)
            throw new DomainException("A handover code is stored as a 64-character hash.");
        if (lifetime <= TimeSpan.Zero)
            throw new DomainException("A handover code must live for a positive time.");

        return new HandoverCode(Id.New())
        {
            BookingId = bookingId,
            Type = type,
            CodeHash = codeHash,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
        };
    }

    /// <summary>
    /// Checks a presented code against this one and, if it matches, spends it.
    /// </summary>
    /// <param name="matches">Whether the presented code hashes to <see cref="CodeHash"/>; computed by the caller in fixed time.</param>
    /// <remarks>
    /// Refusals are checked in the order a customer can do something about: already used, replaced by
    /// a newer code, expired, locked by wrong guesses — and only then the guess itself, which is the
    /// one that counts against the budget.
    /// </remarks>
    public UnitResult<Error> Verify(bool matches, int maxFailedAttempts, Id verifiedByUserId, DateTimeOffset now)
    {
        if (UsedAt is not null)
            return HandoverErrors.CodeUsed;
        if (SupersededAt is not null)
            return HandoverErrors.CodeInvalid;
        if (now >= ExpiresAt)
            return HandoverErrors.CodeExpired;
        if (FailedAttempts >= maxFailedAttempts)
            return HandoverErrors.CodeLocked;

        if (!matches)
        {
            FailedAttempts++;
            return FailedAttempts >= maxFailedAttempts ? HandoverErrors.CodeLocked : HandoverErrors.CodeInvalid;
        }

        UsedAt = now;
        UsedByUserId = verifiedByUserId;
        return UnitResult.Success<Error>();
    }

    /// <summary>The customer asked for a new code: this one stops working.</summary>
    public void Supersede(DateTimeOffset now) => SupersededAt ??= now;
}

public static class HandoverErrors
{
    public static readonly Error CodeInvalid =
        Error.Validation("handover.code_invalid", "That handover code is not right. Ask the customer to check it, or to show a new one.");

    public static readonly Error CodeExpired =
        Error.Validation("handover.code_expired", "That handover code has expired. Ask the customer to show a new one.");

    public static readonly Error CodeUsed =
        Error.Validation("handover.code_used", "That handover code has already been used.");

    public static readonly Error CodeLocked =
        Error.Validation("handover.code_locked", "Too many wrong codes. Ask the customer to show a new one.");

    public static readonly Error CodeRequired =
        Error.Validation("handover.code_required", "Enter the customer's handover code, or record the handover as unverified with a reason.");

    public static readonly Error ReasonRequired =
        Error.Validation("handover.reason_required", $"Say why the handover could not be verified ({HandoverProof.MinReasonLength} characters or more).");

    public static readonly Error NotAvailable =
        Error.Conflict("handover.not_available", "There is no handover to prove on this booking right now.");
}
