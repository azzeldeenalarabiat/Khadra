using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.VerifyEmail;

public sealed class VerifyEmailHandler(
    IVerificationTokenRepository verificationTokens,
    IUserRepository users,
    IOpaqueTokenService opaqueTokens,
    IClock clock,
    IUnitOfWork unitOfWork)
    : IRequestHandler<VerifyEmailCommand, UnitResult<Error>>
{
    public async Task<UnitResult<Error>> Handle(VerifyEmailCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;
        var token = await verificationTokens.GetByHashAsync(
            opaqueTokens.Hash(request.Token),
            VerificationPurpose.EmailVerification,
            cancellationToken);
        if (token is null)
            return UnitResult.Failure(IdentityErrors.InvalidToken);

        var consumed = token.Consume(now);
        if (consumed.IsFailure)
            return consumed;

        var user = await users.GetByIdAsync(token.UserId, cancellationToken);
        if (user is null)
            return UnitResult.Failure(IdentityErrors.InvalidToken);

        user.VerifyEmail(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UnitResult.Success<Error>();
    }
}
