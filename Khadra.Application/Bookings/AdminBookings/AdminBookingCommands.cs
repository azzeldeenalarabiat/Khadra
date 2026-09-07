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
using MediatR;

namespace Khadra.Application.Bookings.AdminBookings;

// The three interventions an administrator can make in a booking.
//
// None of them moves money, and none of them may. Penalties are ASSESSED, never charged (CLAUDE.md,
// spec 3.3): the aggregate records what the booking's own frozen terms say is owed, and money only
// ever moves when an Admin resolves a dispute ticket. Nothing here touches the Payments context,
// which is not built.

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
/// The stand-in for a background job that does not exist yet (pre-launch item 4). Which of the two
/// expiries applies is the booking's status, not the caller's choice, and the aggregate refuses both
/// while the FROZEN window still has time in it — so an admin cannot expire anything early by asking.
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
    IBookingReader reader,
    AdminActionRecorder audit,
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
            cancellationToken);
    }

    public Task<Result<BookingDto, Error>> Handle(ExpireBookingAsAdminCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ActAsync(
            request.BookingId,
            // Which expiry applies is the booking's own state, not something the caller chooses:
            // approved but unpaid past its payment window, or unanswered past the dealer's own. Any other
            // status falls through to the aggregate, which answers with the right refusal.
            (booking, now) => booking.Status == BookingStatus.Approved
                ? booking.ExpireUnpaid(now, actor.UserId)
                : booking.ExpireUnanswered(now, actor.UserId),
            AuditAction.BookingExpired,
            reason: null,
            cancellationToken);
    }

    public Task<Result<BookingDto, Error>> Handle(MarkBookingNoShowAsAdminCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ActAsync(
            request.BookingId,
            (booking, now) => booking.MarkNoShow(now, actor.UserId),
            AuditAction.BookingMarkedNoShow,
            reason: null,
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
        CancellationToken cancellationToken)
    {
        var booking = await bookings.GetByIdAsync(bookingId, cancellationToken);
        if (booking is null)
            return BookingErrors.NotFound;

        var previousStatus = booking.Status.Name;
        var outcome = act(booking, clock.UtcNow);
        if (outcome.IsFailure)
            return outcome.Error;

        audit.Record(
            action,
            AuditEntityType.Booking,
            booking.Id,
            booking.Reference.Value,
            previousStatus,
            booking.Status.Name,
            reason);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var context = await reader.ContextAsync(booking.Id, cancellationToken);
        return BookingDto.From(booking, context, clock.UtcNow);
    }
}
