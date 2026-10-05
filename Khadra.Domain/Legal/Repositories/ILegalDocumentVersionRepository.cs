using Khadra.Domain.Common;

namespace Khadra.Domain.Legal.Repositories;

public interface ILegalDocumentVersionRepository
{
    /// <summary>The unique index on (kind, effective_from): two versions of a document in force from one instant.</summary>
    public const string KindEffectiveFromIndex = "ux_legal_document_versions_kind_effective_from";

    /// <summary>The unique index on (kind, version_label): two versions of a document under one label.</summary>
    public const string KindLabelIndex = "ux_legal_document_versions_kind_version_label";

    void Add(LegalDocumentVersion version);

    Task<LegalDocumentVersion?> GetByIdAsync(Id id, CancellationToken cancellationToken = default);

    /// <summary>When the newest version of a kind came into force, or null if none was ever published.</summary>
    Task<DateTimeOffset?> LatestEffectiveFromAsync(LegalDocumentKind kind, CancellationToken cancellationToken = default);

    Task<bool> LabelTakenAsync(LegalDocumentKind kind, string versionLabel, CancellationToken cancellationToken = default);

    /// <summary>The id of the version in force at <paramref name="at"/>, or null. One indexed query.</summary>
    Task<Id?> CurrentIdAsync(LegalDocumentKind kind, DateTimeOffset at, CancellationToken cancellationToken = default);
}
