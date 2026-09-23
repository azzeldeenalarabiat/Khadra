using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.PushDevices;

// The signed-in person's OWN phone: where to wake it, and in which language. Every route answers for
// whoever is calling and for the session the request was made on; nobody registers a device for
// somebody else.

/// <param name="Token">The push service's token for this install.</param>
/// <param name="Platform">"Android" or "Ios".</param>
/// <param name="Language">"ar" or "en": the language the app is showing right now.</param>
public sealed record RegisterMyPushDeviceCommand(
    string Token,
    string Platform,
    string Language,
    string? AppVersion) : ICommand<UnitResult<Error>>
{
    // The token addresses one phone. It is not a credential, but a log line that carries it lets
    // anyone reading the log wake that phone with our sender, so it is never printed.
    public override string ToString() =>
        $"{nameof(RegisterMyPushDeviceCommand)} {{ Platform = {Platform}, Language = {Language}, AppVersion = {AppVersion} }}";
}

public sealed class RegisterMyPushDeviceCommandValidator : AbstractValidator<RegisterMyPushDeviceCommand>
{
    public RegisterMyPushDeviceCommandValidator()
    {
        RuleFor(command => command.Token).NotEmpty().MaximumLength(PushDevice.MaxTokenLength);
        RuleFor(command => command.Platform)
            .Must(value => Enumeration.GetAll<PushPlatform>().Any(p => string.Equals(p.Name, value, StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Platform must be Android or Ios.");
        RuleFor(command => command.Language)
            .Must(value => Enumeration.GetAll<Language>().Any(l => string.Equals(l.Name, value, StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Language must be ar or en.");
        RuleFor(command => command.AppVersion).MaximumLength(PushDevice.MaxAppVersionLength);
    }
}

/// <summary>Removes the device tied to the session making the request. Idempotent.</summary>
public sealed record RemoveMyPushDeviceCommand : ICommand<UnitResult<Error>>;

public sealed class RegisterMyPushDeviceHandler(
    IPushDeviceRepository devices,
    IUnitOfWork unitOfWork,
    ICurrentActor actor,
    IClock clock)
    : IRequestHandler<RegisterMyPushDeviceCommand, UnitResult<Error>>
{
    public async Task<UnitResult<Error>> Handle(RegisterMyPushDeviceCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (actor.UserId is not { } userId)
            return UnitResult.Failure(IdentityErrors.UserNotFound);

        var platform = Enumeration.GetAll<PushPlatform>()
            .Single(p => string.Equals(p.Name, request.Platform, StringComparison.OrdinalIgnoreCase));
        var language = Enumeration.GetAll<Language>()
            .Single(l => string.Equals(l.Name, request.Language, StringComparison.OrdinalIgnoreCase));
        var now = clock.UtcNow;

        var existing = await devices.GetByTokenAsync(request.Token, cancellationToken);
        if (existing is null)
        {
            await devices.AddAsync(
                PushDevice.Register(request.Token, platform, userId, actor.SessionId, language, request.AppVersion, now),
                cancellationToken);
        }
        else
        {
            // Whoever registers last owns the install: a phone that changed hands follows its new
            // owner, and the previous one stops receiving on it.
            existing.Reregister(platform, userId, actor.SessionId, language, request.AppVersion, now);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintConflictException)
        {
            // Two registrations of the same new token raced (the app registers on sign-in and on a
            // token refresh that can land at the same moment). The other one wrote the row; either
            // answer describes this phone, so the caller has what it asked for.
        }

        return UnitResult.Success<Error>();
    }
}

public sealed class RemoveMyPushDeviceHandler(
    IPushDeviceRepository devices,
    ICurrentActor actor,
    IClock clock)
    : IRequestHandler<RemoveMyPushDeviceCommand, UnitResult<Error>>
{
    public async Task<UnitResult<Error>> Handle(RemoveMyPushDeviceCommand request, CancellationToken cancellationToken)
    {
        if (actor.UserId is not { } userId)
            return UnitResult.Failure(IdentityErrors.UserNotFound);

        // No session id means a token issued before the claim existed; there is nothing this request
        // can name, and the session join at delivery time still keeps a signed-out phone quiet.
        if (actor.SessionId is { } session)
            await devices.RevokeForSessionAsync(userId, session, clock.UtcNow, cancellationToken);

        return UnitResult.Success<Error>();
    }
}

/// <summary>Records the language this person reads Khadra in.</summary>
public sealed record SetMyLanguageCommand(string Language) : ICommand<UnitResult<Error>>;

public sealed class SetMyLanguageCommandValidator : AbstractValidator<SetMyLanguageCommand>
{
    public SetMyLanguageCommandValidator()
    {
        RuleFor(command => command.Language)
            .Must(value => Enumeration.GetAll<Language>().Any(l => string.Equals(l.Name, value, StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Language must be ar or en.");
    }
}

public sealed class SetMyLanguageHandler(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    ICurrentActor actor)
    : IRequestHandler<SetMyLanguageCommand, UnitResult<Error>>
{
    public async Task<UnitResult<Error>> Handle(SetMyLanguageCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (actor.UserId is not { } userId)
            return UnitResult.Failure(IdentityErrors.UserNotFound);

        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return UnitResult.Failure(IdentityErrors.UserNotFound);

        user.ChoosePreferredLanguage(Enumeration.GetAll<Language>()
            .Single(l => string.Equals(l.Name, request.Language, StringComparison.OrdinalIgnoreCase)));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UnitResult.Success<Error>();
    }
}
