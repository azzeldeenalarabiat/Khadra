using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.Payments.ReadModels;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.Payments;
using Khadra.Domain.Payments.Repositories;
using MediatR;

namespace Khadra.Application.Payments.AdminPayments;

/// <summary>
/// An administrator says a capture incident's money has been dealt with at the provider (Wave 4, B1).
/// </summary>
/// <param name="Note">How it was dealt with. Required: an incident is closed by a person's account of it.</param>
public sealed record MarkPaymentIncidentHandledCommand(Id PaymentId, Id IncidentId, Id AdminUserId, string? Note)
    : ICommand<UnitResult<Error>>;

public sealed class MarkPaymentIncidentHandledCommandValidator : AbstractValidator<MarkPaymentIncidentHandledCommand>
{
    public MarkPaymentIncidentHandledCommandValidator() =>
        RuleFor(command => command.Note)
            .NotEmpty().WithMessage("Say how the money was dealt with.")
            .MaximumLength(PaymentIncident.MaxNoteLength);
}

/// <summary>
/// Closes one capture incident, audited in the same save. Nothing here moves money: the incident is a record that a
/// person looked at money the provider took, and this is that person's account of what they did about it.
/// </summary>
public sealed class MarkPaymentIncidentHandledHandler(
    IPaymentIncidentRepository incidents,
    IPaymentRepository payments,
    IPaymentAdminReader reader,
    AdminActionRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<MarkPaymentIncidentHandledCommand, UnitResult<Error>>
{
    public async Task<UnitResult<Error>> Handle(MarkPaymentIncidentHandledCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var incident = await incidents.GetByIdAsync(request.IncidentId, cancellationToken);
        // An incident is reached through its own payment's page; one named under another payment is not found.
        if (incident is null || incident.PaymentId != request.PaymentId)
            return UnitResult.Failure(PaymentErrors.IncidentNotFound);

        var handled = incident.MarkHandled(request.AdminUserId, request.Note!, clock.UtcNow);
        if (handled.IsFailure)
            return handled;

        // Labelled by the booking's reference, as every money entry is: the audit log is append-only and names no
        // customer. The payment's id stands in should its booking no longer resolve.
        var payment = await payments.GetByIdAsync(incident.PaymentId, cancellationToken);
        var booking = payment is null ? null : await reader.BookingLinkAsync(payment.BookingId, cancellationToken);
        audit.Record(
            AuditAction.PaymentIncidentHandled,
            AuditEntityType.PaymentIncident,
            incident.Id,
            booking?.Reference ?? incident.PaymentId.Value.ToString(),
            null,
            incident.Kind.Name,
            incident.HandledNote);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Two administrators at once: the other one's account stands, and this one is told so.
            return UnitResult.Failure(PaymentErrors.IncidentAlreadyHandled);
        }

        return UnitResult.Success<Error>();
    }
}
