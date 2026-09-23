using CSharpFunctionalExtensions;
using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Domain.Auditing;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.Bookings.Handover;

/// <summary>What the customer's phone shows at the counter.</summary>
/// <param name="Type">"Pickup" or "Return".</param>
/// <param name="Code">Six digits. Shown once; the platform keeps only a keyed hash of it.</param>
/// <param name="QrPayload">The same code as a QR, with the booking reference so a scanner can say whose it is.</param>
/// <param name="ExpiresAt">When it stops working. The customer can ask for a new one at any time.</param>
public sealed record HandoverCodeDto(string Type, string Code, string QrPayload, DateTimeOffset ExpiresAt)
{
    // A record prints every property, and this one carries a live credential.
    public override string ToString() => $"{nameof(HandoverCodeDto)} {{ Type = {Type}, ExpiresAt = {ExpiresAt:O} }}";
}

/// <summary>The customer asks for a code to prove, at the counter, that this booking is theirs.</summary>
public sealed record IssueHandoverCodeCommand(Id BookingId) : ICommand<Result<HandoverCodeDto, Error>>;

/// <remarks>
/// <para>
/// <b>Which handover is decided by the booking</b>, never by the caller: Confirmed means the car is
/// waiting to be collected, PickedUp means it is out and coming back. Any other status has nothing to
/// prove and is refused. A code is available the whole time the booking is Confirmed rather than only
/// near the start, because the dealer may hand the car over early and the code must never be stricter
/// than the handover it proves.
/// </para>
/// <para>
/// <b>Asking again replaces the code.</b> The previous one stops working at once, so a screenshot a
/// customer shared, or a code read over their shoulder, is dead the moment they refresh.
/// </para>
/// <para>
/// Only the booking's own customer: anybody else gets not-found, the same as a booking that does not
/// exist.
/// </para>
/// </remarks>
public sealed class IssueHandoverCodeHandler(
    IBookingRepository bookings,
    IHandoverCodeRepository codes,
    IHandoverCodeService service,
    IHandoverSettings settings,
    ICurrentActor actor,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<IssueHandoverCodeCommand, Result<HandoverCodeDto, Error>>
{
    public async Task<Result<HandoverCodeDto, Error>> Handle(IssueHandoverCodeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var booking = await bookings.GetByIdAsync(request.BookingId, cancellationToken);
        if (booking is null || actor.UserId is not { } userId || booking.CustomerId != userId)
            return BookingErrors.NotFound;

        var type = booking.Status == BookingStatus.Confirmed ? HandoverType.Pickup
            : booking.Status == BookingStatus.PickedUp ? HandoverType.Return
            : null;
        if (type is null)
            return HandoverErrors.NotAvailable;

        var now = clock.UtcNow;
        foreach (var previous in await codes.ListCurrentAsync(booking.Id, type, cancellationToken))
            previous.Supersede(now);

        var code = service.Generate();
        var issued = HandoverCode.Issue(booking.Id, type, service.Hash(booking.Id, type, code), now, settings.CodeLifetime);
        await codes.AddAsync(issued, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new HandoverCodeDto(
            type.Name,
            code,
            $"khadra-handover:v1:{booking.Reference.Value}:{code}",
            issued.ExpiresAt);
    }
}

/// <summary>
/// Decides how a handover is proved, before the booking records it: by the customer's code, as
/// unverified with the dealer's reason, or — while verification is not yet required — not at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>A wrong code is committed before the refusal is returned.</b> Otherwise the failed-attempt count
/// would be rolled back with the request and the attempt limit would never be reached. Only the code
/// row is saved on that path; the booking is untouched.
/// </para>
/// <para>
/// <b>A correct code is spent in the same save as the handover it proves</b> — the caller's
/// SaveChanges. If the handover then fails to save (another member of staff recorded it a moment
/// earlier), the code is not spent either.
/// </para>
/// <para>
/// <b>Cross-dealership safety</b> comes from the caller, which has already loaded the booking through
/// the actor's own dealership; this only ever looks up codes by that booking's id.
/// </para>
/// </remarks>
public sealed class HandoverVerifier(
    IHandoverCodeRepository codes,
    IHandoverCodeService service,
    IHandoverSettings settings,
    AdminActionRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result<HandoverProof, Error>> ProveAsync(
        Booking booking,
        HandoverType type,
        string? presentedCode,
        string? unverifiedReason,
        Id actorUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(type);

        var presented = Presented(presentedCode, booking);
        if (presented is not null)
        {
            var current = await codes.GetCurrentAsync(booking.Id, type, cancellationToken);
            if (current is null)
                return HandoverErrors.CodeInvalid;

            var lockedBefore = current.FailedAttempts >= settings.MaxFailedAttempts;
            var verified = current.Verify(
                presented.Value.ForThisBooking && service.Matches(current.CodeHash, booking.Id, type, presented.Value.Code),
                settings.MaxFailedAttempts,
                actorUserId,
                clock.UtcNow);
            if (verified.IsFailure)
            {
                // The guess that used up the budget leaves a trace: somebody at this dealership typed
                // enough wrong codes for this booking to lock the customer's code.
                if (!lockedBefore && current.FailedAttempts >= settings.MaxFailedAttempts)
                {
                    audit.Record(
                        AuditAction.HandoverCodeLocked,
                        AuditEntityType.Booking,
                        booking.Id,
                        booking.Reference.Value,
                        booking.Status.Name,
                        $"{type.Name} code locked after {current.FailedAttempts} wrong tries");
                }

                // Commit the failed attempt (and any audit entry) now; the handover is not going ahead.
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return verified.Error;
            }

            return HandoverProof.ByCode(current.Id);
        }

        if (!string.IsNullOrWhiteSpace(unverifiedReason))
            return HandoverProof.Unverified(unverifiedReason);

        return settings.RequireVerification
            ? HandoverErrors.CodeRequired
            : HandoverProof.NotRequired;
    }

    /// <summary>
    /// What the dealer entered, as the six digits to check and whether it was aimed at this booking.
    /// </summary>
    /// <remarks>
    /// Two shapes arrive: the digits, typed, and the whole QR payload, scanned —
    /// <c>khadra-handover:v1:{reference}:{code}</c>, which a keyboard-wedge scanner types into the same
    /// field. A payload for ANOTHER booking is a wrong guess and counts as one. Spaces and dashes in
    /// typed digits are ignored, because a code read aloud arrives with them.
    /// </remarks>
    private static (string Code, bool ForThisBooking)? Presented(string? raw, Booking booking)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
            return null;

        const string prefix = "khadra-handover:";
        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var parts = value.Split(':');
            return parts.Length == 4 && parts[1] == "v1"
                ? (parts[3], string.Equals(parts[2], booking.Reference.Value, StringComparison.OrdinalIgnoreCase))
                : (value, false);
        }

        return (new string([.. value.Where(char.IsAsciiDigit)]), true);
    }

    /// <summary>
    /// Writes the audit entry for a handover that was proved, or deliberately not proved. Staged; the
    /// caller's save commits it with the handover.
    /// </summary>
    public void Audit(Booking booking, HandoverType type, HandoverProof proof, string previousStatus)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(proof);

        if (proof.Method == HandoverVerification.NotRequired)
            return;

        audit.Record(
            proof.Method == HandoverVerification.Code ? AuditAction.HandoverVerified : AuditAction.HandoverUnverified,
            AuditEntityType.Booking,
            booking.Id,
            booking.Reference.Value,
            previousStatus,
            $"{booking.Status.Name} ({type.Name}, {proof.Method.Name})",
            proof.Reason);
    }
}
