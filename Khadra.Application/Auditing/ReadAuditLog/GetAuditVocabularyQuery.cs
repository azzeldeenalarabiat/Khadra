using CSharpFunctionalExtensions;
using Khadra.Application.Auditing.ReadModels;
using Khadra.Application.Common;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.Auditing.ReadAuditLog;

/// <summary>What the audit log can be filtered by, as the domain defines it.</summary>
public sealed record AuditVocabularyDto(
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> EntityTypes,
    IReadOnlyList<AuditActorDto> Actors);

/// <summary>
/// Someone who appears in the log, for the "who did it" filter.
///
/// Read from the ENTRIES, not from the users table: the log records people who acted, including ones
/// whose accounts have since been deactivated or soft-deleted, and an auditor filtering by an admin
/// who has left must still find their decisions. The name is the one snapshotted on the entry.
/// </summary>
public sealed record AuditActorDto(Guid? UserId, string Name, int EntryCount);

/// <summary>
/// The filter vocabulary for the audit screen.
///
/// Its own endpoint because the console must not contain this list. Eighteen action names retyped
/// into a TypeScript array is static data by any measure: it goes stale the moment an action is
/// added, and the screen then offers a filter set that quietly excludes real entries — the worst
/// possible failure on the one screen whose job is completeness.
/// </summary>
public sealed record GetAuditVocabularyQuery : IQuery<Result<AuditVocabularyDto, Error>>;

public sealed class GetAuditVocabularyHandler(IAuditActorReader actors)
    : IRequestHandler<GetAuditVocabularyQuery, Result<AuditVocabularyDto, Error>>
{
    public async Task<Result<AuditVocabularyDto, Error>> Handle(
        GetAuditVocabularyQuery request,
        CancellationToken cancellationToken)
    {
        // Ordered by id, which is the order they were declared in the domain and groups related
        // actions together (all the dealer verbs, then the customer ones). Alphabetical would split
        // "DealerApproved" from "DealerRejected" with unrelated verbs in between.
        var actions = Enumeration.GetAll<AuditAction>()
            .OrderBy(action => action.Id)
            .Select(action => action.Name)
            .ToList();

        var entityTypes = Enumeration.GetAll<AuditEntityType>()
            .OrderBy(type => type.Id)
            .Select(type => type.Name)
            .ToList();

        return new AuditVocabularyDto(actions, entityTypes, await actors.ListAsync(cancellationToken));
    }
}
