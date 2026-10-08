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
    ISignInThrottle throttle,
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

        // Pre-launch item 51: refused BEFORE the account is looked up or the password checked, so a name under a
        // refusal answers the same whether or not anyone holds it, and whether or not this guess was right.
        var now = clock.UtcNow;
        if (await throttle.BlockedForAsync(email.Value, now, cancellationToken) is { } wait)
            return IdentityErrors.TooManySignInAttempts(wait);

        var user = await users.GetByEmailAsync(email.Value, cancellationToken);
        if (user is null)
        {
            // Same hashing cost as a real check so timing does not reveal whether the account exists.
            passwordHasher.Verify(request.Password, passwordHasher.DummyHash);
            await throttle.RecordFailureAsync(email.Value, now, cancellationToken);
            return IdentityErrors.InvalidCredentials;
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash.Value))
        {
            await throttle.RecordFailureAsync(email.Value, now, cancellationToken);
            return IdentityErrors.InvalidCredentials;
        }

        // Account state is disclosed only after the password proves out. The right password on an account that may
        // not sign in (suspended, unverified) is not a guess: it is neither counted nor forgiven.
        var canAuthenticate = user.CanAuthenticate();
        if (canAuthenticate.IsFailure)
            return canAuthenticate.Error;

        await throttle.ResetAsync(email.Value, cancellationToken);
        user.RecordSuccessfulLogin(now);
        var tokens = await tokenFactory.IssueNewFamilyAsync(user, request.Client, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return tokens;
    }
}
