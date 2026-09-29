using System.Security.Cryptography;
using System.Text;
using Khadra.Application.Common.Ports;
using Khadra.Application.FinancialDocuments.Queries;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Application.FinancialDocuments.Rendering;
using Khadra.Domain.Common;
using Khadra.Domain.FinancialDocuments;
using Khadra.Domain.Payments;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Documents;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Infrastructure.Reporting;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Drawing issued documents as PDFs end to end on SQLite (payments Phase 6): the settlement pass's rendering
/// step exactly as <c>BookingSettlementService</c> runs it, with the real renderer, the real repositories and
/// readers, and storage that keeps the bytes — and the links that hand them out, ownership first.
/// </summary>
public sealed class FinancialDocumentRenditionTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly IssuanceHarness _harness;

    public FinancialDocumentRenditionTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new KhadraDbContext(options);
        context.Database.EnsureCreated();
        _harness = new IssuanceHarness(options);
    }

    public void Dispose() => _connection.Dispose();

    // ── Drawing ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Every_issued_document_gets_a_pdf_in_english_and_in_arabic_drawn_from_its_own_snapshot()
    {
        await IssuedAsync();

        var outcomes = await _harness.RenderPassAsync();

        Assert.Equal(4, outcomes.Count);
        Assert.All(outcomes, outcome => Assert.NotNull(outcome.RenditionId));
        var documents = await _harness.DocumentsAsync();
        var renditions = await _harness.RenditionsAsync();
        Assert.Equal(4, renditions.Count);
        foreach (var document in documents)
        {
            var mine = renditions.Where(rendition => rendition.DocumentId == document.Id).ToList();
            Assert.Equal(["ar", "en"], mine.Select(rendition => rendition.Language.Name).Order(StringComparer.Ordinal));
            foreach (var rendition in mine)
            {
                // What the row vouches for is exactly what storage holds, and which record it was drawn from.
                var bytes = _harness.Storage.Files[rendition.StorageKey];
                Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), rendition.ContentSha256);
                Assert.Equal(bytes.LongLength, rendition.SizeBytes);
                Assert.Equal(document.ContentSha256, rendition.SnapshotSha256);
                Assert.Equal(RenditionFormat.Pdf, rendition.Format);
                Assert.Equal(RenditionKind.AsIssued, rendition.Kind);
                Assert.Equal(DocumentPrintLayout.TemplateVersion, rendition.TemplateVersion);
                Assert.StartsWith("QuestPDF 2026.", rendition.RendererVersion, StringComparison.Ordinal);
                Assert.Equal(_harness.Now, rendition.RenderedAt);
                Assert.StartsWith($"financial-documents/{document.Id.Value:D}/v1-{rendition.Language.Name}-", rendition.StorageKey, StringComparison.Ordinal);
                // A key every storage provider accepts: the one whitelist both of them apply.
                Assert.Equal(rendition.StorageKey, DocumentKeys.Validate(rendition.StorageKey));
            }
        }

        Assert.True(_harness.RenderingLog.Logged(2620));
        Assert.DoesNotContain(_harness.RenderingLog.Entries, entry => entry.Level >= Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task A_pdf_is_drawn_once_however_many_passes_run()
    {
        await IssuedAsync();
        await _harness.RenderPassAsync();
        var stored = _harness.Storage.Files.Count;

        _harness.Now = _harness.Now.AddMinutes(1);
        var again = await _harness.RenderPassAsync();

        Assert.Empty(again);
        Assert.Equal(4, (await _harness.RenditionsAsync()).Count);
        Assert.Equal(stored, _harness.Storage.Files.Count);
    }

    [Fact]
    public async Task A_pair_already_drawn_is_not_drawn_again_when_asked_directly()
    {
        var (receipt, _) = await IssuedAsync();
        await _harness.RenderPassAsync();

        await using var context = _harness.NewContext();
        var outcome = await _harness.RenderHandler(context).Handle(new RenderFinancialDocumentCommand(receipt.Id, Language.Arabic, RenditionKind.AsIssued), CancellationToken.None);

        Assert.Equal("already_rendered", outcome.Skipped);
        Assert.Equal(4, _harness.Storage.Files.Count);
    }

    [Fact]
    public async Task The_work_is_oldest_issue_first_english_before_arabic_and_bounded()
    {
        var (first, _) = await IssuedAsync();
        _harness.Now = _harness.Now.AddMinutes(10);
        await _harness.PaidAsync(PaymentProviders.Sandbox, unique: "20202");
        await _harness.PassAsync();

        await using var context = _harness.NewContext();
        var reader = new FinancialDocumentRenditionWorkReader(context);
        var all = await reader.ListAsync([1], [], 100);
        Assert.Equal(8, all.Count);
        // The first booking's two documents come before the second's; each document's languages in the order a page
        // counts and offers them — the reader's pair and DocumentPrintLayout.Languages never part.
        Assert.Equal(DocumentPrintLayout.Languages, new[] { all[0].Language, all[1].Language });
        Assert.Equal(first.BookingId, (await DocumentAsync(all[0].DocumentId)).BookingId);
        Assert.All(all, candidate => Assert.Equal(RenditionKind.AsIssued, candidate.Kind));
        for (var index = 0; index < all.Count; index += 2)
        {
            Assert.Equal(all[index].DocumentId, all[index + 1].DocumentId);
            Assert.Equal(Language.English, all[index].Language);
            Assert.Equal(Language.Arabic, all[index + 1].Language);
        }

        // At most the limit; and a pair this process could not draw is stepped around, not counted.
        Assert.Equal(all.Take(3), await reader.ListAsync([1], [], 3));
        Assert.Equal(all.Skip(1).Take(3), await reader.ListAsync([1], [all[0]], 3));
        Assert.Equal(all.Skip(4), await reader.ListAsync([1], [.. all.Take(4)], 100));
        Assert.Empty(await reader.ListAsync([1], [], 0));
        Assert.Empty(await reader.ListAsync([], [], 10));
    }

    [Fact]
    public async Task A_document_whose_snapshot_schema_this_build_cannot_read_is_not_even_listed()
    {
        var (receipt, statement) = await IssuedAsync();
        await _harness.ChangeAsync(context =>
            context.Database.ExecuteSqlAsync($"UPDATE financial_documents SET snapshot_schema_version = 2 WHERE document_number = {receipt.Number}"));

        var outcomes = await _harness.RenderPassAsync();

        Assert.Equal(2, outcomes.Count);
        Assert.All(await _harness.RenditionsAsync(), rendition => Assert.Equal(statement.Id, rendition.DocumentId));
    }

    [Fact]
    public async Task A_snapshot_that_no_longer_matches_its_hash_is_never_drawn_and_waits_for_the_next_start()
    {
        var (receipt, statement) = await IssuedAsync();
        // Altered behind the record's back — possible on SQLite, which has no trigger; PostgreSQL refuses it.
        await _harness.ChangeAsync(context => context.Database.ExecuteSqlAsync(
            $"UPDATE financial_documents SET snapshot = snapshot || ' ' WHERE document_number = {receipt.Number}"));

        var outcomes = await _harness.RenderPassAsync();

        Assert.Equal(["snapshot_altered", "snapshot_altered"], outcomes.Where(outcome => outcome.CannotBeDrawn).Select(outcome => outcome.Skipped!));
        Assert.All(await _harness.RenditionsAsync(), rendition => Assert.Equal(statement.Id, rendition.DocumentId));
        Assert.DoesNotContain(_harness.Storage.Files.Keys, key => key.Contains(receipt.Id.Value.ToString("D"), StringComparison.Ordinal));
        Assert.Equal(2, _harness.RenderingLog.Entries.Count(entry => entry.Id.Id == 2621 && entry.Level == Microsoft.Extensions.Logging.LogLevel.Error));

        // Left alone for the rest of this process — not tried, and not logged, every minute.
        var again = await _harness.RenderPassAsync();
        Assert.Empty(again);
        Assert.Equal(2, _harness.RenderingLog.Entries.Count(entry => entry.Id.Id == 2621));

        // A new process tries again, and says so again.
        _harness.Undrawable = [];
        Assert.Equal(2, (await _harness.RenderPassAsync()).Count(outcome => outcome.Skipped == "snapshot_altered"));
    }

    [Fact]
    public async Task A_snapshot_the_layout_cannot_read_is_refused_whole()
    {
        var (receipt, _) = await IssuedAsync();
        const string Broken = """{"schemaVersion":1,"content":{"title":{"en":"Payment receipt"}}}""";
        await _harness.ChangeAsync(context => context.Database.ExecuteSqlAsync(
            $"UPDATE financial_documents SET snapshot = {Broken}, content_sha256 = {FinancialDocument.Sha256(Broken)} WHERE document_number = {receipt.Number}"));

        var outcomes = await _harness.RenderPassAsync();

        Assert.Equal(2, outcomes.Count(outcome => outcome.Skipped == "snapshot_unreadable" && outcome.CannotBeDrawn));
        Assert.True(_harness.RenderingLog.Logged(2622));
        Assert.DoesNotContain(await _harness.RenditionsAsync(), rendition => rendition.DocumentId == receipt.Id);
    }

    [Fact]
    public async Task A_pdf_that_cannot_be_drawn_is_logged_once_and_the_others_still_are()
    {
        var (receipt, _) = await IssuedAsync();
        var real = _harness.Renderer;
        var renderer = Substitute.For<IFinancialDocumentPdfRenderer>();
        renderer.Probe().Returns(real.Probe());
        renderer.RendererVersion.Returns(real.RendererVersion);
        renderer.Render(Arg.Any<PrintedDocument>()).Returns(call =>
        {
            var printed = call.Arg<PrintedDocument>();
            return printed.Language == Language.Arabic && printed.Number == receipt.Number
                ? throw new InvalidOperationException("The layout could not be placed on the page.")
                : real.Render(printed);
        });
        _harness.Renderer = renderer;

        var outcomes = await _harness.RenderPassAsync();

        Assert.Equal(3, outcomes.Count(outcome => outcome.RenditionId is not null));
        Assert.Equal("drawing_failed", Assert.Single(outcomes, outcome => outcome.CannotBeDrawn).Skipped);
        Assert.Equal(new RenditionCandidate(receipt.Id, Language.Arabic, RenditionKind.AsIssued), Assert.Single(_harness.Undrawable));
        Assert.Single(_harness.RenderingLog.Entries, entry => entry.Id.Id == 2623);
        Assert.Empty(await _harness.RenderPassAsync());
        Assert.Single(_harness.RenderingLog.Entries, entry => entry.Id.Id == 2623);
    }

    [Fact]
    public async Task A_host_that_cannot_draw_at_all_is_not_asked_to()
    {
        await IssuedAsync();
        var renderer = Substitute.For<IFinancialDocumentPdfRenderer>();
        renderer.Probe().Returns(new PdfRendererStatus(false, "DllNotFoundException: libSkiaSharp"));
        _harness.Renderer = renderer;

        Assert.Empty(await _harness.RenderPassAsync());
        renderer.DidNotReceive().Render(Arg.Any<PrintedDocument>());
    }

    [Fact]
    public async Task A_full_pass_that_draws_nothing_stops_drawing_until_the_next_start_and_a_smaller_one_never_does()
    {
        await IssuedAsync();
        var renderer = Substitute.For<IFinancialDocumentPdfRenderer>();
        renderer.Probe().Returns(new PdfRendererStatus(true, "ready"));
        renderer.RendererVersion.Returns("QuestPDF 2026.9.1");
        renderer.Render(Arg.Any<PrintedDocument>()).Throws(new InvalidOperationException("A defect in the layout."));
        _harness.Renderer = renderer;

        // Four failures in a pass of up to forty: four documents that cannot be drawn, and drawing carries on.
        Assert.Equal(4, (await _harness.RenderPassAsync()).Count(outcome => outcome.CannotBeDrawn));
        Assert.False(_harness.DrawingStopped);

        // A FULL pass of nothing but failures is a defect in the layout or the renderer: drawing stops.
        _harness.Undrawable = [];
        _harness.MaxRenditionsPerPass = 4;
        Assert.Equal(4, (await _harness.RenderPassAsync()).Count(outcome => outcome.CannotBeDrawn));
        Assert.True(_harness.DrawingStopped);
        Assert.Empty(await _harness.RenderPassAsync());
    }

    [Fact]
    public async Task Storage_that_refuses_stops_the_step_and_the_next_pass_draws_everything()
    {
        await IssuedAsync();
        _harness.Storage.Refuse = true;

        var refused = await _harness.RenderPassAsync();

        // One attempt, not four: a store that refused one PDF would refuse the next.
        Assert.True(Assert.Single(refused).StorageFailed);
        Assert.Empty(await _harness.RenditionsAsync());
        Assert.Empty(_harness.Undrawable);
        Assert.True(_harness.RenderingLog.Logged(2624));

        _harness.Storage.Refuse = false;
        Assert.Equal(4, (await _harness.RenderPassAsync()).Count(outcome => outcome.RenditionId is not null));
    }

    [Fact]
    public async Task Bytes_storage_did_not_keep_whole_are_removed_and_never_recorded()
    {
        await IssuedAsync();
        _harness.Storage.Truncate = 10;

        var outcome = Assert.Single(await _harness.RenderPassAsync());

        Assert.Equal("stored_size_differs", outcome.Skipped);
        Assert.True(outcome.StorageFailed);
        Assert.Empty(_harness.Storage.Files);
        Assert.Single(_harness.Storage.Deleted);
        Assert.Empty(await _harness.RenditionsAsync());
    }

    [Fact]
    public async Task A_rendition_is_append_only_in_the_model()
    {
        await IssuedAsync();
        await _harness.RenderPassAsync();

        await using var context = _harness.NewContext();
        var rendition = await context.FinancialDocumentRenditions.FirstAsync();
        context.FinancialDocumentRenditions.Remove(rendition);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("append-only", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_rendition_per_document_language_format_and_template_and_a_new_template_draws_beside_the_old()
    {
        var (receipt, _) = await IssuedAsync();
        await _harness.RenderPassAsync();
        var document = (await _harness.DocumentsAsync()).Single(candidate => candidate.Id == receipt.Id);

        await using (var duplicate = _harness.NewContext())
        {
            duplicate.FinancialDocumentRenditions.Add(Rendition(document, Language.English, RenditionKind.AsIssued, templateVersion: 1));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }

        await using (var newer = _harness.NewContext())
        {
            newer.FinancialDocumentRenditions.Add(Rendition(document, Language.English, RenditionKind.AsIssued, templateVersion: 2));
            await newer.SaveChangesAsync();
        }

        await using var reading = _harness.NewContext();
        var current = await new FinancialDocumentRenditionRepository(reading).CurrentAsync(receipt.Id, Language.English, RenditionFormat.Pdf, RenditionKind.AsIssued);
        Assert.Equal(2, current!.TemplateVersion);
        Assert.Equal(3, (await new FinancialDocumentRenditionRepository(reading).ListForDocumentAsync(receipt.Id)).Count);
    }

    // ── What the customer is offered, and the links ────────────────────────────────────────────────

    [Fact]
    public async Task The_page_offers_each_drawn_language_and_says_while_a_pdf_is_being_prepared()
    {
        var (receipt, _) = await IssuedAsync();

        var before = await PageAsync(receipt.Id);
        Assert.Empty(before.Pdf.Languages);
        Assert.True(before.Pdf.Preparing);

        await _harness.RenderPassAsync();

        var after = await PageAsync(receipt.Id);
        Assert.Equal(["en", "ar"], after.Pdf.Languages);
        Assert.False(after.Pdf.Preparing);
    }

    [Fact]
    public async Task A_link_is_minted_for_the_customers_own_pdf_and_opens_exactly_the_stored_bytes()
    {
        var (receipt, _) = await IssuedAsync();
        await _harness.RenderPassAsync();
        var signer = Signer();

        var link = await MineAsync(receipt.CustomerId, receipt.Id, "ar", signer);

        Assert.True(link.IsSuccess);
        Assert.Equal(_harness.Now.AddMinutes(5), link.Value.ExpiresAt);
        var token = link.Value.Url.Split('/')[^1].Split('?')[0];
        Assert.True(signer.TryDecodeToken(token, out var key));
        var rendition = (await _harness.RenditionsAsync()).Single(candidate => candidate.DocumentId == receipt.Id && candidate.Language == Language.Arabic);
        Assert.Equal(rendition.StorageKey, key);
        Assert.Equal(rendition.ContentSha256, Convert.ToHexStringLower(SHA256.HashData(_harness.Storage.Files[key])));
    }

    [Fact]
    public async Task A_stranger_is_told_what_a_missing_document_would_tell_them_ready_or_not()
    {
        var (receipt, _) = await IssuedAsync();
        var stranger = Id.New();

        // Not drawn yet: the owner hears "being prepared"; the stranger hears nothing of the sort.
        Assert.Equal(FinancialDocumentErrors.PdfNotReady, (await MineAsync(receipt.CustomerId, receipt.Id, "en", Signer())).Error);
        var strangerBefore = await MineAsync(stranger, receipt.Id, "en", Signer());
        var missing = await MineAsync(stranger, Id.New(), "en", Signer());
        Assert.Equal(FinancialDocumentErrors.NotFound, strangerBefore.Error);
        Assert.Equal(missing.Error, strangerBefore.Error);

        await _harness.RenderPassAsync();
        Assert.Equal(missing.Error, (await MineAsync(stranger, receipt.Id, "en", Signer())).Error);
    }

    [Fact]
    public async Task A_voided_documents_customer_is_given_its_voided_copy_and_the_original_stays_the_administrators()
    {
        // Owner, 2026-09-29: the document stays in the customer's history, and its PDF becomes a copy stamped VOID that
        // names its correction — drawn beside the original, which is never touched and never handed to them again.
        var (receipt, _) = await IssuedAsync();
        await _harness.RenderPassAsync();
        var originals = (await _harness.RenditionsAsync())
            .Where(rendition => rendition.DocumentId == receipt.Id)
            .ToDictionary(rendition => rendition.Language.Name, rendition => (rendition.StorageKey, rendition.ContentSha256));
        _harness.Now = _harness.Now.AddMinutes(5);
        var voided = await _harness.VoidAsync(receipt.Id, "Issued from the wrong capture.");
        Assert.True(voided.IsSuccess);

        // Until the copy is drawn the page says it is being prepared — and the original is not handed out meanwhile.
        var waiting = await PageAsync(receipt.Id);
        Assert.Empty(waiting.Pdf.Languages);
        Assert.True(waiting.Pdf.Preparing);
        Assert.Equal(FinancialDocumentErrors.PdfNotReady, (await MineAsync(receipt.CustomerId, receipt.Id, "en", Signer())).Error);

        // The next pass draws the two voided copies and the correction's two PDFs, and nothing else.
        var outcomes = await _harness.RenderPassAsync();
        Assert.Equal(4, outcomes.Count(outcome => outcome.RenditionId is not null));
        var renditions = await _harness.RenditionsAsync();
        var copies = renditions.Where(rendition => rendition.DocumentId == receipt.Id && rendition.Kind == RenditionKind.Voided).ToList();
        Assert.Equal(["ar", "en"], copies.Select(copy => copy.Language.Name).Order(StringComparer.Ordinal));
        foreach (var copy in copies)
        {
            Assert.Contains($"/v1-{copy.Language.Name}-void-", copy.StorageKey, StringComparison.Ordinal);
            Assert.Equal(receipt.ContentSha256, copy.SnapshotSha256);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(_harness.Storage.Files[copy.StorageKey])), copy.ContentSha256);
        }

        // The original bytes are exactly what they were: same rows, same keys, same hashes, still stored.
        var asIssued = renditions.Where(rendition => rendition.DocumentId == receipt.Id && rendition.Kind == RenditionKind.AsIssued).ToList();
        Assert.Equal(2, asIssued.Count);
        foreach (var original in asIssued)
        {
            Assert.Equal(originals[original.Language.Name], (original.StorageKey, original.ContentSha256));
            Assert.Equal(original.ContentSha256, Convert.ToHexStringLower(SHA256.HashData(_harness.Storage.Files[original.StorageKey])));
        }

        // The customer's page offers the voided copies; their link opens the copy, never the original.
        var page = await PageAsync(receipt.Id);
        Assert.Equal(["en", "ar"], page.Pdf.Languages);
        Assert.False(page.Pdf.Preparing);
        foreach (var language in new[] { "en", "ar" })
        {
            var key = KeyOf((await MineAsync(receipt.CustomerId, receipt.Id, language, Signer())).Value);
            Assert.Equal(copies.Single(copy => copy.Language.Name == language).StorageKey, key);
        }

        // The administrator reads the original by default and the customer's copy on asking for it.
        Assert.Equal(originals["en"].StorageKey, KeyOf((await AdminAsync(receipt.Id, "en")).Value));
        Assert.Equal(copies.Single(copy => copy.Language == Language.Arabic).StorageKey, KeyOf((await AdminAsync(receipt.Id, "ar", "Voided")).Value));

        // A stranger learns nothing of the void or the copy.
        Assert.Equal(FinancialDocumentErrors.NotFound, (await MineAsync(Id.New(), receipt.Id, "en", Signer())).Error);

        // Its correction is offered as issued.
        var correction = (await PageAsync(voided.Value.ReplacementDocumentId)).Pdf;
        Assert.Equal(["en", "ar"], correction.Languages);
        Assert.Empty(await _harness.RenderPassAsync());
    }

    [Fact]
    public async Task A_document_voided_before_it_was_drawn_gets_both_kinds_in_one_pass_as_issued_first()
    {
        var (receipt, statement) = await IssuedAsync();
        _harness.Now = _harness.Now.AddMinutes(1);
        var voided = await _harness.VoidAsync(receipt.Id, "Voided before any PDF was drawn.");
        Assert.True(voided.IsSuccess);

        await using (var context = _harness.NewContext())
        {
            var work = await new FinancialDocumentRenditionWorkReader(context).ListAsync([1], [], 100);
            // Oldest issue first; within a document, the record as issued before its voided copy, English before Arabic.
            Assert.Equal(
                [
                    new RenditionCandidate(receipt.Id, Language.English, RenditionKind.AsIssued),
                    new RenditionCandidate(receipt.Id, Language.Arabic, RenditionKind.AsIssued),
                    new RenditionCandidate(receipt.Id, Language.English, RenditionKind.Voided),
                    new RenditionCandidate(receipt.Id, Language.Arabic, RenditionKind.Voided),
                ],
                work.Where(candidate => candidate.DocumentId == receipt.Id));
            Assert.Equal(8, work.Count);
            Assert.Equal(2, work.Count(candidate => candidate.DocumentId == statement.Id));
            Assert.Equal(2, work.Count(candidate => candidate.DocumentId.Value == voided.Value.ReplacementDocumentId));

            // A kind this process could not draw is stepped around on its own: the other kind is still owed.
            var skipped = await new FinancialDocumentRenditionWorkReader(context)
                .ListAsync([1], [new RenditionCandidate(receipt.Id, Language.English, RenditionKind.AsIssued)], 100);
            Assert.Contains(new RenditionCandidate(receipt.Id, Language.English, RenditionKind.Voided), skipped);
            Assert.DoesNotContain(new RenditionCandidate(receipt.Id, Language.English, RenditionKind.AsIssued), skipped);
        }

        var outcomes = await _harness.RenderPassAsync();

        Assert.Equal(8, outcomes.Count(outcome => outcome.RenditionId is not null));
        Assert.Equal(4, (await _harness.RenditionsAsync()).Count(rendition => rendition.DocumentId == receipt.Id));
        Assert.Empty(await _harness.RenderPassAsync());
    }

    [Fact]
    public async Task One_voided_copy_per_document_language_and_template_beside_the_original()
    {
        var (receipt, _) = await IssuedAsync();
        await _harness.VoidAsync(receipt.Id, "Duplicate capture.");
        await _harness.RenderPassAsync();
        var document = (await _harness.DocumentsAsync()).Single(candidate => candidate.Id == receipt.Id);

        await using (var duplicate = _harness.NewContext())
        {
            duplicate.FinancialDocumentRenditions.Add(Rendition(document, Language.Arabic, RenditionKind.Voided, templateVersion: 1));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        }

        // And asked for directly, it is already drawn.
        await using var context = _harness.NewContext();
        var outcome = await _harness.RenderHandler(context).Handle(
            new RenderFinancialDocumentCommand(receipt.Id, Language.Arabic, RenditionKind.Voided), CancellationToken.None);
        Assert.Equal("already_rendered", outcome.Skipped);
    }

    [Fact]
    public async Task A_document_that_is_not_voided_has_no_voided_copy_for_anyone()
    {
        var (receipt, _) = await IssuedAsync();
        await _harness.RenderPassAsync();

        Assert.Equal(FinancialDocumentErrors.NotVoided, (await AdminAsync(receipt.Id, "en", "Voided")).Error);
        Assert.Equal(FinancialDocumentErrors.NotFound, (await AdminAsync(Id.New(), "en", "Voided")).Error);
        Assert.Equal(KeyOf((await AdminAsync(receipt.Id, "en")).Value), KeyOf((await AdminAsync(receipt.Id, "en", "AsIssued")).Value));

        // Nor is one drawn when asked for directly.
        await using var context = _harness.NewContext();
        var outcome = await _harness.RenderHandler(context).Handle(
            new RenderFinancialDocumentCommand(receipt.Id, Language.English, RenditionKind.Voided), CancellationToken.None);
        Assert.Equal("not_voided", outcome.Skipped);
        Assert.DoesNotContain(await _harness.RenditionsAsync(), rendition => rendition.Kind == RenditionKind.Voided);
    }

    [Fact]
    public async Task The_administrator_is_told_a_pdf_is_being_prepared_and_a_missing_document_is_missing()
    {
        var (receipt, _) = await IssuedAsync();

        Assert.Equal(FinancialDocumentErrors.PdfNotReady, (await AdminAsync(receipt.Id, "ar")).Error);
        Assert.Equal(FinancialDocumentErrors.NotFound, (await AdminAsync(Id.New(), "ar")).Error);
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("ar", true)]
    [InlineData("AR", true)]
    [InlineData(" en ", true)]
    [InlineData("fr", false)]
    [InlineData("ar-JO", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_pdf_is_asked_for_in_en_or_ar(string? language, bool accepted)
    {
        Assert.Equal(accepted, new GetMyFinancialDocumentPdfLinkQueryValidator().Validate(new GetMyFinancialDocumentPdfLinkQuery(Id.New(), Id.New(), language)).IsValid);
        Assert.Equal(accepted, new GetAdminFinancialDocumentPdfLinkQueryValidator().Validate(new GetAdminFinancialDocumentPdfLinkQuery(Id.New(), language)).IsValid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("AsIssued", true)]
    [InlineData("Voided", true)]
    [InlineData(" voided ", true)]
    [InlineData("Original", false)]
    [InlineData("void", false)]
    public void The_administrator_asks_for_the_document_as_issued_or_its_voided_copy(string? kind, bool accepted) =>
        Assert.Equal(accepted, new GetAdminFinancialDocumentPdfLinkQueryValidator().Validate(new GetAdminFinancialDocumentPdfLinkQuery(Id.New(), "en", kind)).IsValid);

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A sandbox payment's receipt and its booking's statement, issued.</summary>
    private async Task<(FinancialDocument Receipt, FinancialDocument Statement)> IssuedAsync()
    {
        await _harness.PaidAsync(PaymentProviders.Sandbox);
        await _harness.PassAsync();
        var documents = await _harness.DocumentsAsync();
        return (
            documents.Single(document => document.Type == FinancialDocumentType.PaymentReceipt),
            documents.Single(document => document.Type == FinancialDocumentType.BookingStatement));
    }

    private async Task<FinancialDocument> DocumentAsync(Id id) => (await _harness.DocumentsAsync()).Single(document => document.Id == id);

    private async Task<FinancialDocumentDto> PageAsync(Id documentId)
    {
        await using var context = _harness.NewContext();
        var reader = new FinancialDocumentReader(context);
        var record = await reader.GetAsync(documentId);
        return (await FinancialDocumentPageReader.ReadAsync(reader, record!, CancellationToken.None)).Page;
    }

    private async Task<CSharpFunctionalExtensions.Result<SignedDocumentLink, Error>> MineAsync(Id customerId, Id documentId, string language, IDocumentLinkSigner signer)
    {
        await using var context = _harness.NewContext();
        return await Links(context, signer).Handle(new GetMyFinancialDocumentPdfLinkQuery(customerId, documentId, language), CancellationToken.None);
    }

    private async Task<CSharpFunctionalExtensions.Result<SignedDocumentLink, Error>> AdminAsync(Id documentId, string language, string? kind = null)
    {
        await using var context = _harness.NewContext();
        return await Links(context, Signer()).Handle(new GetAdminFinancialDocumentPdfLinkQuery(documentId, language, kind), CancellationToken.None);
    }

    /// <summary>The storage key a minted link opens.</summary>
    private static string KeyOf(SignedDocumentLink link)
    {
        var token = link.Url.Split('/')[^1].Split('?')[0];
        Assert.True(Signer().TryDecodeToken(token, out var key));
        return key;
    }

    private FinancialDocumentPdfLinkHandlers Links(KhadraDbContext context, IDocumentLinkSigner signer) =>
        new(new FinancialDocumentRepository(context), new FinancialDocumentRenditionRepository(context), signer, new TestClock(_harness.Now));

    private static HmacDocumentLinkSigner Signer() =>
        new(Options.Create(new JwtOptions { SigningKey = new string('k', 48) }), FakeDocumentPolicy.Default);

    private FinancialDocumentRendition Rendition(FinancialDocument document, Language language, RenditionKind kind, int templateVersion) =>
        FinancialDocumentRendition.Record(
            document,
            language,
            RenditionFormat.Pdf,
            kind,
            templateVersion,
            "QuestPDF 2026.9.1",
            FinancialDocumentRendition.NewStorageKey(document.Id, language, RenditionFormat.Pdf, kind, templateVersion),
            new string('a', 64),
            1234,
            _harness.Now);
}
