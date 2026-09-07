using Khadra.Application.Auditing.ReadAuditLog;
using Khadra.Application.Auditing.ReadModels;
using Khadra.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khadra.WebAPI.Controllers;

/// <summary>
/// The audit trail (spec 7): every privileged action, who took it, and on what grounds.
///
/// Read-only, and there is no write endpoint here by design. Entries are written by the handler that
/// performs the action, inside that handler's own transaction, so the record and the thing it records
/// commit together or not at all. The table is append-only underneath — a database trigger and a
/// SaveChanges guard both refuse UPDATE and DELETE — and nothing on this controller could change that
/// even if it wanted to.
///
/// The Admin policy is at CLASS level. Program.cs's fallback only requires an authenticated user, so
/// an action here without an explicit policy would let any dealer employee read every decision the
/// platform has ever made about every dealer.
/// </summary>
[Authorize(Policy = SecurityPolicies.Admin)]
[Route("api/v1/admin/audit-logs")]
public sealed class AuditLogController : ApiControllerBase
{
    /// <summary>
    /// One page of the log, newest first.
    ///
    /// The date range is in calendar days as the admin picked them, resolved against the platform's
    /// reporting zone; `to` includes the whole of the day it names.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<AuditLogEntry>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> List(
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] Guid? actorUserId,
        [FromQuery] Guid? entityId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? search,
        [FromQuery] bool? systemOnly,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new ListAuditLogQuery(action, entityType, actorUserId, entityId, from, to, search, systemOnly, page, pageSize),
            cancellationToken);

        return FromResult(result);
    }

    /// <summary>
    /// What the log can be filtered by: the action and entity vocabularies, and everyone who appears.
    ///
    /// Sent rather than left to the console, which would otherwise hold eighteen action names as a
    /// hard-coded list — stale the moment an action is added, and offering a filter set that quietly
    /// excludes real entries on the one screen whose job is completeness.
    /// </summary>
    [HttpGet("vocabulary")]
    [ProducesResponseType<AuditVocabularyDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> Vocabulary(CancellationToken cancellationToken) =>
        FromResult(await Mediator.Send(new GetAuditVocabularyQuery(), cancellationToken));
}
