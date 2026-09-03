using Khadra.Domain.Common;

namespace Khadra.Application.Auditing.ReadModels;

/// <summary>
/// One line of the recent-activity feed.
///
/// Structured, not rendered: the client composes "Rania approved dealer Aqaba Coast Cars" from these
/// parts and maps <paramref name="Action"/> to an icon. Composing the sentence here would put English
/// word order and the console's icon set inside the API.
/// </summary>
public sealed record ActivityEntry(
    Id Id,
    DateTimeOffset OccurredAt,
    string ActorName,
    string Action,
    string EntityType,
    string SubjectLabel);

public interface IAuditFeedReader
{
    Task<IReadOnlyList<ActivityEntry>> RecentAsync(int count, CancellationToken cancellationToken = default);
}
