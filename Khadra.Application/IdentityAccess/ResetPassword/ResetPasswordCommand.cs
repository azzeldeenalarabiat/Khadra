using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.IdentityAccess.ResetPassword;

public sealed record ResetPasswordCommand(string Token, string NewPassword) : ICommand<UnitResult<Error>>;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(command => command.Token).NotEmpty().MaximumLength(512);
        RuleFor(command => command.NewPassword).NotEmpty().MaximumLength(PasswordPolicy.MaximumLength);
    }
}
