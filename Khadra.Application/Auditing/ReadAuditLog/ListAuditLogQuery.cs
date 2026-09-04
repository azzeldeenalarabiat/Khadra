using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Auditing.ReadModels;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using MediatR;

namespace Khadra.Application.Auditing.ReadAuditLog;

/// <summary>
/// The audit log (spec 7): every privileged action, who took it, and on what grounds.
///
/// A read model rather than the repository. The entries are already immutable and already written in
/// the same transaction as the action they describe; nothing here may change one, and there is
/// deliberately no command in this folder at all.
/// </summary>
public sealed record ListAuditLogQuery(
    string? Action,
    string? EntityType,
    Guid? ActorUserId,
    Guid? EntityId,
    DateOnly? From,
    DateOnly? To,
    string? Search,
    int? Page,
    int? PageSize) : IQuery<Result<PagedResult<AuditLogEntry>, Error>>;

public sealed class ListAuditLogQueryValidator : AbstractValidator<ListAuditLogQuery>
{
    public ListAuditLogQueryValidator()
    {
        // An unknown action or entity type is rejected rather than quietly ignored. On this screen a
        // filter that silently does nothing lets an auditor believe they have seen everything of a
        // kind when they have seen the unfiltered log.
        RuleFor(query => query.Action)
            .Must(name => name is null || Enumeration.GetAll<AuditAction>()
                .Any(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Unknown audit action.");

        RuleFor(query => query.EntityType)
            .Must(name => name is null || Enumeration.GetAll<AuditEntityType>()
                .Any(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Unknown audit entity type.");

        RuleFor(query => query.Search).MaximumLength(200);

        RuleFor(query => query)
            .Must(query => query.From is null || query.To is null || query.From <= query.To)
            .WithMessage("The start of the range must not be after its end.");
    }
}

public sealed class ListAuditLogHandler(IAuditLogReader reader, IReportingCalendar calendar)
    : IRequestHandler<ListAuditLogQuery, Result<PagedResult<AuditLogEntry>, Error>>
{
    public async Task<Result<PagedResult<AuditLogEntry>, Error>> Handle(
        ListAuditLogQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = PageRequest.From(request.Page, request.PageSize);

        // The dates arrive as calendar days an admin picked, and they mean days in the platform's
        // reporting zone (Asia/Amman), not UTC. "To 3 September" means the whole of that day, so the
        // window runs to the START of the next one -- a naive `<= 2026-09-03` would silently exclude
        // everything that happened after midnight on the day the admin asked about.
        var from = request.From is { } fromDay ? calendar.StartOfDay(fromDay) : (DateTimeOffset?)null;
        var to = request.To is { } toDay ? calendar.StartOfDay(toDay.AddDays(1)) : (DateTimeOffset?)null;

        var filter = new AuditLogFilter(
            request.Action,
            request.EntityType,
            request.ActorUserId is { } actor ? Id.From(actor) : null,
            from,
            to,
            request.Search,
            request.EntityId is { } entity ? Id.From(entity) : null);

        return await reader.ListAsync(filter, page, cancellationToken);
    }
}
