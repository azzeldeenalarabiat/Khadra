using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Application.IdentityAccess.ResendVerification;

// Always succeeds from the caller's point of view so the endpoint cannot be used to enumerate accounts.
public sealed record ResendVerificationCommand(string Email) : ICommand<UnitResult<Error>>;

public sealed class ResendVerificationCommandValidator : AbstractValidator<ResendVerificationCommand>
{
    public ResendVerificationCommandValidator()
    {
        RuleFor(command => command.Email).NotEmpty().MaximumLength(EmailAddress.MaxLength);
    }
}
