using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.IdentityAccess.ForgotPassword;

// Always succeeds from the caller's point of view (no account enumeration).
public sealed record ForgotPasswordCommand(string Email) : ICommand<UnitResult<Error>>;

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(command => command.Email).NotEmpty().MaximumLength(EmailAddress.MaxLength);
    }
}
