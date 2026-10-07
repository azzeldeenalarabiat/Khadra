using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Legal.ReadModels;
using Khadra.Application.PlatformSettings.AppConfig;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using MediatR;

namespace Khadra.Application.Legal;

/// <summary>One acceptance on a person's record.</summary>
/// <param name="Channel"><c>Website</c>, <c>App</c> or <c>Console</c>.</param>
/// <param name="Language">The language of the text the person read: <c>ar</c> or <c>en</c>.</param>
public sealed record AcceptedLegalConsentDto(
    string Kind,
    Guid VersionId,
    string VersionLabel,
    DateTimeOffset AcceptedAt,
    string Channel,
    string Language);

/// <summary>A person's consents to the legal texts (Wave 4, W4-8).</summary>
/// <param name="Accepted">Every acceptance on the record, newest first: the record producible for the data subject.</param>
/// <param name="Pending">
/// The texts in force still to accept, in the shape <c>/app-config</c> lists them; always empty for an administrator,
/// whom the texts do not address (owner, 2026-10-07).
/// </param>
public sealed record MyLegalConsentsDto(
    IReadOnlyList<AcceptedLegalConsentDto> Accepted,
    IReadOnlyList<LegalConfigDocumentDto> Pending);

public sealed record GetMyLegalConsentsQuery(Id UserId, UserRole Role) : IQuery<Result<MyLegalConsentsDto, Error>>;

/// <summary>The consent prompt's answer: the person accepts the texts in force they were shown.</summary>
public sealed record AcceptLegalTextsCommand(
    Id UserId,
    UserRole Role,
    IReadOnlyList<Guid> VersionIds,
    string Language,
    ClientInfo Client) : ICommand<Result<MyLegalConsentsDto, Error>>;

public sealed class AcceptLegalTextsCommandValidator : AbstractValidator<AcceptLegalTextsCommand>
{
    /// <summary>A guard, not a business figure: two texts are published today, and a person accepts each once.</summary>
    public const int MaxVersions = 10;

    public AcceptLegalTextsCommandValidator()
    {
        RuleFor(command => command.VersionIds).NotEmpty().Must(ids => ids.Count <= MaxVersions)
            .WithMessage($"Accept at most {MaxVersions} texts at once.");
        RuleFor(command => command.Language).Must(ConsentInput.IsLanguage).WithMessage("Language must be ar or en.");
    }
}

public sealed class MyLegalConsentsHandlers(
    ILegalConsentReader consents,
    LegalConsentRecorder recorder,
    ICustomerSiteSettings site,
    IClock clock,
    IUnitOfWork unitOfWork)
    : IRequestHandler<GetMyLegalConsentsQuery, Result<MyLegalConsentsDto, Error>>,
      IRequestHandler<AcceptLegalTextsCommand, Result<MyLegalConsentsDto, Error>>
{
    public async Task<Result<MyLegalConsentsDto, Error>> Handle(GetMyLegalConsentsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ReadAsync(request.UserId, request.Role, clock.UtcNow, cancellationToken);
    }

    public async Task<Result<MyLegalConsentsDto, Error>> Handle(AcceptLegalTextsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;

        // An administrator is never asked, and records nothing (the advisor's review): a row saying staff agreed to the
        // customers' terms would be a permanent record of something that did not apply to them.
        if (request.Role == UserRole.Admin)
            return await ReadAsync(request.UserId, request.Role, now, cancellationToken);

        var staged = await recorder.StageAsync(
            request.UserId,
            new ConsentInput(request.VersionIds, request.Language),
            LegalConsentRecorder.ChannelFor(request.Role, request.Client),
            required: false,
            now,
            cancellationToken);
        if (staged.IsFailure)
            return staged.Error;

        // An acceptance already on the record writes nothing; the answer is the record either way.
        if (staged.Value > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return await ReadAsync(request.UserId, request.Role, now, cancellationToken);
    }

    private async Task<MyLegalConsentsDto> ReadAsync(Id userId, UserRole role, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var accepted = await consents.AcceptedAsync(userId, cancellationToken);
        IReadOnlyList<PendingLegalVersion> pending =
            role == UserRole.Admin ? [] : await consents.PendingAsync(userId, now, cancellationToken);
        return new MyLegalConsentsDto(
            [.. accepted.Select(row => new AcceptedLegalConsentDto(
                row.Kind.Name, row.VersionId.Value, row.VersionLabel, row.AcceptedAt, row.Channel.Name, row.Language.Name))],
            [.. pending.Select(row => LegalPageLinks.Describe(site, row))]);
    }
}
