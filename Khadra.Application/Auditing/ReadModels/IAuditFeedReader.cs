using Khadra.Domain.Common;

namespace Khadra.Application.Auditing.ReadModels;

/// <summary>
/// One line of the recent-activity feed.
///
/// Structured, not rendered: the client composes "Rania approved dealer Aqaba Coast Cars" from these
/// parts and maps <paramref name="Action"/> to an icon. Composing the sentence here would put English
/// word order and the console's icon set inside the API.
/// </summary>
/// <param name="EntityId">The record acted on, as the audit log already sends it.</param>
/// <param name="BookingReference">
/// The booking the entry is about: a Booking entry's own, a Dispute entry's disputed one, null for
/// every other kind. Sent as its own fact because disputes used to be labelled with the English
/// sentence "Dispute on KH-…", in a table that can never be rewritten, and the console has to word
/// those old entries in Arabic too without reading the reference back out of an English label.
/// </param>
public sealed record ActivityEntry(
    Id Id,
    DateTimeOffset OccurredAt,
    string ActorName,
    string Action,
    string EntityType,
    string SubjectLabel,
    Id? EntityId,
    string? BookingReference);

public interface IAuditFeedReader
{
    Task<IReadOnlyList<ActivityEntry>> RecentAsync(int count, CancellationToken cancellationToken = default);
}
