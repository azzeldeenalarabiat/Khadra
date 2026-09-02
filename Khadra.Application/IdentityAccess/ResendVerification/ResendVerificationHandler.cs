using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.ResendVerification;

public sealed class ResendVerificationHandler(
    IUserRepository users,
    IVerificationTokenRepository verificationTokens,
    IOpaqueTokenService opaqueTokens,
    IAuthPolicySettings policy,
    IClock clock,
    IUnitOfWork unitOfWork,
    AuthEmailDispatcher emails)
    : IRequestHandler<ResendVerificationCommand, UnitResult<Error>>
{
    public async Task<UnitResult<Error>> Handle(ResendVerificationCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = EmailAddress.Create(request.Email);
        if (email.IsFailure)
            return UnitResult.Success<Error>();

        var user = await users.GetByEmailAsync(email.Value, cancellationToken);
        if (user is null || user.IsEmailVerified)
            return UnitResult.Success<Error>();

        var now = clock.UtcNow;
        await verificationTokens.InvalidateActiveAsync(user.Id, VerificationPurpose.EmailVerification, now, cancellationToken);

        var rawToken = opaqueTokens.Generate();
        var token = VerificationToken.Issue(
            user.Id,
            VerificationPurpose.EmailVerification,
            rawToken.Hash,
            now,
            policy.EmailVerificationLifetime);
        await verificationTokens.AddAsync(token, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await emails.SendEmailVerificationAsync(user, rawToken.Value, cancellationToken);
        return UnitResult.Success<Error>();
    }
}
