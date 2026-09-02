using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.Login;

public sealed class LoginHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    AuthTokenFactory tokenFactory,
    IClock clock,
    IUnitOfWork unitOfWork)
    : IRequestHandler<LoginCommand, Result<AuthTokensDto, Error>>
{
    public async Task<Result<AuthTokensDto, Error>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = EmailAddress.Create(request.Email);
        if (email.IsFailure)
            return IdentityErrors.InvalidCredentials;

        var user = await users.GetByEmailAsync(email.Value, cancellationToken);
        if (user is null)
        {
            // Same hashing cost as a real check so timing does not reveal whether the account exists.
            passwordHasher.Verify(request.Password, passwordHasher.DummyHash);
            return IdentityErrors.InvalidCredentials;
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash.Value))
            return IdentityErrors.InvalidCredentials;

        // Account state is disclosed only after the password proves out.
        var canAuthenticate = user.CanAuthenticate();
        if (canAuthenticate.IsFailure)
            return canAuthenticate.Error;

        var now = clock.UtcNow;
        user.RecordSuccessfulLogin(now);
        var tokens = await tokenFactory.IssueNewFamilyAsync(user, request.Client, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return tokens;
    }
}
