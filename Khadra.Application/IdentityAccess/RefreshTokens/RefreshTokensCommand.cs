using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Domain.Common;

namespace Khadra.Application.IdentityAccess.RefreshTokens;

public sealed record RefreshTokensCommand(string RefreshToken, ClientInfo Client)
    : ICommand<Result<AuthTokensDto, Error>>;

public sealed class RefreshTokensCommandValidator : AbstractValidator<RefreshTokensCommand>
{
    public RefreshTokensCommandValidator()
    {
        RuleFor(command => command.RefreshToken).NotEmpty().MaximumLength(512);
        RuleFor(command => command.Client).NotNull();
    }
}
