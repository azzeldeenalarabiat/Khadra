using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Auditing;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Bookings.ReadModels;
using Khadra.Application.Common;
using Khadra.Domain.Auditing;
using Khadra.Domain.Bookings;
using Khadra.Domain.Bookings.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Application.Notifications;
using Khadra.Application.Payments;
using Khadra.Domain.Payments.Repositories;
using Khadra.Domain.Notifications;
using MediatR;

namespace Khadra.Application.Bookings.AdminBookings;

// The three interventions an administrator can make in a booking.
//
// Penalties are ASSESSED, never charged (CLAUDE.md, spec 3.3): none of these takes money from
// anyone. What they DO record is the refund a paid booking's ending owes the customer (Phase 3,
// 2026-09-26), in the same save: an administrator's cancellation returns the whole payment, deposit
// included, and a no-show returns everything above the deposit. The deposit a no-show holds follows
// the dispute rules.

/// <summary>
/// Cancels a booking the platform has to step into — a dealership suspended mid-rental, a booking
/// stuck on a car that has been removed.
/// </summary>
/// <remarks>
/// Attributed to <see cref="BookingParty.Admin"/>, which assesses NO penalty. Cancelling "as" the
/// customer or the dealer would have the administrator assess a penalty against that party by hand —
/// a full deposit, in the customer's case — which is precisely the judgement spec 3.3 reserves for a
/// dispute ticket where both sides have been heard.
/// </remarks>
public sealed record CancelBookingAsAdminCommand(Id BookingId, string Reason) : ICommand<Result<BookingDto, Error>>;

/// <summary>
/// Ends a booking whose own deadline has passed: approved but unpaid past its payment window, or
/// unanswered past the dealer's.
/// </summary>
/// <remarks>
/// <para>
/// Which of the two expiries applies is the booking's status, not the caller's choice, and the aggregate refuses both
/// while the FROZEN window still has time in it — so an admin cannot expire anything early by asking.
/// </para>
/// <para>
/// <b>Loaded as stored</b> (pre-launch item 232). Every other handler loads a booking settled against the clock, and a
/// lapsed one then already reads Expired by the system, so this command used to refuse every time. Here the
/// administrator's act IS the settlement: the transition is theirs, and it is announced through the one
/// <see cref="BookingExpiryAnnouncer"/> the sweep uses, so the customer and the office are told exactly as they would
/// have been. A booking the sweep already expired and saved is refused as before; it has been announced.
/// </para>
/// </remarks>
public sealed record ExpireBookingAsAdminCommand(Id BookingId) : ICommand<Result<BookingDto, Error>>;

/// <summary>
/// Records that the customer never collected the car, once the booking's own no-show window has run
/// out. Assesses whatever that booking's terms say, which for a self-pickup is the deposit.
/// </summary>
public sealed record MarkBookingNoShowAsAdminCommand(Id BookingId) : ICommand<Result<BookingDto, Error>>;

public sealed class CancelBookingAsAdminCommandValidator : AbstractValidator<CancelBookingAsAdminCommand>
{
    public CancelBookingAsAdminCommandValidator()
    {
        // Required, and bounded by the column behind it (bookings.cancellation_reason).
        RuleFor(command => command.Reason)
            .NotEmpty()
            .WithMessage("A reason is required: it is shown to both parties and written to the audit log.")
            .MaximumLength(1000);
    }
}

