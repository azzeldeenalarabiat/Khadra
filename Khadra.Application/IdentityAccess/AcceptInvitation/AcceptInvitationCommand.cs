using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Legal;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.Legal;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.AcceptInvitation;

/// <summary>
/// An invited employee takes up their account: proves they own the mailbox, sets their first
/// password, and can sign in from then on (spec 4.2).
/// </summary>
/// <param name="Consent">
/// The legal texts the console showed and the person accepted (Wave 4, W4-8): required of a staff invitation while a
/// text is in force; ignored for an administrator's, whom the texts do not address (owner, 2026-10-07).
/// </param>
/// <returns>How many acceptances were recorded.</returns>
public sealed record AcceptInvitationCommand(string Token, string Password, ConsentInput? Consent = null)
    : ICommand<Result<int, Error>>;

public sealed class AcceptInvitationCommandValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationCommandValidator()
    {
        RuleFor(command => command.Token).NotEmpty().MaximumLength(512);
        RuleFor(command => command.Password).NotEmpty().MaximumLength(72);
        RuleFor(command => command.Consent!).SetValidator(new ConsentInputValidator()).When(command => command.Consent is not null);
    }
}

/// <summary>Shaped like ResetPasswordHandler: one token, one consumption, one password.</summary>
public sealed class AcceptInvitationHandler(
    IVerificationTokenRepository verificationTokens,
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IOpaqueTokenService opaqueTokens,
    IAuthPolicySettings policy,
    IClock clock,
    IUnitOfWork unitOfWork,
    LegalConsentRecorder consents)
    : IRequestHandler<AcceptInvitationCommand, Result<int, Error>>
{
    public async Task<Result<int, Error>> Handle(AcceptInvitationCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var password = PasswordPolicy.Validate(request.Password, policy.PasswordMinimumLength);
        if (password.IsFailure)
            return password.Error;

        var now = clock.UtcNow;

        // One screen redeems both invitations, so the token decides which it is. Looked up by hash
        // against each purpose in turn rather than asking the caller: the link is all the person
        // has, and a staff invitation and an admin one are indistinguishable from the outside.
        var hash = opaqueTokens.Hash(request.Token);
        var token =
            await verificationTokens.GetByHashAsync(hash, VerificationPurpose.EmployeeInvitation, cancellationToken)
            ?? await verificationTokens.GetByHashAsync(hash, VerificationPurpose.AdminInvitation, cancellationToken);
        if (token is null)
            return IdentityErrors.InvalidToken;

        var consumed = token.Consume(now);
        if (consumed.IsFailure)
            return consumed.Error;

        var user = await users.GetByIdAsync(token.UserId, cancellationToken);
        if (user is null)
            return IdentityErrors.InvalidToken;

        // An invitation must not outlive the first real password.
        //
        // PasswordChangedAt is null for every invited account -- the factories never set it, and only
        // ChangePassword does -- so this is a precise "somebody has already chosen a password here".
        // Without it an invitation stays a working credential for its whole seven days EVEN AFTER the
        // owner has the account: accepting again would overwrite their password and hand the account
        // to whoever still holds the link. That is not hypothetical here. Every message sent while
        // Email:Provider selected the Logging transport was written to the application log WITH its
        // link, so an invitation issued in that window is readable by anyone who can read the log,
        // and it would otherwise survive the owner recovering the account by any other route.
        //
        // It does not block the legitimate path. An invited person has no password yet, and somebody
        // who verified their address first (resend-verification issues its own token, and gates on
        // the address rather than the role, so an invited administrator can use it) still has none
        // until they choose one here.
        if (user.PasswordChangedAt is not null)
            return IdentityErrors.InvalidToken;

        // A staff invitation accepts the texts in force in the same save (Wave 4, W4-8); a refusal here saves nothing,
        // so it does not spend the invitation. An administrator's asks nothing: the texts address customers and rental
        // offices, not Khadra's own staff (owner, 2026-10-07), and whatever was sent with it is not recorded.
        var recorded = 0;
        if (token.Purpose == VerificationPurpose.EmployeeInvitation)
        {
            var consented = await consents.StageAsync(
                user.Id, request.Consent ?? ConsentInput.None, ConsentChannel.Console, required: true, now, cancellationToken);
            if (consented.IsFailure)
                return consented.Error;
            recorded = consented.Value;
        }

        // Accepting IS the proof of address: nobody else could have read the link. Then the first
        // real password replaces the unusable one; the stamp rotation and session revoke it raises
        // are harmless on an account that has never signed in.
        user.VerifyEmail(now);
        user.ChangePassword(PasswordHash.FromHash(passwordHasher.Hash(request.Password)), now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return recorded;
    }
}
