using Khadra.Application.AdminDashboard;
using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Domain.Common;
using Khadra.Tests.Support;

namespace Khadra.Tests.Application.FinancialDocuments;

/// <summary>
/// Documents owed and not issued are never silent (payments Phase 5): one work-queue row, "N financial
/// documents are on hold", for a human to look at.
/// </summary>
public sealed class FinancialDocumentsOnHoldQueueTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    [Fact]
    public void Documents_on_hold_are_one_row_that_needs_a_look()
    {
        var holds = new FinancialDocumentHoldsSummary(3, [Id.New(), Id.New(), Id.New()], ["KH-AAAA1111", "KH-BBBB2222"], Now.AddHours(-5));

        var queue = AttentionQueueBuilder.Build([], new Dictionary<Id, string>(), [], 0.75m, 48, Now, documentsOnHold: holds);

        var row = Assert.Single(queue.Items);
        Assert.Equal(AttentionQueueBuilder.Kinds.FinancialDocumentsOnHold, row.Kind);
        Assert.Equal(AttentionQueueBuilder.Severities.Warning, row.Severity);
        Assert.Equal(3, row.Count);
        Assert.Equal(3, row.SubjectIds.Count);
        Assert.Equal("KH-AAAA1111 · KH-BBBB2222", row.Subtitle);
        Assert.Equal(Now.AddHours(-5), row.SlaStartedAt);
        // Nobody froze a deadline for it, and none is invented.
        Assert.Null(row.SlaDeadlineAt);
        Assert.False(row.IsOverdue);
    }

    [Fact]
    public void Receipts_whose_email_has_not_gone_are_one_row_too_named_by_their_numbers()
    {
        // Payments Phase 7: a failed email, or one queued too long waiting for its PDF, is sent again only when
        // somebody asks — so somebody must be told.
        var emails = new FinancialDocumentEmailsSummary(2, [Id.New(), Id.New()], ["PAY-2026-000014", "RFD-2026-000003"], Now.AddMinutes(-45));

        var queue = AttentionQueueBuilder.Build([], new Dictionary<Id, string>(), [], 0.75m, 48, Now, emailsNotSent: emails);

        var row = Assert.Single(queue.Items);
        Assert.Equal(AttentionQueueBuilder.Kinds.FinancialDocumentEmailsNotSent, row.Kind);
        Assert.Equal("financial-document-emails-not-sent", row.Id);
        Assert.Equal(AttentionQueueBuilder.Severities.Warning, row.Severity);
        Assert.Equal(2, row.Count);
        Assert.Equal("PAY-2026-000014 · RFD-2026-000003", row.Subtitle);
        Assert.Equal(Now.AddMinutes(-45), row.SlaStartedAt);
        Assert.Null(row.SlaDeadlineAt);
        Assert.Empty(AttentionQueueBuilder.Build([], new Dictionary<Id, string>(), [], 0.75m, 48, Now, emailsNotSent: FinancialDocumentEmailsSummary.None).Items);
    }

    /// <summary>
    /// Pre-launch item 197: a PDF that cannot be drawn is one row, counted by DOCUMENT (a receipt failing in both
    /// languages is one document to look at), named by numbers, opening the document when there is one.
    /// </summary>
    [Fact]
    public void Pdfs_that_cannot_be_drawn_are_one_row_counted_by_document()
    {
        var only = Id.New();
        var pdfs = new FinancialDocumentPdfHoldsSummary(2, [only], ["PAY-2026-000021"], Now.AddHours(-2));

        var queue = AttentionQueueBuilder.Build([], new Dictionary<Id, string>(), [], 0.75m, 48, Now, pdfsNotDrawn: pdfs);

        var row = Assert.Single(queue.Items);
        Assert.Equal(AttentionQueueBuilder.Kinds.FinancialDocumentPdfsNotDrawn, row.Kind);
        Assert.Equal(AttentionQueueBuilder.Severities.Warning, row.Severity);
        Assert.Equal(1, row.Count);
        Assert.Equal([only.Value], row.SubjectIds);
        Assert.Equal("PAY-2026-000021", row.Subtitle);
        Assert.Null(row.SlaDeadlineAt);
        Assert.Empty(AttentionQueueBuilder.Build([], new Dictionary<Id, string>(), [], 0.75m, 48, Now, pdfsNotDrawn: FinancialDocumentPdfHoldsSummary.None).Items);
    }

    [Fact]
    public void Nothing_on_hold_adds_nothing() =>
        Assert.Empty(AttentionQueueBuilder.Build([], new Dictionary<Id, string>(), [], 0.75m, 48, Now, documentsOnHold: FinancialDocumentHoldsSummary.None).Items);
}
