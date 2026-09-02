using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Domain.Common;

namespace Khadra.Application.IdentityAccess.VerifyEmail;

public sealed record VerifyEmailCommand(string Token) : ICommand<UnitResult<Error>>;

public sealed class VerifyEmailCommandValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailCommandValidator()
    {
        RuleFor(command => command.Token).NotEmpty().MaximumLength(512);
    }
}
