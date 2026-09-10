using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.IdentityAccess.ForgotPassword;

/// <summary>
/// Issues a password-reset link, and says in the log why it did not when it did not.
/// </summary>
/// <remarks>
/// The public answer is deliberately the same whatever happens — "if an account exists for this
/// email, a password reset link has been sent" — because anything else turns this form into a way to
/// ask the platform who holds an account. That is correct, and it is also why a real failure here is
/// invisible from outside: a request that quietly matched nothing looks exactly like one that sent a
/// link. Production spent a day on that, with the API reporting success and no message ever leaving.
///
/// So the reason goes to the LOG, where an operator can read it and a caller cannot. What never goes
/// there is the thing that would undo the protection or hand over the account: the address is never
/// logged, and neither is the token, the link, or any password. A request is identified by its
/// correlation id — already returned to the caller as X-Correlation-ID and already on the "Handled
/// ForgotPasswordCommand" line — which joins the two without naming anybody.
/// </remarks>
public sealed partial class ForgotPasswordHandler(
    IUserRepository users,
    IVerificationTokenRepository verificationTokens,
    IOpaqueTokenService opaqueTokens,
    IAuthPolicySettings policy,
    IClock clock,
    IUnitOfWork unitOfWork,
    AuthEmailDispatcher emails,
    ICurrentActor actor,
    ILogger<ForgotPasswordHandler> logger)
    : IRequestHandler<ForgotPasswordCommand, UnitResult<Error>>
{
    public async Task<UnitResult<Error>> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var correlationId = actor.CorrelationId;

        var email = EmailAddress.Create(request.Email);
        if (email.IsFailure)
        {
            // Not the same as "no account": the submitted value was not an address at all, so nothing
            // was even looked up. The validator only checks that it is non-empty and short enough.
            LogAddressUnusable(logger, correlationId);
            return UnitResult.Success<Error>();
        }

        var user = await users.GetByEmailAsync(email.Value, cancellationToken);
        if (user is null)
        {
            // Two very different situations answer null, and telling them apart is most of the value
            // of this log line. GetByEmailAsync honours the soft-delete query filter; ExistsByEmailAsync
            // ignores it. So a hit here means the row is present and excluded, which is correct
            // behaviour -- a deleted account must not be resettable -- and is otherwise indistinguishable
            // from a typo. The extra query runs only on the miss path.
            var deletedRowExists = await users.ExistsByEmailAsync(email.Value, cancellationToken);
            if (deletedRowExists)
                LogAccountDeleted(logger, correlationId);
            else
                LogNoAccount(logger, correlationId);

            return UnitResult.Success<Error>();
        }

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

        // Neither the account's status nor whether its email was ever verified is checked, and that is
        // deliberate: a suspended person still owns their address, and someone who never confirmed it
        // is exactly the person likely to have forgotten the password. Recorded here because "it must
        // be blocked by the account state" is the first guess anyone makes when no mail arrives, and
        // the answer is no.
        //
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
        if (delivered)
        {
            // "Accepted by the transport", not "arrived". Worth a line, because its ABSENCE beside a
            // "Handled ForgotPasswordCommand" line is what says the handler returned early -- and that
            // line is written from a finally block, so it appears even when nothing was done.
            LogResetSent(logger, correlationId, user.Id.ToString());
            return UnitResult.Success<Error>();
        }

        LogResetNotSent(logger, correlationId, user.Id.ToString());
        return UnitResult.Failure(IdentityErrors.PasswordResetEmailNotSent);
    }

    [LoggerMessage(1300, LogLevel.Information,
        "Password reset for correlation {CorrelationId}: the submitted value is not a usable email " +
        "address, so nothing was looked up and no message was sent. The caller was told the usual " +
        "'if an account exists' answer.")]
    private static partial void LogAddressUnusable(ILogger logger, string correlationId);

    [LoggerMessage(1301, LogLevel.Information,
        "Password reset for correlation {CorrelationId}: no account matches that address, so no " +
        "message was sent. This is the normal answer to a typo. If it is unexpected, the address in " +
        "the database differs from the one submitted -- the column is case-sensitive in Postgres " +
        "while the application lower-cases on write, so a row inserted by hand can be unreachable.")]
    private static partial void LogNoAccount(ILogger logger, string correlationId);

    [LoggerMessage(1302, LogLevel.Warning,
        "Password reset for correlation {CorrelationId}: an account with that address EXISTS but is " +
        "soft-deleted, so it is excluded and no message was sent. This is intended -- a deleted " +
        "account must not be resettable -- and is logged because it is otherwise indistinguishable " +
        "from a typo.")]
    private static partial void LogAccountDeleted(ILogger logger, string correlationId);

    [LoggerMessage(1303, LogLevel.Information,
        "Password reset for correlation {CorrelationId}: link issued for user {UserId} and ACCEPTED " +
        "by the mail transport. Accepted is not delivered -- check the provider if it does not arrive.")]
    private static partial void LogResetSent(ILogger logger, string correlationId, string userId);

    [LoggerMessage(1304, LogLevel.Error,
        "Password reset for correlation {CorrelationId}: link issued for user {UserId} but the mail " +
        "transport REFUSED it. The token is valid, so asking again costs the person nothing. The " +
        "transport's own words are on the preceding delivery-failure line.")]
    private static partial void LogResetNotSent(ILogger logger, string correlationId, string userId);
}
