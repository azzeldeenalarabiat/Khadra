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
    public void Nothing_on_hold_adds_nothing() =>
        Assert.Empty(AttentionQueueBuilder.Build([], new Dictionary<Id, string>(), [], 0.75m, 48, Now, documentsOnHold: FinancialDocumentHoldsSummary.None).Items);
}
