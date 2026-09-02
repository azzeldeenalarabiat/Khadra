using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Domain.Common;

namespace Khadra.Application.IdentityAccess.Logout;

// Idempotent: unknown or foreign tokens are ignored so logout always succeeds for the caller.
public sealed record LogoutCommand(Id UserId, string RefreshToken, bool AllDevices) : ICommand<UnitResult<Error>>;

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator()
    {
        RuleFor(command => command.RefreshToken).NotEmpty().MaximumLength(512);
    }
}
