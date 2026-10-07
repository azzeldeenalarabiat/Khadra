using Khadra.Domain.Common;
using Khadra.Domain.Legal;

namespace Khadra.Application.Legal.ReadModels;

/// <summary>A version in force that a person has not accepted.</summary>
public sealed record PendingLegalVersion(LegalDocumentKind Kind, Id VersionId, string VersionLabel, DateTimeOffset EffectiveFrom);

/// <summary>One acceptance on a person's record, with the version it names.</summary>
public sealed record AcceptedLegalVersion(
    LegalDocumentKind Kind,
    Id VersionId,
    string VersionLabel,
    DateTimeOffset AcceptedAt,
    ConsentChannel Channel,
    Language Language);

/// <summary>
/// A person's consents to the legal texts (Wave 4, W4-8). Never called concurrently: the readers share a DbContext.
/// </summary>
public interface ILegalConsentReader
{
    /// <summary>
    /// The versions in force at <paramref name="at"/> that <paramref name="userId"/> has not accepted, in the kinds'
    /// order. The ONE statement of "pending" (the advisor's review): the consent gate, <c>/auth/me</c> and the person's
    /// own record all ask it, so the prompt and the refusal cannot disagree.
    /// </summary>
    Task<IReadOnlyList<PendingLegalVersion>> PendingAsync(Id userId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Every acceptance on this person's record, newest first: the record producible for the data subject.</summary>
    Task<IReadOnlyList<AcceptedLegalVersion>> AcceptedAsync(Id userId, CancellationToken cancellationToken = default);

    /// <summary>Of <paramref name="versionIds"/>, those this person has already accepted: a version is recorded once.</summary>
    Task<IReadOnlySet<Id>> AlreadyAcceptedAsync(Id userId, IReadOnlyCollection<Id> versionIds, CancellationToken cancellationToken = default);
}
