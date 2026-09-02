using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.IdentityAccess.ChangePassword;

// Authenticated password change. Every existing session is revoked and a fresh token pair is returned.
public sealed record ChangePasswordCommand(
    Id UserId,
    string CurrentPassword,
    string NewPassword,
    ClientInfo Client) : ICommand<Result<AuthTokensDto, Error>>;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(command => command.CurrentPassword).NotEmpty().MaximumLength(PasswordPolicy.MaximumLength);
        RuleFor(command => command.NewPassword).NotEmpty().MaximumLength(PasswordPolicy.MaximumLength);
        RuleFor(command => command.Client).NotNull();
    }
}