public sealed class AdminBookingCommandHandlers(
    IBookingRepository bookings,
    IPaymentRepository payments,
    IBookingReader reader,
    AdminActionRecorder audit,
    DealerTeamNotifier team,
    IDealerRepository dealers,
    BookingExpiryAnnouncer announcer,
    ICurrentActor actor,
    IUnitOfWork unitOfWork,
    IClock clock) :
    IRequestHandler<CancelBookingAsAdminCommand, Result<BookingDto, Error>>,
    IRequestHandler<ExpireBookingAsAdminCommand, Result<BookingDto, Error>>,
    IRequestHandler<MarkBookingNoShowAsAdminCommand, Result<BookingDto, Error>>
{
    public Task<Result<BookingDto, Error>> Handle(CancelBookingAsAdminCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ActAsync(
            request.BookingId,
            (booking, now) => booking.Cancel(BookingParty.Admin, actor.UserId, request.Reason, now),
            AuditAction.BookingCancelledByAdmin,
            request.Reason,
            NotificationKind.YourBookingCancelled,
            _ => NotificationKind.BookingCancelledByAdmin,
            cancellationToken);
    }

    public async Task<Result<BookingDto, Error>> Handle(ExpireBookingAsAdminCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var booking = await bookings.GetByIdAsStoredAsync(request.BookingId, cancellationToken);
        if (booking is null)
            return BookingErrors.NotFound;

        var previousStatus = booking.Status.Name;
        var now = clock.UtcNow;
        // Which expiry applies is the booking's own state, not something the caller chooses: approved but unpaid past
        // its payment window, or unanswered past the dealer's own. Any other status falls through to the aggregate,
        // which answers with the right refusal.
        var lapse = booking.Status == BookingStatus.Approved ? BookingLapseKind.Unpaid : BookingLapseKind.Unanswered;
        var outcome = lapse == BookingLapseKind.Unpaid
            ? booking.ExpireUnpaid(now, actor.UserId)
            : booking.ExpireUnanswered(now, actor.UserId);
        if (outcome.IsFailure)
            return outcome.Error;

        // As every admin action records its ending's refund in the same save. An expiry ends a booking nobody has paid
        // for, so there is none, and nothing about money changes here.
        await BookingEndingRefunds.RecordAsync(booking, payments, now, cancellationToken);

        audit.Record(
            AuditAction.BookingExpired,
            AuditEntityType.Booking,
            booking.Id,
            booking.Reference.Value,
            previousStatus,
            booking.Status.Name,
            reason: null);

        // Announced as the sweep announces an expiry, in the same save: the customer always, and the office only of an
        // approval nobody paid for, never of a request it let lapse (C7).
        await announcer.AnnounceAsync(booking, lapse, now, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BookingDto.From(booking, await reader.ContextAsync(booking.Id, cancellationToken), clock.UtcNow);
    }

    public Task<Result<BookingDto, Error>> Handle(MarkBookingNoShowAsAdminCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ActAsync(
            request.BookingId,
            (booking, now) => booking.MarkNoShow(now, actor.UserId),
            AuditAction.BookingMarkedNoShow,
            reason: null,
            NotificationKind.YourBookingMarkedNoShow,
            _ => NotificationKind.BookingMarkedNoShow,
            cancellationToken);
    }

    /// <summary>
    /// Load, let the aggregate judge, audit and commit — in that order, in one transaction.
    /// </summary>
    /// <remarks>
    /// The three actions differ only in the method they call and the action they record. Every rule
    /// about whether the move is allowed lives in the aggregate, against the terms that booking
    /// froze; this makes no judgement of its own, which is why an admin cannot expire a booking whose
    /// window has not run out.
    /// </remarks>
    private async Task<Result<BookingDto, Error>> ActAsync(
        Id bookingId,
        Func<Booking, DateTimeOffset, UnitResult<Error>> act,
        AuditAction action,
        string? reason,
        NotificationKind customerKind,
        Func<string, NotificationKind?> officeKind,
        CancellationToken cancellationToken)
    {
        var booking = await bookings.GetByIdAsync(bookingId, cancellationToken);
        if (booking is null)
            return BookingErrors.NotFound;

        var previousStatus = booking.Status.Name;
        var now = clock.UtcNow;
        var outcome = act(booking, now);
        if (outcome.IsFailure)
            return outcome.Error;

        // The refund this ending owes, in THIS save: the whole payment for an administrator's
        // cancellation before pickup (owner, 2026-09-26), everything above the deposit for a no-show.
        await BookingEndingRefunds.RecordAsync(booking, payments, now, cancellationToken);

        audit.Record(
            action,
            AuditEntityType.Booking,
            booking.Id,
            booking.Reference.Value,
            previousStatus,
            booking.Status.Name,
            reason);

        // The customer hears what happened to their booking, in the same transaction as the action —
        // the same rule as the audit entry above. Named by the gallery, as every customer row is.
        var context = await reader.ContextAsync(booking.Id, cancellationToken);
        await team.NotifyCustomerAsync(
            booking.CustomerId,
            context.DealerName,
            customerKind,
            clock.UtcNow,
            booking.Id,
            booking.Reference.Value);

        // And the office, as Khadra's act, in the console and by email (Fix & Polish Wave 3, C5). The
        // dealership is read, never written.
        if (officeKind(previousStatus) is { } kind && await dealers.GetByIdAsync(booking.DealerId, cancellationToken) is { } dealer)
            await team.NotifyTeamFromPlatformAsync(dealer, kind, clock.UtcNow, booking.Id, booking.Reference.Value);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Read again after the save, so the answer carries the refund this action just recorded.
        return BookingDto.From(booking, await reader.ContextAsync(booking.Id, cancellationToken), clock.UtcNow);
    }
}
