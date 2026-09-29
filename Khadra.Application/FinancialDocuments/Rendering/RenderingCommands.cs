using System.Security.Cryptography;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.FinancialDocuments.Composition;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.FinancialDocuments.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.FinancialDocuments.Rendering;

/// <summary>The PDFs owed right now, for one settlement pass (payments Phase 6).</summary>
/// <param name="Excluded">Pairs this process already failed to draw: a defect, which drawing again will not fix.</param>
public sealed record ListFinancialDocumentRenditionWorkQuery(IReadOnlyCollection<RenditionCandidate> Excluded)
    : IQuery<IReadOnlyList<RenditionCandidate>>;

/// <summary>
/// Draws ONE document's PDF in one language — as issued, or as its voided copy — stores it privately and records
/// it, in its own scope (payments Phase 6). The settlement pass sends one of these per candidate.
/// </summary>
public sealed record RenderFinancialDocumentCommand(Id DocumentId, Language Language, RenditionKind Kind) : ICommand<RenditionOutcome>;

/// <summary>What one rendering step did.</summary>
/// <param name="Skipped">Why nothing was drawn, when nothing was.</param>
/// <param name="CannotBeDrawn">
/// This document cannot be drawn as it stands — a defect, logged at Error. Drawing it again in this process
/// would fail the same way, so the pass leaves it until the next start.
/// </param>
/// <param name="StorageFailed">The bytes could not be stored. The pass stops drawing until its next tick.</param>
public sealed record RenditionOutcome(Id? RenditionId, string? Skipped, bool CannotBeDrawn, bool StorageFailed)
{
    public static RenditionOutcome Rendered(FinancialDocumentRendition rendition) => new(rendition.Id, null, false, false);

    public static RenditionOutcome NotOwed(string why) => new(null, why, false, false);

    public static RenditionOutcome Undrawable(string why) => new(null, why, true, false);

    public static RenditionOutcome NotStored(string why) => new(null, why, false, true);
}

public sealed class ListFinancialDocumentRenditionWorkHandler(
    IFinancialDocumentRenditionWorkReader reader,
    IFinancialDocumentPdfRenderer renderer,
    IFinancialDocumentSettings settings)
    : IRequestHandler<ListFinancialDocumentRenditionWorkQuery, IReadOnlyList<RenditionCandidate>>
{
    public async Task<IReadOnlyList<RenditionCandidate>> Handle(
        ListFinancialDocumentRenditionWorkQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A host that cannot draw at all said so once, at boot. Asking it to draw every document owed would
        // only say it again, once per document, every minute.
        if (!renderer.Probe().IsReady)
            return [];

        return await reader.ListAsync(
            DocumentPrintLayout.SupportedSchemaVersions,
            request.Excluded,
            settings.MaxRenditionsPerPass,
            cancellationToken);
    }
}

