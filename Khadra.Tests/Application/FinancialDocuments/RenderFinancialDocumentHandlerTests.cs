using Khadra.Application.FinancialDocuments.Rendering;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.FinancialDocuments.Repositories;
using Khadra.Tests.Support;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Khadra.Tests.Application.FinancialDocuments;

/// <summary>
/// The ways a drawn PDF can fail to be recorded (payments Phase 6), which SQLite cannot stage. Only a lost
/// race says for CERTAIN that this handler's row was not written — another process's stands — so only then
/// are its bytes removed. Any other failure may have committed without saying so (a shutdown, a dropped
/// connection), and removing the bytes could leave a recorded PDF that every download fails on for good; a
/// private file nobody points at costs nothing by comparison. So they are kept, and the failure is thrown.
/// </summary>
public sealed class RenderFinancialDocumentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);

    private readonly IFinancialDocumentRepository _documents = Substitute.For<IFinancialDocumentRepository>();
    private readonly IFinancialDocumentRenditionRepository _renditions = Substitute.For<IFinancialDocumentRenditionRepository>();
    private readonly IFinancialDocumentPdfRenderer _renderer = Substitute.For<IFinancialDocumentPdfRenderer>();
    private readonly MemoryDocumentStorage _storage = new();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RecordingLogger<RenderFinancialDocumentHandler> _log = new();
    private readonly FinancialDocument _document = Composed();

    public RenderFinancialDocumentHandlerTests()
    {
        _documents.GetByIdAsync(_document.Id, Arg.Any<CancellationToken>()).Returns(_document);
        _renderer.RendererVersion.Returns("QuestPDF 2026.9.1");
        _renderer.Render(Arg.Any<PrintedDocument>()).Returns([0x25, 0x50, 0x44, 0x46, 0x2D, 0x31]);
    }

    [Fact]
    public async Task Losing_the_race_to_another_process_removes_this_copy_and_says_so()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new UniqueConstraintConflictException("taken"));

        var outcome = await Handler().Handle(new RenderFinancialDocumentCommand(_document.Id, Language.English, RenditionKind.AsIssued), CancellationToken.None);

        Assert.Equal("lost_race", outcome.Skipped);
        Assert.False(outcome.CannotBeDrawn);
        Assert.False(outcome.StorageFailed);
        Assert.Empty(_storage.Files);
        Assert.Single(_storage.Deleted);
    }

    [Fact]
    public async Task A_record_that_may_have_committed_keeps_its_bytes_and_the_failure_is_thrown()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("The connection dropped."));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler().Handle(new RenderFinancialDocumentCommand(_document.Id, Language.Arabic, RenditionKind.AsIssued), CancellationToken.None));

        Assert.Single(_storage.Files);
        Assert.Empty(_storage.Deleted);
        Assert.Single(_log.Entries, entry => entry.Id.Id == 2627 && entry.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task A_shutdown_never_cancels_the_record_of_bytes_already_stored_nor_removes_them()
    {
        using var shutdown = new CancellationTokenSource();
        CancellationToken? saveToken = null;
        // The answer is lost as the process stops; the insert itself may well have committed.
        _unitOfWork.SaveChangesAsync(Arg.Do<CancellationToken>(token => saveToken = token))
            .ThrowsAsync(new OperationCanceledException(shutdown.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Handler().Handle(new RenderFinancialDocumentCommand(_document.Id, Language.English, RenditionKind.AsIssued), shutdown.Token));

        // The record is written under no token of the pass's: a few milliseconds past the point of no return.
        Assert.Equal(CancellationToken.None, saveToken);
        Assert.Single(_storage.Files);
        Assert.Empty(_storage.Deleted);
    }

    [Fact]
    public async Task A_snapshot_the_layout_cannot_read_is_refused_whole_before_anything_is_drawn()
    {
        var broken = Composed("{\"schemaVersion\":1,\"content\":{\"headline\":{\"money\":{\"amount\":\"12.500\",\"currency\":\"JOD\"}}}}");
        _documents.GetByIdAsync(broken.Id, Arg.Any<CancellationToken>()).Returns(broken);

        var outcome = await Handler().Handle(new RenderFinancialDocumentCommand(broken.Id, Language.English, RenditionKind.AsIssued), CancellationToken.None);

        Assert.True(outcome.CannotBeDrawn);
        Assert.Equal("snapshot_unreadable", outcome.Skipped);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task What_is_recorded_is_the_hash_and_size_of_exactly_the_bytes_drawn()
    {
        FinancialDocumentRendition? recorded = null;
        _renditions.When(repository => repository.Add(Arg.Any<FinancialDocumentRendition>())).Do(call => recorded = call.Arg<FinancialDocumentRendition>());

        var outcome = await Handler().Handle(new RenderFinancialDocumentCommand(_document.Id, Language.Arabic, RenditionKind.AsIssued), CancellationToken.None);

        Assert.Equal(recorded!.Id, outcome.RenditionId);
        Assert.Equal(6, recorded.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData([0x25, 0x50, 0x44, 0x46, 0x2D, 0x31])), recorded.ContentSha256);
        Assert.Equal("QuestPDF 2026.9.1", recorded.RendererVersion);
        Assert.Equal(Now, recorded.RenderedAt);
        Assert.Equal(recorded.StorageKey, Assert.Single(_storage.Files.Keys));
        _renderer.Received(1).Render(Arg.Is<PrintedDocument>(printed => printed.Language == Language.Arabic && printed.Watermark == null));
    }

    [Fact]
    public async Task A_document_that_is_not_there_is_not_owed_a_pdf()
    {
        var outcome = await Handler().Handle(new RenderFinancialDocumentCommand(Id.New(), Language.English, RenditionKind.AsIssued), CancellationToken.None);

        Assert.Equal("not_found", outcome.Skipped);
        _renderer.DidNotReceive().Render(Arg.Any<PrintedDocument>());
    }

    [Fact]
    public async Task A_voided_copy_of_a_document_that_is_not_voided_is_not_owed_and_nothing_is_drawn()
    {
        var outcome = await Handler().Handle(new RenderFinancialDocumentCommand(_document.Id, Language.English, RenditionKind.Voided), CancellationToken.None);

        Assert.Equal("not_voided", outcome.Skipped);
        Assert.False(outcome.CannotBeDrawn);
        _renderer.DidNotReceive().Render(Arg.Any<PrintedDocument>());
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task A_voided_copy_is_stamped_names_its_correction_and_never_the_reason_and_is_recorded_as_its_own_kind()
    {
        // Owner, 2026-09-29: the customer's copy of a voided document is drawn beside the original, never over it.
        const string reason = "Wrong amount keyed by the administrator";
        var voidedAt = Now.AddHours(3);
        var correction = CorrectionOf(_document, "PAY-2026-000002");
        _documents.VoidOfAsync(_document.Id, Arg.Any<CancellationToken>())
            .Returns(FinancialDocumentVoid.Record(_document.Id, Id.New(), reason, voidedAt).Value);
        _documents.FamilyMemberAsync(_document.Type, _document.SubjectId, 2, Arg.Any<CancellationToken>()).Returns(correction);
        FinancialDocumentRendition? recorded = null;
        _renditions.When(repository => repository.Add(Arg.Any<FinancialDocumentRendition>())).Do(call => recorded = call.Arg<FinancialDocumentRendition>());

        var outcome = await Handler().Handle(new RenderFinancialDocumentCommand(_document.Id, Language.English, RenditionKind.Voided), CancellationToken.None);

        Assert.Equal(recorded!.Id, outcome.RenditionId);
        Assert.Equal(RenditionKind.Voided, recorded.Kind);
        Assert.Contains("/v1-en-void-", recorded.StorageKey, StringComparison.Ordinal);
        // Drawn from the document's own snapshot: the copy represents the record as issued, marked void.
        Assert.Equal(_document.ContentSha256, recorded.SnapshotSha256);
        _renderer.Received(1).Render(Arg.Is<PrintedDocument>(printed =>
            printed.Void != null
            && printed.Void.Stamp == "VOID"
            && printed.Void.Replacement!.Contains("PAY-2026-000002", StringComparison.Ordinal)
            && !(printed.Void.Notice + printed.Void.Replacement).Contains(reason, StringComparison.Ordinal)
            && printed.Metadata.ModifiedAt == voidedAt
            && printed.Metadata.Title.Contains("VOID", StringComparison.Ordinal)));
        Assert.Contains(_log.Entries, entry => entry.Id.Id == 2620 && entry.Message.Contains("Voided", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_voided_copy_names_no_replacement_when_the_next_version_is_not_a_correction()
    {
        _documents.VoidOfAsync(_document.Id, Arg.Any<CancellationToken>())
            .Returns(FinancialDocumentVoid.Record(_document.Id, Id.New(), "Duplicate", Now).Value);

        var outcome = await Handler().Handle(new RenderFinancialDocumentCommand(_document.Id, Language.Arabic, RenditionKind.Voided), CancellationToken.None);

        Assert.NotNull(outcome.RenditionId);
        _renderer.Received(1).Render(Arg.Is<PrintedDocument>(printed =>
            printed.Void != null
            && printed.Void.Stamp == "ملغى"
            && printed.Void.Replacement == null));
    }

    [Fact]
    public async Task The_document_as_issued_carries_no_void_marks_even_once_it_is_voided()
    {
        _documents.VoidOfAsync(_document.Id, Arg.Any<CancellationToken>())
            .Returns(FinancialDocumentVoid.Record(_document.Id, Id.New(), "Duplicate", Now).Value);

        await Handler().Handle(new RenderFinancialDocumentCommand(_document.Id, Language.English, RenditionKind.AsIssued), CancellationToken.None);

        // The original stays exactly the record as issued; it never learns about its void.
        _renderer.Received(1).Render(Arg.Is<PrintedDocument>(printed => printed.Void == null && printed.Metadata.ModifiedAt == null));
        await _documents.DidNotReceive().VoidOfAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>());
    }

    private RenderFinancialDocumentHandler Handler() =>
        new(_documents, _renditions, _renderer, _storage, _unitOfWork, DocumentFixtures.Amman, new TestClock(Now), _log);

    /// <summary>A receipt for real money (no watermark), composed by the real composer as issuing stores it.</summary>
    private static FinancialDocument Composed()
    {
        var (booking, payment) = Build.PaidBooking(now: Now);
        var stamp = Khadra.Application.FinancialDocuments.Composition.DocumentStamp.First("PAY-2026-000001", Now);
        var draft = DocumentFixtures.Composer().PaymentReceipt(
            new Khadra.Application.FinancialDocuments.Composition.PaymentReceiptFacts(
                DocumentFixtures.Issuer, DocumentFixtures.PartiesOf(booking), booking, payment, [payment]),
            stamp);
        return FinancialDocument.Issue(draft, stamp.Number, Now);
    }

    /// <summary>The correction issued with a void: the voided receipt's next version, under its own number.</summary>
    private static FinancialDocument CorrectionOf(FinancialDocument voided, string number)
    {
        var draft = new FinancialDocumentDraft(
            voided.Type, voided.SubjectId, Version: voided.Version + 1, PreviousVersionId: voided.Id, RelatedDocumentId: null,
            BookingId: voided.BookingId, BookingReference: voided.BookingReference, CustomerId: voided.CustomerId, DealerId: voided.DealerId,
            PaymentId: voided.PaymentId, RefundId: null, FinancialDocumentCause.Correction, OccurredAt: Now, CoversThrough: null,
            CheckpointFingerprint: null, HeadlineAmount: voided.HeadlineAmount, Provider: voided.Provider, CalculatorVersion: 1,
            SnapshotSchemaVersion: voided.SnapshotSchemaVersion, Snapshot: voided.Snapshot);
        return FinancialDocument.Issue(draft, number, Now);
    }

    /// <summary>A receipt whose stored snapshot is exactly <paramref name="snapshot"/>, hashed as issuing hashes it.</summary>
    private static FinancialDocument Composed(string snapshot)
    {
        var paymentId = Id.New();
        var draft = new FinancialDocumentDraft(
            FinancialDocumentType.PaymentReceipt, paymentId, Version: 1, PreviousVersionId: null, RelatedDocumentId: null,
            BookingId: Id.New(), BookingReference: "KH-BROKEN01", CustomerId: Id.New(), DealerId: Id.New(), PaymentId: paymentId,
            RefundId: null, FinancialDocumentCause.PaymentCaptured, OccurredAt: Now, CoversThrough: null, CheckpointFingerprint: null,
            HeadlineAmount: Money.Jod(12.5m), Provider: "TestProvider", CalculatorVersion: 1, SnapshotSchemaVersion: 1, Snapshot: snapshot);
        return FinancialDocument.Issue(draft, "PAY-2026-000009", Now);
    }
}
