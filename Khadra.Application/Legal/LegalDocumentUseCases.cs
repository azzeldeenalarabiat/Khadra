using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Legal.Dtos;
using Khadra.Application.Legal.ReadModels;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.Legal;
using Khadra.Domain.Legal.Repositories;
using MediatR;

namespace Khadra.Application.Legal;

// The legal texts (Wave 2 G1; pre-launch item 224). Published by an administrator through the screen, never by a
// seeder or a migration. Each publish is a new append-only version, in force at once and audited in the same
// transaction. Anyone may read the version in force.

/// <summary>Publishes a version of a legal text, in force from the moment it is published.</summary>
/// <param name="Kind">The document: <c>Terms</c> or <c>Privacy</c> (any case), or its slug.</param>
public sealed record PublishLegalDocumentCommand(string Kind, string? VersionLabel, string? BodyEn, string? BodyAr)
    : ICommand<Result<LegalDocumentVersionDto, Error>>;

/// <summary>
/// What <see cref="PublishLegalDocumentCommand"/> would publish: every check it makes, and the HTML the public page
/// would show. A query carried by POST, because it takes both whole texts. Writes nothing.
/// </summary>
public sealed record PreviewLegalDocumentQuery(string Kind, string? VersionLabel, string? BodyEn, string? BodyAr)
    : IQuery<Result<LegalDocumentPreviewDto, Error>>;

/// <param name="Kind">One document's versions only; every document's when null.</param>
public sealed record ListLegalDocumentVersionsQuery(string? Kind, int? Page, int? PageSize)
    : IQuery<Result<PagedResult<LegalDocumentVersionSummaryDto>, Error>>;

public sealed record GetLegalDocumentVersionQuery(Id VersionId) : IQuery<Result<LegalDocumentVersionDto, Error>>;

/// <summary>The version of a document in force, as anyone may read it.</summary>
/// <param name="IfNoneMatch">The caller's <c>If-None-Match</c>: the version is not sent again when it still matches.</param>
public sealed record GetCurrentLegalDocumentQuery(string Kind, string? IfNoneMatch)
    : IQuery<Result<CurrentLegalDocumentResult, Error>>;

public sealed class PublishLegalDocumentCommandValidator : AbstractValidator<PublishLegalDocumentCommand>
{
    public PublishLegalDocumentCommandValidator() => RuleFor(command => command.Kind).NotEmpty().MaximumLength(40);
}

public sealed class PreviewLegalDocumentQueryValidator : AbstractValidator<PreviewLegalDocumentQuery>
{
    public PreviewLegalDocumentQueryValidator() => RuleFor(query => query.Kind).NotEmpty().MaximumLength(40);
}

/// <summary>Only a ceiling on what is read at all: the domain states every rule about the texts themselves.</summary>
internal static class LegalTextRules
{
    // Room for CRLF line ends, which become LF before the domain measures the text.
    private const int RawBodyCeiling = LegalDocumentVersion.MaxBodyLength * 2;

    public static bool WithinCeiling(string? body) => body is null || body.Length <= RawBodyCeiling;
}

