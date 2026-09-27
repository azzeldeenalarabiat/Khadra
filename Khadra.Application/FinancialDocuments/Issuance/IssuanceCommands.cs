using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.FinancialDocuments.Composition;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.FinancialDocuments.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.FinancialDocuments.Issuance;

/// <summary>The document families owed a document right now, for one settlement pass.</summary>
public sealed record ListFinancialDocumentWorkQuery : IQuery<IReadOnlyList<IssuanceCandidate>>;

/// <summary>
/// Issues ONE document for one family, in its own scope and transaction (payments Phase 5): compose the
/// snapshot, take the number, insert the row. The settlement pass sends one of these per candidate.
/// </summary>
public sealed record IssueFinancialDocumentCommand(FinancialDocumentType Type, Id SubjectId, Id BookingId)
    : ICommand<IssuanceOutcome>;

/// <summary>
/// Records that a family is owed a document it could not get, in a scope of its own: after a failed
/// transaction the issuing scope's context still holds the row that was not inserted.
/// </summary>
public sealed record RecordFinancialDocumentHoldCommand(
    FinancialDocumentType Type,
    Id SubjectId,
    Id BookingId,
    IssuanceHoldReason Reason,
    string? Error) : ICommand<Unit>;

/// <summary>
/// A hold names its family and why. The error text is the system's own, never a person's, and the hold keeps
/// it to its column; nothing here is user input, but every command carrying text is validated.
/// </summary>
public sealed class RecordFinancialDocumentHoldCommandValidator : AbstractValidator<RecordFinancialDocumentHoldCommand>
{
    public RecordFinancialDocumentHoldCommandValidator()
    {
        RuleFor(command => command.Type).NotNull();
        RuleFor(command => command.Reason).NotNull();
        RuleFor(command => command.SubjectId).Must(id => !id.IsEmpty);
        RuleFor(command => command.BookingId).Must(id => !id.IsEmpty);
    }
}

/// <summary>What one issuing step did.</summary>
/// <param name="Hold">Set when the family must go on hold; the pass records it in a fresh scope.</param>
/// <param name="Skipped">Why nothing was owed after all, when nothing was issued and nothing is held.</param>
public sealed record IssuanceOutcome(
    Id? DocumentId,
    string? Number,
    Preparation.OnHold? Hold,
    string? Skipped)
{
    public static IssuanceOutcome Issued(FinancialDocument document) => new(document.Id, document.Number, null, null);

    public static IssuanceOutcome Held(Preparation.OnHold hold) => new(null, null, hold, null);

    public static IssuanceOutcome NotOwed(string why) => new(null, null, null, why);
}

public sealed class ListFinancialDocumentWorkHandler(
    IFinancialDocumentCandidateReader candidates,
    IFinancialDocumentSettings settings,
    IClock clock)
    : IRequestHandler<ListFinancialDocumentWorkQuery, IReadOnlyList<IssuanceCandidate>>
{
    public Task<IReadOnlyList<IssuanceCandidate>> Handle(ListFinancialDocumentWorkQuery request, CancellationToken cancellationToken) =>
        candidates.ListAsync(
            clock.UtcNow,
            settings.LateCommitMargin,
            settings.Issuer is not null,
            settings.MaxDocumentsPerPass,
            cancellationToken);
}