/// <summary>
/// Draws one issued document's PDF in one language (payments Phase 6).
/// </summary>
/// <remarks>
/// <para>
/// <b>From the stored snapshot, once.</b> The document row and its snapshot are the official record; a PDF
/// is a picture of it. It is drawn only from a snapshot that still hashes to what was issued, through the
/// print layout that reads the snapshot the way every other reader does — and a snapshot it cannot read is
/// refused whole rather than drawn in part.
/// </para>
/// <para>
/// <b>Stored before it is recorded.</b> The bytes go to private storage under a fresh key, then the row
/// recording them commits. A crash between the two leaves bytes nothing points at, never a row pointing at
/// nothing; a second process drawing the same PDF loses on the unique index and removes its own copy.
/// </para>
/// <para>
/// <b>A voided copy</b> (owner, 2026-09-29) is the same document drawn after its void, stamped VOID and saying
/// when it was voided and what replaced it. Every fact on it is final: the void is unique and append-only, and the
/// correction is the voided version plus one, issued with the void — never the family's latest, which a later
/// checkpoint may have moved on from. The void's reason never reaches it: it is the administrators' alone.
/// </para>
/// </remarks>
public sealed partial class RenderFinancialDocumentHandler(
    IFinancialDocumentRepository documents,
    IFinancialDocumentRenditionRepository renditions,
    IFinancialDocumentPdfRenderer renderer,
    IDocumentStorage storage,
    IUnitOfWork unitOfWork,
    IReportingCalendar calendar,
    IClock clock,
    ILogger<RenderFinancialDocumentHandler> logger)
    : IRequestHandler<RenderFinancialDocumentCommand, RenditionOutcome>
{
    public async Task<RenditionOutcome> Handle(RenderFinancialDocumentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (language, format, kind) = (request.Language, RenditionFormat.Pdf, request.Kind);

        var document = await documents.GetByIdAsync(request.DocumentId, cancellationToken);
        if (document is null)
            return RenditionOutcome.NotOwed("not_found");
        if (await renditions.CurrentAsync(document.Id, language, format, kind, cancellationToken) is not null)
            return RenditionOutcome.NotOwed("already_rendered");

        VoidFacts? voided = null;
        if (kind == RenditionKind.Voided)
        {
            var recorded = await documents.VoidOfAsync(document.Id, cancellationToken);
            if (recorded is null)
                return RenditionOutcome.NotOwed("not_voided");
            var next = await documents.FamilyMemberAsync(document.Type, document.SubjectId, document.Version + 1, cancellationToken);
            var replacedBy = next is not null && next.Cause == FinancialDocumentCause.Correction ? next.Number : null;
            voided = new VoidFacts(recorded.VoidedAt, SnapshotJson.Local(recorded.VoidedAt, calendar), replacedBy);
        }

        if (!string.Equals(FinancialDocument.Sha256(document.Snapshot), document.ContentSha256, StringComparison.Ordinal))
        {
            LogSnapshotAltered(logger, document.Number);
            return RenditionOutcome.Undrawable("snapshot_altered");
        }

        PrintedDocument? printed;
        try
        {
            printed = DocumentPrintLayout.TryLayOut(
                document.SnapshotSchemaVersion,
                document.Snapshot,
                document.Number,
                document.IsTest,
                language,
                voided);
        }
#pragma warning disable CA1031 // Whatever the layout throws, it is this snapshot it could not read: the class of failure, not one instance.
        catch (Exception failure) when (failure is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogUnreadableWith(logger, document.Number, document.SnapshotSchemaVersion, failure);
            return RenditionOutcome.Undrawable("snapshot_unreadable");
        }

        if (printed is null)
        {
            LogUnreadable(logger, document.Number, document.SnapshotSchemaVersion);
            return RenditionOutcome.Undrawable("snapshot_unreadable");
        }

        byte[] bytes;
        try
        {
            bytes = renderer.Render(printed);
        }
#pragma warning disable CA1031 // Whatever the library throws, it is this document that could not be drawn.
        catch (Exception failure) when (failure is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogDrawingFailed(logger, document.Number, language.Name, failure);
            return RenditionOutcome.Undrawable("drawing_failed");
        }

        var key = FinancialDocumentRendition.NewStorageKey(document.Id, language, format, kind, DocumentPrintLayout.TemplateVersion);
        try
        {
            await using var content = new MemoryStream(bytes, writable: false);
            var stored = await storage.SaveAtAsync(key, format.ContentType, content, cancellationToken);
            if (stored.SizeBytes != bytes.LongLength)
            {
                // Storage kept something other than what was drawn. Never recorded: the row would vouch for it.
                LogStoredShort(logger, document.Number, bytes.LongLength, stored.SizeBytes);
                await DeleteQuietlyAsync(key);
                return RenditionOutcome.NotStored("stored_size_differs");
            }
        }
#pragma warning disable CA1031 // A store that refuses one write refuses the next: the pass stops and retries on its next tick.
        catch (Exception failure) when (failure is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogStorageFailed(logger, document.Number, failure);
            return RenditionOutcome.NotStored("storage_failed");
        }

        var rendition = FinancialDocumentRendition.Record(
            document,
            language,
            format,
            kind,
            DocumentPrintLayout.TemplateVersion,
            renderer.RendererVersion,
            key,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            bytes.LongLength,
            clock.UtcNow);
        renditions.Add(rendition);
        try
        {
            // Past the point of no return: the bytes are stored, and recording them is one short insert. Not
            // cancelled by a shutdown — an insert that reached the database may have committed however its
            // answer is lost, and then the bytes it points at must still be there.
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (UniqueConstraintConflictException)
        {
            // Another process recorded this PDF first. Its row stands and this row is certainly not written,
            // so this copy is nobody's, and it goes.
            await DeleteQuietlyAsync(key);
            return RenditionOutcome.NotOwed("lost_race");
        }
        catch (Exception failure)
        {
            // Anything else may have committed without saying so. Removing the bytes could leave a recorded
            // PDF that every download fails on, for good; keeping them costs, at worst, a private file nobody
            // points at. So they stay, and the key is logged.
            LogOrphanKept(logger, key, failure.GetType().Name);
            throw;
        }

        LogRendered(logger, document.Number, language.Name, kind.Name, bytes.LongLength);
        return RenditionOutcome.Rendered(rendition);
    }

    /// <summary>
    /// Removes bytes that no row will ever point at. Best effort: an orphan left behind is private, unreachable
    /// without a row, and harmless — logged, never allowed to hide why it was left.
    /// </summary>
    private async Task DeleteQuietlyAsync(string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
#pragma warning disable CA1031 // Cleaning up must not replace the outcome it is cleaning up after.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            LogOrphanLeft(logger, key, failure);
        }
    }

    [LoggerMessage(2620, LogLevel.Information, "Drew the {Language} {Kind} PDF of {Number} ({Bytes} bytes).")]
    private static partial void LogRendered(ILogger logger, string number, string language, string kind, long bytes);

    [LoggerMessage(2621, LogLevel.Error, "The stored snapshot of {Number} no longer matches its hash. It is not drawn: a PDF shows the record as issued or nothing.")]
    private static partial void LogSnapshotAltered(ILogger logger, string number);

    [LoggerMessage(2622, LogLevel.Error, "The snapshot of {Number} (schema {SchemaVersion}) could not be read for print. It is not drawn.")]
    private static partial void LogUnreadable(ILogger logger, string number, int schemaVersion);

    [LoggerMessage(2628, LogLevel.Error, "Laying out the snapshot of {Number} (schema {SchemaVersion}) for print failed.")]
    private static partial void LogUnreadableWith(ILogger logger, string number, int schemaVersion, Exception exception);

    [LoggerMessage(2623, LogLevel.Error, "The {Language} PDF of {Number} could not be drawn. It is tried again when the process next starts.")]
    private static partial void LogDrawingFailed(ILogger logger, string number, string language, Exception exception);

    [LoggerMessage(2624, LogLevel.Error, "The PDF of {Number} could not be stored. Drawing stops until the next settlement pass.")]
    private static partial void LogStorageFailed(ILogger logger, string number, Exception exception);

    [LoggerMessage(2625, LogLevel.Error, "Storage kept {Stored} bytes of the {Drawn}-byte PDF of {Number}. It was removed and not recorded.")]
    private static partial void LogStoredShort(ILogger logger, string number, long drawn, long stored);

    [LoggerMessage(2626, LogLevel.Warning, "Could not remove the unrecorded PDF at {StorageKey}. It is private and nothing points at it.")]
    private static partial void LogOrphanLeft(ILogger logger, string storageKey, Exception exception);

    [LoggerMessage(2627, LogLevel.Warning, "Recording the PDF at {StorageKey} failed ({Failure}); it may have committed, so its bytes are kept. If it did not, they are private and nothing points at them.")]
    private static partial void LogOrphanKept(ILogger logger, string storageKey, string failure);
}
