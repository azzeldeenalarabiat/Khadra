using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.AcceptInvitation;

/// <summary>
/// An invited employee takes up their account: proves they own the mailbox, sets their first
/// password, and can sign in from then on (spec 4.2).
/// </summary>
public sealed record AcceptInvitationCommand(string Token, string Password) : ICommand<UnitResult<Error>>;

public sealed class AcceptInvitationCommandValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationCommandValidator()
    {
        RuleFor(command => command.Token).NotEmpty().MaximumLength(512);
        RuleFor(command => command.Password).NotEmpty().MaximumLength(72);
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
    IUnitOfWork unitOfWork)
    : IRequestHandler<AcceptInvitationCommand, UnitResult<Error>>
{
    public async Task<UnitResult<Error>> Handle(AcceptInvitationCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var password = PasswordPolicy.Validate(request.Password, policy.PasswordMinimumLength);
        if (password.IsFailure)
            return password;

        var now = clock.UtcNow;
        var token = await verificationTokens.GetByHashAsync(
            opaqueTokens.Hash(request.Token),
            VerificationPurpose.EmployeeInvitation,
            cancellationToken);
        if (token is null)
            return UnitResult.Failure(IdentityErrors.InvalidToken);

        var consumed = token.Consume(now);
        if (consumed.IsFailure)
            return consumed;

        var user = await users.GetByIdAsync(token.UserId, cancellationToken);
        if (user is null)
            return UnitResult.Failure(IdentityErrors.InvalidToken);

        // Accepting IS the proof of address: nobody else could have read the link. Then the first
        // real password replaces the unusable one; the stamp rotation and session revoke it raises
        // are harmless on an account that has never signed in.
        user.VerifyEmail(now);
        user.ChangePassword(PasswordHash.FromHash(passwordHasher.Hash(request.Password)), now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UnitResult.Success<Error>();
    }
}
