using CSharpFunctionalExtensions;
using Khadra.Domain.Common;

namespace Khadra.Domain.Payables;

/// <summary>
/// Why a booking's payable is held back (payments Phase 8): not recorded, because its records contradict one
/// another or its outcome cannot be recorded yet; or recorded and left out of every settlement, because it no
/// longer matches its records or an administrator left it out. Administrators see every open hold on the work
/// queue with its reason, so an office's money is never silently missing from its balance.
/// </summary>
/// <remarks>
/// <para>
/// One open hold per booking and reason, which the database enforces. The payables pass opens and releases the
/// system's own holds as it finds them; an administrator opens and releases a manual one, audited. A released
/// hold is kept, with who released it and when: the history of why a payment was late is part of the record.
/// </para>
/// <para>
/// A payable is also not due while a refund on its booking is outstanding or a dispute on it is live. That is
/// not a hold: it is read live from the refunds and the tickets every time, so it can never say something the
/// money no longer does.
/// </para>
/// </remarks>
public sealed class OfficePayableHold : AggregateRoot
{
    public const int MaxDetailLength = 500;

    public Id BookingId { get; private set; }
    public Id DealerId { get; private set; }

    /// <summary>The payable it holds back; null for a hold that stops one being recorded.</summary>
    public Id? PayableId { get; private set; }

    public PayableHoldReason Reason { get; private set; } = null!;

    /// <summary>
    /// For a system hold, a short technical description (the issue codes, the difference found); for a manual one,
    /// the administrator's reason. Administrators see it; nobody else does.
    /// </summary>
    public string? Detail { get; private set; }

    public DateTimeOffset OpenedAt { get; private set; }

    /// <summary>The administrator who held it; null for the system's holds.</summary>
    public Id? OpenedByAdminId { get; private set; }

    /// <summary>How many passes found the system's reason still true.</summary>
    public int Checks { get; private set; }

    public DateTimeOffset LastCheckedAt { get; private set; }

    /// <summary>When the payables pass looks at the booking again. Manual holds wait for a person, not the pass.</summary>
    public DateTimeOffset? NextCheckAt { get; private set; }

    public DateTimeOffset? ReleasedAt { get; private set; }
    public Id? ReleasedByAdminId { get; private set; }
    public string? ReleaseNote { get; private set; }

    public bool IsOpen => ReleasedAt is null;

    private OfficePayableHold()
    {
    }

    private OfficePayableHold(Id id) : base(id)
    {
    }

    /// <summary>The payables pass found a reason to hold the booking's payable back.</summary>
    public static OfficePayableHold OpenBySystem(
        PayableHoldReason reason,
        Id bookingId,
        Id dealerId,
        Id? payableId,
        string? detail,
        DateTimeOffset now,
        DateTimeOffset nextCheckAt)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (!reason.BySystem)
            throw new DomainException($"{reason.Name} is an administrator's hold, not the system's.");
        if (bookingId.IsEmpty || dealerId.IsEmpty)
            throw new DomainException("A hold names its booking and its office.");
        if (reason.BeforeRecording != (payableId is null))
            throw new DomainException($"A {reason.Name} hold {(reason.BeforeRecording ? "stops a payable being recorded" : "holds a recorded payable")}.");

        return new OfficePayableHold(Id.New())
        {
            BookingId = bookingId,
            DealerId = dealerId,
            PayableId = payableId,
            Reason = reason,
            Detail = Shorten(detail),
            OpenedAt = now,
            Checks = 1,
            LastCheckedAt = now,
            NextCheckAt = nextCheckAt,
        };
    }

    /// <summary>An administrator leaves a recorded, open payable out of settlements, and says why.</summary>
    public static Result<OfficePayableHold, Error> OpenByAdmin(OfficePayable payable, Id adminUserId, string? reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(payable);
        if (adminUserId.IsEmpty)
            throw new DomainException("A manual hold names its administrator.");
        if (payable.IsSettled)
            return PayableErrors.AlreadySettled;

        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return PayableErrors.HoldReasonRequired;
        if (trimmed.Length > MaxDetailLength)
            return PayableErrors.HoldReasonTooLong;

        return new OfficePayableHold(Id.New())
        {
            BookingId = payable.BookingId,
            DealerId = payable.DealerId,
            PayableId = payable.Id,
            Reason = PayableHoldReason.Manual,
            Detail = trimmed,
            OpenedAt = now,
            OpenedByAdminId = adminUserId,
            LastCheckedAt = now,
        };
    }

    /// <summary>The pass found the system's reason still true.</summary>
    public void CheckedAgain(string? detail, DateTimeOffset now, DateTimeOffset nextCheckAt)
    {
        if (!Reason.BySystem || !IsOpen)
            throw new DomainException("Only an open system hold is checked again.");

        Checks++;
        Detail = Shorten(detail);
        LastCheckedAt = now;
        NextCheckAt = nextCheckAt;
    }

    /// <summary>The pass found the system's reason no longer true: the records were put right.</summary>
    public void ReleaseBySystem(DateTimeOffset now)
    {
        if (!Reason.BySystem)
            throw new DomainException("An administrator's hold is released by an administrator.");

        ReleasedAt ??= now;
        NextCheckAt = null;
    }

    /// <summary>An administrator releases their hold: the payable is due in the next settlement.</summary>
    public UnitResult<Error> ReleaseByAdmin(Id adminUserId, string? note, DateTimeOffset now)
    {
        if (adminUserId.IsEmpty)
            throw new DomainException("A release names its administrator.");
        if (Reason.BySystem || !IsOpen)
            return PayableErrors.NotHeld;

        var trimmed = note?.Trim();
        if (trimmed?.Length > MaxDetailLength)
            return PayableErrors.HoldReasonTooLong;

        ReleasedAt = now;
        ReleasedByAdminId = adminUserId;
        ReleaseNote = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        return UnitResult.Success<Error>();
    }

    private static string? Shorten(string? detail) =>
        string.IsNullOrWhiteSpace(detail) ? null
        : detail.Length <= MaxDetailLength ? detail
        : detail[..MaxDetailLength];
}
