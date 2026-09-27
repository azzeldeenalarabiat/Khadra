using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.FinancialDocuments.Composition;
using Khadra.Application.FinancialDocuments.Issuance;
using Khadra.Application.FinancialDocuments.Queries;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.FinancialDocuments.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.FinancialDocuments.VoidFinancialDocument;

/// <summary>
/// An administrator voids an issued document that was wrong, and its correction is issued in its place
/// (payments Phase 5).
/// </summary>
public sealed record VoidFinancialDocumentCommand(Id DocumentId, Id AdminUserId, string? Reason)
    : ICommand<Result<VoidedFinancialDocumentDto, Error>>;

public sealed class VoidFinancialDocumentCommandValidator : AbstractValidator<VoidFinancialDocumentCommand>
{
    public VoidFinancialDocumentCommandValidator() =>
        RuleFor(command => command.Reason)
            .NotEmpty().WithMessage("Say why the document is being voided.")
            .MaximumLength(FinancialDocumentVoid.MaxReasonLength);
}

/// <summary>
/// Voids a CURRENT document and issues its correction, in ONE transaction, audited in the same one (owner,
/// 2026-09-27).
/// </summary>
/// <remarks>
/// <para>
/// Nothing is edited or deleted: the voided document stays readable, marked void and linked to its
/// replacement, which is the family's next version under a NEW number. A void never stands alone — if the
/// correction cannot be issued (the records contradict one another, the issuer is missing, composition
/// fails), nothing is voided and the administrator is told why. That is also what lets the issuing sweep
/// ask one question of a receipt family, "does any row exist?", and never issue a voided receipt again.
/// </para>
/// <para>
/// Only the current version can be voided; earlier versions are preserved history. Two administrators
/// voiding the same document at once: the void's key is the document's id, so one wins and the other is
/// told it is already voided.
/// </para>
/// </remarks>
public sealed partial class VoidFinancialDocumentHandler(
    IFinancialDocumentRepository documents,
    DocumentPreparation preparation,
    FinancialDocumentIssuing issuing,
    AdminActionRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<VoidFinancialDocumentHandler> logger)
    : IRequestHandler<VoidFinancialDocumentCommand, Result<VoidedFinancialDocumentDto, Error>>
{
    public async Task<Result<VoidedFinancialDocumentDto, Error>> Handle(
        VoidFinancialDocumentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;

        var document = await documents.GetByIdAsync(request.DocumentId, cancellationToken);
        if (document is null)
            return FinancialDocumentErrors.NotFound;
        if (await documents.IsVoidedAsync(document.Id, cancellationToken))
            return FinancialDocumentErrors.AlreadyVoided;
        var latest = await documents.LatestOfFamilyAsync(document.Type, document.SubjectId, cancellationToken);
        if (latest is null || latest.Id != document.Id)
            return FinancialDocumentErrors.NotCurrent;

        var voided = FinancialDocumentVoid.Record(document.Id, request.AdminUserId, request.Reason, now);
        if (voided.IsFailure)
            return voided.Error;

        // Composed from today's committed facts exactly as an original is — so a statement's correction
        // states the booking as it stands now, and a receipt's states the same immutable money again.
        var prepared = await preparation.PrepareAsync(
            document.Type, document.SubjectId, document.BookingId, now, correcting: true, cancellationToken);
        if (prepared is not Preparation.Ready ready)
        {
            return prepared is Preparation.OnHold { Reason: var reason } && reason == IssuanceHoldReason.RecordsNeedReview
                ? FinancialDocumentErrors.CorrectionNeedsReview
                : prepared is Preparation.OnHold { Reason: var missing } && missing == IssuanceHoldReason.IssuerNotConfigured
                    ? FinancialDocumentErrors.CorrectionIssuerNotConfigured
                    : FinancialDocumentErrors.CorrectionFailed;
        }

        // The kind of money never changes between a document and its correction.
        if (!string.Equals(ready.Provider, document.Provider, StringComparison.Ordinal))
            return FinancialDocumentErrors.CorrectionFailed;

        try
        {
            FinancialDocument? correction = null;
            await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    documents.AddVoid(voided.Value);
                    correction = await issuing.IssueAsync(
                        document.Type,
                        ready.Provider,
                        now,
                        document.Version + 1,
                        new DocumentReference(document.Id, document.Number),
                        isCorrection: true,
                        ready.Compose,
                        token);
                    // Labelled by the NUMBER: an audit entry can never be erased, so it names no customer.
                    audit.Record(
                        AuditAction.FinancialDocumentVoided,
                        AuditEntityType.FinancialDocument,
                        document.Id,
                        document.Number,
                        document.Number,
                        correction.Number,
                        voided.Value.Reason);
                    await unitOfWork.SaveChangesAsync(token);
                },
                cancellationToken);

            LogVoided(logger, document.Number, correction!.Number);
            return new VoidedFinancialDocumentDto(document.Id.Value, document.Number, correction.Id.Value, correction.Number);
        }
        catch (DocumentCompositionException failure)
        {
            LogCorrectionFailed(logger, document.Number, failure);
            return FinancialDocumentErrors.CorrectionFailed;
        }
        catch (UniqueConstraintConflictException conflict)
        {
            // Lost a race: another administrator's void (the void's key is the document's id), or a new
            // version the issuing sweep inserted a moment earlier. Either way nothing here was written.
            return conflict.ConstraintName is { } name && name.StartsWith("pk_financial_document_voids", StringComparison.Ordinal)
                ? FinancialDocumentErrors.AlreadyVoided
                : FinancialDocumentErrors.NotCurrent;
        }
    }

    [LoggerMessage(2614, LogLevel.Information, "{Number} was voided and replaced by {Correction}.")]
    private static partial void LogVoided(ILogger logger, string number, string correction);

    [LoggerMessage(2615, LogLevel.Error, "The correction of {Number} could not be composed. Nothing was voided.")]
    private static partial void LogCorrectionFailed(ILogger logger, string number, Exception exception);
}
