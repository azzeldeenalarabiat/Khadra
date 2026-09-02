using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.ChangePassword;

public sealed class ChangePasswordHandler(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IAuthPolicySettings policy,
    AuthTokenFactory tokenFactory,
    IClock clock,
    IUnitOfWork unitOfWork,
    AuthEmailDispatcher emails)
    : IRequestHandler<ChangePasswordCommand, Result<AuthTokensDto, Error>>
{
    public async Task<Result<AuthTokensDto, Error>> Handle(
        ChangePasswordCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash.Value))
            return IdentityErrors.InvalidCredentials;

        var password = PasswordPolicy.Validate(request.NewPassword, policy.PasswordMinimumLength);
        if (password.IsFailure)
            return password.Error;

        var now = clock.UtcNow;
        user.ChangePassword(PasswordHash.FromHash(passwordHasher.Hash(request.NewPassword)), now);
        // Commit first so the UserPasswordChanged handler revokes the OLD families only.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var tokens = await tokenFactory.IssueNewFamilyAsync(user, request.Client, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await emails.SendPasswordChangedAsync(user, cancellationToken);
        return tokens;
    }
}
