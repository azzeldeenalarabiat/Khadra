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

        // The two "nothing to do" paths above return success on purpose: an unknown address and an
        // already-verified one must be indistinguishable from a real resend, or this endpoint
        // becomes a way to ask the platform who holds an account.
        //
        // A send that was ATTEMPTED and failed is different, and is reported. The narrow leak is
        // real — a failure implies the address reached the mail server, so it exists here — but it
        // only appears when the mail path is actually broken, and the alternative is telling someone
        // a link is on its way when the server refused it. Recorded on the pre-launch checklist
        // alongside the other enumeration items.
        var delivered = await emails.SendEmailVerificationAsync(user, rawToken.Value, cancellationToken);
        return delivered
            ? UnitResult.Success<Error>()
            : UnitResult.Failure(IdentityErrors.VerificationEmailNotSent);
    }
}
