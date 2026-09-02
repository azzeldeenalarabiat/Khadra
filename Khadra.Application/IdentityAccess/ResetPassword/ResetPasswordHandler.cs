using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.ResetPassword;

public sealed class ResetPasswordHandler(
    IVerificationTokenRepository verificationTokens,
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IOpaqueTokenService opaqueTokens,
    IAuthPolicySettings policy,
    IClock clock,
    IUnitOfWork unitOfWork,
    AuthEmailDispatcher emails)
    : IRequestHandler<ResetPasswordCommand, UnitResult<Error>>
{
    public async Task<UnitResult<Error>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var password = PasswordPolicy.Validate(request.NewPassword, policy.PasswordMinimumLength);
        if (password.IsFailure)
            return password;

        var now = clock.UtcNow;
        var token = await verificationTokens.GetByHashAsync(
            opaqueTokens.Hash(request.Token),
            VerificationPurpose.PasswordReset,
            cancellationToken);
        if (token is null)
            return UnitResult.Failure(IdentityErrors.InvalidToken);

        var consumed = token.Consume(now);
        if (consumed.IsFailure)
            return consumed;

        var user = await users.GetByIdAsync(token.UserId, cancellationToken);
        if (user is null)
            return UnitResult.Failure(IdentityErrors.InvalidToken);

        // Raises UserPasswordChanged: every refresh-token family of this user is revoked after commit.
        user.ChangePassword(PasswordHash.FromHash(passwordHasher.Hash(request.NewPassword)), now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await emails.SendPasswordChangedAsync(user, cancellationToken);
        return UnitResult.Success<Error>();
    }
}