/// <summary>
/// Issues one document, or says why not (payments Phase 5).
/// </summary>
/// <remarks>
/// <para>
/// <b>The money never waits for its paperwork.</b> This runs after money is recorded, never inside the
/// transaction that records it: a defect here can never stop a payment or a refund being recorded, the
/// number's row lock stays off the money path, and the money handlers stay as they are.
/// </para>
/// <para>
/// A receipt family is owed exactly when no row of it exists — an administrator's void always carries
/// its correction, so a voided receipt is never issued again. A statement is owed when the booking's
/// checkpoint fingerprint differs from its latest version's.
/// </para>
/// </remarks>
public sealed partial class IssueFinancialDocumentHandler(
    DocumentPreparation preparation,
    FinancialDocumentIssuing issuing,
    IFinancialDocumentRepository documents,
    IFinancialDocumentIssuanceHoldRepository holds,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<IssueFinancialDocumentHandler> logger)
    : IRequestHandler<IssueFinancialDocumentCommand, IssuanceOutcome>
{
    public async Task<IssuanceOutcome> Handle(IssueFinancialDocumentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (type, subjectId) = (request.Type, request.SubjectId);
        var now = clock.UtcNow;

        if (type.IsReceipt && await documents.LatestOfFamilyAsync(type, subjectId, cancellationToken) is not null)
            return IssuanceOutcome.NotOwed("already_issued");

        var prepared = await preparation.PrepareAsync(type, subjectId, request.BookingId, now, correcting: false, cancellationToken);
        switch (prepared)
        {
            case Preparation.OnHold hold:
                return IssuanceOutcome.Held(hold);

            case Preparation.NotOwed notOwed:
                // Nothing is owed, so nothing is on hold: a family whose records were put right, or whose
                // statement turned out unchanged, leaves the work queue.
                if (await holds.FindAsync(type, subjectId, cancellationToken) is { IsResolved: false } open)
                {
                    open.Resolve(now);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }

                return IssuanceOutcome.NotOwed(notOwed.Why);

            case Preparation.Ready ready:
                return await IssueAsync(request, ready, now, cancellationToken);

            default:
                throw new InvalidOperationException("An unknown preparation.");
        }
    }

    private async Task<IssuanceOutcome> IssueAsync(
        IssueFinancialDocumentCommand request,
        Preparation.Ready ready,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (type, subjectId) = (request.Type, request.SubjectId);
        var (version, previous) = (1, (DocumentReference?)null);
        if (type == FinancialDocumentType.BookingStatement
            && await documents.LatestOfFamilyAsync(type, subjectId, cancellationToken) is { } latest)
        {
            (version, previous) = (latest.Version + 1, new DocumentReference(latest.Id, latest.Number));
        }

        try
        {
            FinancialDocument? issued = null;
            await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    issued = await issuing.IssueAsync(type, ready.Provider, now, version, previous, isCorrection: false, ready.Compose, token);
                    if (await holds.FindAsync(type, subjectId, token) is { } hold)
                        hold.Resolve(now);
                    await unitOfWork.SaveChangesAsync(token);
                },
                cancellationToken);

            LogIssued(logger, issued!.Number, type.Name, request.BookingId.Value);
            return IssuanceOutcome.Issued(issued);
        }
        catch (DocumentCompositionException failure)
        {
            // A defect: logged at Error, and the family goes on hold so an administrator sees it.
            LogCompositionFailed(logger, type.Name, subjectId.Value, failure);
            return IssuanceOutcome.Held(new Preparation.OnHold(IssuanceHoldReason.SnapshotFailed, failure.Message));
        }
        catch (UniqueConstraintConflictException)
        {
            // Another issuer inserted this family's document first — or an administrator's correction took
            // the version. The transaction rolled back, the number with it; the next pass looks again.
            return IssuanceOutcome.NotOwed("lost_race");
        }
    }

    [LoggerMessage(2610, LogLevel.Information, "Issued {Number} ({Type}) for booking {BookingId}.")]
    private static partial void LogIssued(ILogger logger, string number, string type, Guid bookingId);

    [LoggerMessage(2611, LogLevel.Error, "A {Type} for {SubjectId} could not be composed. It is on hold until the defect is fixed.")]
    private static partial void LogCompositionFailed(ILogger logger, string type, Guid subjectId, Exception exception);
}

/// <summary>
/// Puts a family on hold, or keeps it there with one more failed attempt (payments Phase 5). One row per
/// family, upserted; each failure waits longer before the next attempt, up to the configured maximum.
/// </summary>
public sealed partial class RecordFinancialDocumentHoldHandler(
    IFinancialDocumentIssuanceHoldRepository holds,
    IFinancialDocumentSettings settings,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<RecordFinancialDocumentHoldHandler> logger)
    : IRequestHandler<RecordFinancialDocumentHoldCommand, Unit>
{
    public async Task<Unit> Handle(RecordFinancialDocumentHoldCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;

        var hold = await holds.FindAsync(request.Type, request.SubjectId, cancellationToken);
        var attempts = hold is { IsResolved: false } ? hold.Attempts + 1 : 1;
        var nextAttemptAt = now.Add(Delay(request.Reason, attempts));

        if (hold is null)
            holds.Add(FinancialDocumentIssuanceHold.Open(request.Type, request.SubjectId, request.BookingId, request.Reason, request.Error, now, nextAttemptAt));
        else
            hold.Fail(request.Reason, request.Error, now, nextAttemptAt);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintConflictException)
        {
            // Another process opened this family's hold a moment earlier: its row stands, and it says the same.
            return Unit.Value;
        }

        if (request.Reason == IssuanceHoldReason.SnapshotFailed)
            LogSnapshotHeld(logger, request.Type.Name, request.SubjectId.Value, attempts, request.Error);
        else if (attempts == 1)
            LogHeld(logger, request.Type.Name, request.SubjectId.Value, request.Reason.Name, request.Error);

        return Unit.Value;
    }

    /// <summary>
    /// Doubling from the first delay, capped. A missing issuer waits the longest straight away: nothing can
    /// change it but new configuration, and the candidates stop waiting on those holds once an issuer exists.
    /// </summary>
    private TimeSpan Delay(IssuanceHoldReason reason, int attempts)
    {
        if (reason == IssuanceHoldReason.IssuerNotConfigured)
            return settings.RetryMaxDelay;

        var factor = Math.Pow(2, Math.Min(attempts - 1, 30));
        var delay = TimeSpan.FromTicks((long)Math.Min(settings.RetryInitialDelay.Ticks * factor, settings.RetryMaxDelay.Ticks));
        return delay < settings.RetryInitialDelay ? settings.RetryInitialDelay : delay;
    }

    [LoggerMessage(2612, LogLevel.Warning, "A {Type} for {SubjectId} is on hold ({Reason}): {Error}")]
    private static partial void LogHeld(ILogger logger, string type, Guid subjectId, string reason, string? error);

    [LoggerMessage(2613, LogLevel.Error, "A {Type} for {SubjectId} is still on hold after {Attempts} attempts because it could not be composed: {Error}")]
    private static partial void LogSnapshotHeld(ILogger logger, string type, Guid subjectId, int attempts, string? error);
}