public sealed class LegalDocumentHandlers(
    ILegalDocumentVersionRepository versions,
    ILegalDocumentReader reader,
    ILegalTextRenderer renderer,
    AdminActionRecorder audit,
    ICurrentActor actor,
    IClock clock,
    IUnitOfWork unitOfWork) :
    IRequestHandler<PublishLegalDocumentCommand, Result<LegalDocumentVersionDto, Error>>,
    IRequestHandler<PreviewLegalDocumentQuery, Result<LegalDocumentPreviewDto, Error>>,
    IRequestHandler<ListLegalDocumentVersionsQuery, Result<PagedResult<LegalDocumentVersionSummaryDto>, Error>>,
    IRequestHandler<GetLegalDocumentVersionQuery, Result<LegalDocumentVersionDto, Error>>,
    IRequestHandler<GetCurrentLegalDocumentQuery, Result<CurrentLegalDocumentResult, Error>>
{
    public async Task<Result<LegalDocumentVersionDto, Error>> Handle(
        PublishLegalDocumentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var drafted = await DraftAsync(request.Kind, request.VersionLabel, request.BodyEn, request.BodyAr, cancellationToken);
        if (drafted.IsFailure)
            return drafted.Error;
        var version = drafted.Value.Version;

        versions.Add(version);
        // Who published what, in the same transaction as the version itself. The label names it; the entry is the
        // only place the publisher's name is written.
        audit.Record(
            AuditAction.LegalDocumentPublished,
            AuditEntityType.LegalDocument,
            version.Id,
            $"{version.Kind.Name} {version.VersionLabel}",
            drafted.Value.Current?.VersionLabel,
            version.VersionLabel);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintConflictException conflict)
        {
            // Another administrator published the same document a moment earlier: nothing here was written.
            return conflict.ConstraintName == ILegalDocumentVersionRepository.KindLabelIndex
                ? LegalErrors.LabelTaken
                : LegalErrors.PublishConflict;
        }

        return await DetailAsync(version, cancellationToken);
    }

    public async Task<Result<LegalDocumentPreviewDto, Error>> Handle(
        PreviewLegalDocumentQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var drafted = await DraftAsync(request.Kind, request.VersionLabel, request.BodyEn, request.BodyAr, cancellationToken);
        if (drafted.IsFailure)
            return drafted.Error;
        var (version, current) = drafted.Value;

        return new LegalDocumentPreviewDto(
            version.Kind.Name,
            version.VersionLabel,
            new LegalTextsDto(renderer.Render(version.BodyEn), renderer.Render(version.BodyAr)),
            current is null ? null : Summary(current, await reader.AdminNameAsync(current.PublishedByAdminId, cancellationToken), LegalVersionStates.Current));
    }

    public async Task<Result<PagedResult<LegalDocumentVersionSummaryDto>, Error>> Handle(
        ListLegalDocumentVersionsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        LegalDocumentKind? kind = null;
        if (request.Kind is not null && (kind = LegalDocumentKind.FromSlug(request.Kind)) is null)
            return LegalErrors.KindUnknown;

        var now = clock.UtcNow;
        var current = (await reader.CurrentAsync(now, cancellationToken)).Select(version => version.VersionId).ToHashSet();
        var page = await reader.ListAsync(kind, PageRequest.From(request.Page, request.PageSize), cancellationToken);

        return new PagedResult<LegalDocumentVersionSummaryDto>(
            page.Items
                .Select(row => new LegalDocumentVersionSummaryDto(
                    row.VersionId.Value,
                    row.Kind.Name,
                    row.VersionLabel,
                    row.EffectiveFrom,
                    row.PublishedAt,
                    row.PublishedByAdminId.Value,
                    row.PublishedByName,
                    current.Contains(row.VersionId) ? LegalVersionStates.Current : LegalVersionStates.Superseded))
                .ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }

    public async Task<Result<LegalDocumentVersionDto, Error>> Handle(
        GetLegalDocumentVersionQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var version = await versions.GetByIdAsync(request.VersionId, cancellationToken);
        if (version is null)
            return LegalErrors.VersionNotFound;

        return await DetailAsync(version, cancellationToken);
    }

    public async Task<Result<CurrentLegalDocumentResult, Error>> Handle(
        GetCurrentLegalDocumentQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kind = LegalDocumentKind.FromSlug(request.Kind);
        if (kind is null)
            return LegalErrors.KindUnknown;

        // The validator names the version and the renderer, so it is answered from one indexed query. A caller
        // whose copy is still in force is sent nothing more, and nothing is rendered for it.
        var currentId = await versions.CurrentIdAsync(kind, clock.UtcNow, cancellationToken);
        if (currentId is not { } id)
            return LegalErrors.NotPublished;
        var etag = ETagFor(id);
        if (Matches(request.IfNoneMatch, etag))
            return new CurrentLegalDocumentResult(etag, null);

        var version = await versions.GetByIdAsync(id, cancellationToken);
        if (version is null)
            return LegalErrors.NotPublished;

        return new CurrentLegalDocumentResult(
            etag,
            new PublicLegalDocumentDto(
                version.Kind.Name,
                version.Id.Value,
                version.VersionLabel,
                version.EffectiveFrom,
                version.PublishedAt,
                new LegalTextsDto(renderer.Render(version.BodyEn), renderer.Render(version.BodyAr))));
    }

    /// <summary>A weak validator: equal ids and an equal renderer give the same document.</summary>
    private string ETagFor(Id versionId) => $"W/\"legal-r{renderer.Version}-{versionId.Value:N}\"";

    /// <summary>Whether <c>If-None-Match</c> names this validator (or is <c>*</c>), compared weakly as RFC 9110 says.</summary>
    private static bool Matches(string? ifNoneMatch, string etag)
    {
        if (string.IsNullOrWhiteSpace(ifNoneMatch))
            return false;
        static string Opaque(string tag) => tag.Trim() is var trimmed && trimmed.StartsWith("W/", StringComparison.Ordinal) ? trimmed[2..] : tag.Trim();
        var mine = Opaque(etag);
        return ifNoneMatch.Split(',').Any(candidate => candidate.Trim() == "*" || Opaque(candidate) == mine);
    }

    /// <summary>
    /// Every check publishing makes, in the order an administrator can act on. The texts are checked first, then the
    /// Markdown, then the label, which needs the database. The version in force is returned, which the new one would
    /// replace.
    /// </summary>
    private async Task<Result<(LegalDocumentVersion Version, LegalDocumentVersion? Current), Error>> DraftAsync(
        string kindName,
        string? versionLabel,
        string? bodyEn,
        string? bodyAr,
        CancellationToken cancellationToken)
    {
        var kind = LegalDocumentKind.FromSlug(kindName);
        if (kind is null)
            return LegalErrors.KindUnknown;
        if (!LegalTextRules.WithinCeiling(bodyEn) || !LegalTextRules.WithinCeiling(bodyAr))
            return LegalErrors.BodyTooLong;

        var now = clock.UtcNow;
        var published = LegalDocumentVersion.Publish(
            kind,
            versionLabel,
            bodyEn,
            bodyAr,
            actor.UserId!.Value,
            now,
            await versions.LatestEffectiveFromAsync(kind, cancellationToken));
        if (published.IsFailure)
            return published.Error;
        var version = published.Value;

        if (renderer.Check(version.BodyEn) is { } english)
            return LegalErrors.TextUnsupported("en", english.Line, english.Reason);
        if (renderer.Check(version.BodyAr) is { } arabic)
            return LegalErrors.TextUnsupported("ar", arabic.Line, arabic.Reason);

        if (await versions.LabelTakenAsync(kind, version.VersionLabel, cancellationToken))
            return LegalErrors.LabelTaken;

        var currentId = await versions.CurrentIdAsync(kind, now, cancellationToken);
        var current = currentId is { } id ? await versions.GetByIdAsync(id, cancellationToken) : null;
        return (version, current);
    }

    private async Task<LegalDocumentVersionDto> DetailAsync(LegalDocumentVersion version, CancellationToken cancellationToken)
    {
        var currentId = await versions.CurrentIdAsync(version.Kind, clock.UtcNow, cancellationToken);
        var state = currentId == version.Id ? LegalVersionStates.Current : LegalVersionStates.Superseded;
        return new LegalDocumentVersionDto(
            Summary(version, await reader.AdminNameAsync(version.PublishedByAdminId, cancellationToken), state),
            new LegalTextsDto(version.BodyEn, version.BodyAr),
            new LegalTextsDto(renderer.Render(version.BodyEn), renderer.Render(version.BodyAr)),
            new LegalTextsDto(version.BodyEnSha256, version.BodyArSha256));
    }

    private static LegalDocumentVersionSummaryDto Summary(LegalDocumentVersion version, string? publishedByName, string state) =>
        new(
            version.Id.Value,
            version.Kind.Name,
            version.VersionLabel,
            version.EffectiveFrom,
            version.PublishedAt,
            version.PublishedByAdminId.Value,
            publishedByName,
            state);
}
