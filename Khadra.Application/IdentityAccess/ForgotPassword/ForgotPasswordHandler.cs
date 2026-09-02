using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.ForgotPassword;

public sealed class ForgotPasswordHandler(
    IUserRepository users,
    IVerificationTokenRepository verificationTokens,
    IOpaqueTokenService opaqueTokens,
    IAuthPolicySettings policy,
    IClock clock,
    IUnitOfWork unitOfWork,
    AuthEmailDispatcher emails)
    : IRequestHandler<ForgotPasswordCommand, UnitResult<Error>>
{
    public async Task<UnitResult<Error>> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = EmailAddress.Create(request.Email);
        if (email.IsFailure)
            return UnitResult.Success<Error>();

        var user = await users.GetByEmailAsync(email.Value, cancellationToken);
        if (user is null)
            return UnitResult.Success<Error>();

        var now = clock.UtcNow;
        await verificationTokens.InvalidateActiveAsync(user.Id, VerificationPurpose.PasswordReset, now, cancellationToken);

        var rawToken = opaqueTokens.Generate();
        var token = VerificationToken.Issue(
            user.Id,
            VerificationPurpose.PasswordReset,
            rawToken.Hash,
            now,
            policy.PasswordResetLifetime);
        await verificationTokens.AddAsync(token, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await emails.SendPasswordResetAsync(user, rawToken.Value, cancellationToken);
        return UnitResult.Success<Error>();
    }
}
