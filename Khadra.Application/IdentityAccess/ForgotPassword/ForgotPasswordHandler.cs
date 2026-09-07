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

        // The two "nothing to do" paths above return success on purpose: an unparseable address and
        // an unknown one must be indistinguishable from a real request, or this form becomes a way to
        // ask the platform who holds an account.
        //
        // A send that was ATTEMPTED and failed is different, and is reported -- the same trade-off
        // ResendVerification already makes. The leak is real and slightly wider here (any account,
        // not only an unverified one), but it opens ONLY while the mail path is broken, and the
        // alternative is what this screen did until now: tell someone a reset link is on its way when
        // the relay had just refused it, and leave them waiting at an inbox nothing was coming to.
        // Recorded on the pre-launch checklist beside the other enumeration items.
        var delivered = await emails.SendPasswordResetAsync(user, rawToken.Value, cancellationToken);
        return delivered
            ? UnitResult.Success<Error>()
            : UnitResult.Failure(IdentityErrors.PasswordResetEmailNotSent);
    }
}
