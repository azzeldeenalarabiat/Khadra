using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.Payables.Dtos;
using Khadra.Application.Payables.ReadModels;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.Payables;
using Khadra.Domain.Payables.Repositories;
using MediatR;

namespace Khadra.Application.Payables.Holds;

/// <summary>
/// An administrator leaves one booking's payable out of settlements, and says why (payments Phase 8): the manual,
/// audited way to settle an office's balance without it.
/// </summary>
public sealed record HoldOfficePayableCommand(Id PayableId, Id AdminUserId, string? Reason)
    : ICommand<Result<OfficePayableDto, Error>>;

public sealed class HoldOfficePayableCommandValidator : AbstractValidator<HoldOfficePayableCommand>
{
    public HoldOfficePayableCommandValidator() =>
        RuleFor(command => command.Reason)
            .NotEmpty().WithMessage("Say why the payable is being held.")
            .MaximumLength(OfficePayableHold.MaxDetailLength);
}

/// <summary>An administrator lets a payable they held back into the next settlement.</summary>
public sealed record ReleaseOfficePayableCommand(Id PayableId, Id AdminUserId, string? Note)
    : ICommand<Result<OfficePayableDto, Error>>;

public sealed class ReleaseOfficePayableCommandValidator : AbstractValidator<ReleaseOfficePayableCommand>
{
    public ReleaseOfficePayableCommandValidator() =>
        RuleFor(command => command.Note).MaximumLength(OfficePayableHold.MaxDetailLength);
}

/// <summary>
/// Holds or releases a payable, audited in the same save (payments Phase 8). One manual hold per booking at a time,
/// which the database enforces: two administrators holding at once collide, and the second is told it is held.
/// </summary>
public sealed class OfficePayableHoldHandlers(
    IOfficePayableRepository payables,
    IOfficePayableHoldRepository holds,
    IOfficeLedgerReader ledger,
    AdminActionRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<HoldOfficePayableCommand, Result<OfficePayableDto, Error>>,
      IRequestHandler<ReleaseOfficePayableCommand, Result<OfficePayableDto, Error>>
{
    public async Task<Result<OfficePayableDto, Error>> Handle(HoldOfficePayableCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;

        var payable = await payables.GetAsync(request.PayableId, cancellationToken);
        if (payable is null)
            return PayableErrors.NotFound;
        var open = await holds.ListOpenForBookingAsync(payable.BookingId, cancellationToken);
        if (open.Any(hold => hold.Reason == PayableHoldReason.Manual))
            return PayableErrors.AlreadyHeld;

        var hold = OfficePayableHold.OpenByAdmin(payable, request.AdminUserId, request.Reason, now);
        if (hold.IsFailure)
            return hold.Error;

        holds.Add(hold.Value);
        // Labelled by the booking's reference: an audit entry can never be erased, so it names no customer.
        audit.Record(
            AuditAction.OfficePayableHeld,
            AuditEntityType.OfficePayable,
            payable.Id,
            payable.BookingReference,
            null,
            PayableHoldReason.Manual.Name,
            hold.Value.Detail);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintConflictException)
        {
            return PayableErrors.AlreadyHeld;
        }

        return OfficePayableDto.ForAdmin((await ledger.PayableAsync(payable.Id, cancellationToken))!);
    }

    public async Task<Result<OfficePayableDto, Error>> Handle(ReleaseOfficePayableCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;

        var payable = await payables.GetAsync(request.PayableId, cancellationToken);
        if (payable is null)
            return PayableErrors.NotFound;
        var hold = (await holds.ListOpenForBookingAsync(payable.BookingId, cancellationToken))
            .FirstOrDefault(candidate => candidate.Reason == PayableHoldReason.Manual);
        if (hold is null)
            return PayableErrors.NotHeld;

        var released = hold.ReleaseByAdmin(request.AdminUserId, request.Note, now);
        if (released.IsFailure)
            return released.Error;

        audit.Record(
            AuditAction.OfficePayableReleased,
            AuditEntityType.OfficePayable,
            payable.Id,
            payable.BookingReference,
            PayableHoldReason.Manual.Name,
            null,
            hold.ReleaseNote);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return PayableErrors.NotHeld;
        }

        return OfficePayableDto.ForAdmin((await ledger.PayableAsync(payable.Id, cancellationToken))!);
    }
}
