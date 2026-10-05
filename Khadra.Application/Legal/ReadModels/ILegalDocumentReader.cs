using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.Legal;

namespace Khadra.Application.Legal.ReadModels;

/// <summary>One published version as the administrator's list shows it.</summary>
/// <param name="PublishedByName">The publisher's name, read live; null once their account no longer resolves.</param>
public sealed record LegalVersionRow(
    Id VersionId,
    LegalDocumentKind Kind,
    string VersionLabel,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset PublishedAt,
    Id PublishedByAdminId,
    string? PublishedByName);

/// <summary>The version of one kind in force at an instant, without its texts.</summary>
public sealed record CurrentLegalVersion(LegalDocumentKind Kind, Id VersionId, string VersionLabel, DateTimeOffset EffectiveFrom);

/// <summary>The legal texts as the screens read them (Wave 2 G1). Never called concurrently: the readers share a DbContext.</summary>
public interface ILegalDocumentReader
{
    /// <summary>Every published version, newest first; of one kind when <paramref name="kind"/> is given.</summary>
    Task<PagedResult<LegalVersionRow>> ListAsync(LegalDocumentKind? kind, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>The version of each kind in force at <paramref name="at"/>; a kind with none is absent.</summary>
    Task<IReadOnlyList<CurrentLegalVersion>> CurrentAsync(DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same, or null when the database could not be read just now. For <c>/app-config</c>, which answers without
    /// a database (an outdated app reads its update screen from it): null is "not known", never "nothing published".
    /// </summary>
    Task<IReadOnlyList<CurrentLegalVersion>?> CurrentIfReadableAsync(DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>An administrator's name, read live; null once the account no longer resolves.</summary>
    Task<string?> AdminNameAsync(Id adminId, CancellationToken cancellationToken = default);
}
