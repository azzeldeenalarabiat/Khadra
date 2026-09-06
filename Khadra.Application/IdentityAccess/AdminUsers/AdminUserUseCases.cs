using System.Security.Cryptography;
using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.AdminUsers;

// Who may administer the platform (spec 3).
//
// There is ONE administrator role. The design draws a Support/Finance/Super picker, but `UserRole`
// has a single Admin member and no finer permission model exists, so offering a choice would be a
// promise the system cannot keep. Deactivation is `User.Suspend`, not `Delete`: the cross-deleted
// unique index on email means deleting an administrator burns their address for ever.

public sealed record ListAdminUsersQuery : IQuery<Result<IReadOnlyList<AdminUserListItem>, Error>>;

/// <param name="ExpiresAt">When the invitation link dies, so the console never states a lifetime it guessed.</param>
/// <param name="InvitationEmailSent">
/// Whether the relay accepted the invitation. False does NOT undo the invitation -- the account and
/// its token stand -- but the inviting administrator is the only person who can act on it, and there
/// is no way for them to notice on their own: the row looks the same either way. It matters more here
/// than anywhere else because there is no resend command for an administrator invitation, and the
/// unique index on the address means re-inviting answers 409 for ever.
/// </param>
public sealed record InviteAdminResult(
    Guid UserId,
    string Email,
    DateTimeOffset ExpiresAt,
    bool InvitationEmailSent);

public sealed record InviteAdminCommand(string Email, string Phone, string FullName)
    : ICommand<Result<InviteAdminResult, Error>>;

public sealed record DeactivateAdminCommand(Id UserId, string Reason) : ICommand<UnitResult<Error>>;

public sealed record ReactivateAdminCommand(Id UserId) : ICommand<UnitResult<Error>>;

public sealed class InviteAdminCommandValidator : AbstractValidator<InviteAdminCommand>
{
    public InviteAdminCommandValidator()
    {
        // Name and phone are required because User needs both; the same recorded deviation from the
        // design as the employee invitation, which asks for them for the same reason.
        RuleFor(command => command.Email).NotEmpty().MaximumLength(EmailAddress.MaxLength);
        RuleFor(command => command.Phone).NotEmpty();
        RuleFor(command => command.FullName).NotEmpty().MaximumLength(PersonName.MaxLength);
    }
}

public sealed class DeactivateAdminCommandValidator : AbstractValidator<DeactivateAdminCommand>
{
    public DeactivateAdminCommandValidator()
    {
        // 500 to match users.suspension_reason, which is what stores it.
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(500);
    }
}

public sealed class ListAdminUsersHandler(IAdminUserReader reader)
    : IRequestHandler<ListAdminUsersQuery, Result<IReadOnlyList<AdminUserListItem>, Error>>
{
    public async Task<Result<IReadOnlyList<AdminUserListItem>, Error>> Handle(
        ListAdminUsersQuery request,
        CancellationToken cancellationToken)
    {
        var admins = await reader.ListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<AdminUserListItem>, Error>(admins);
    }
}

public sealed class AdminUserCommandHandlers(
    IUserRepository users,
    IVerificationTokenRepository verificationTokens,
    IPasswordHasher passwordHasher,
    IOpaqueTokenService opaqueTokens,
    IAuthPolicySettings policy,
    AuthEmailDispatcher emails,
    AdminActionRecorder audit,
    ICurrentActor actor,
    IUnitOfWork unitOfWork,
    IClock clock) :
    IRequestHandler<InviteAdminCommand, Result<InviteAdminResult, Error>>,
    IRequestHandler<DeactivateAdminCommand, UnitResult<Error>>,
    IRequestHandler<ReactivateAdminCommand, UnitResult<Error>>
{
    public async Task<Result<InviteAdminResult, Error>> Handle(InviteAdminCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var address = EmailAddress.Create(request.Email);
        if (address.IsFailure)
            return address.Error;
        var phone = PhoneNumber.Create(request.Phone);
        if (phone.IsFailure)
            return phone.Error;
        var name = PersonName.Create(request.FullName);
        if (name.IsFailure)
            return name.Error;

        // Platform-wide uniqueness: User.Role is singular, so someone who already rents here cannot
        // also administer under the same address.
        if (await users.ExistsByEmailAsync(address.Value, cancellationToken))
            return IdentityErrors.EmailTaken;
        if (await users.ExistsByPhoneAsync(phone.Value, cancellationToken))
            return IdentityErrors.PhoneTaken;

        var now = clock.UtcNow;

        // A hash of bytes nobody keeps: there is no usable password until the invitation is
        // accepted, and the unverified email refuses a sign-in anyway. The alternative — a temporary
        // password — puts a working credential into an email nobody controls.
        var unusable = passwordHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var invited = User.CreateInvitedAdmin(
            address.Value, phone.Value, name.Value, PasswordHash.FromHash(unusable), now);
        await users.AddAsync(invited, cancellationToken);

        var invitation = opaqueTokens.Generate();
        var expiresAt = now.Add(policy.EmployeeInvitationLifetime);
        await verificationTokens.AddAsync(
            VerificationToken.Issue(
                invited.Id, VerificationPurpose.AdminInvitation, invitation.Hash, now, policy.EmployeeInvitationLifetime),
            cancellationToken);

        // An administrator's name in the log is fine: pre-launch item 18 is about customers, whose
        // identity the platform holds on their behalf.
        audit.Record(
            AuditAction.AdminInvited,
            AuditEntityType.AdminUser,
            invited.Id,
            invited.Name.Value,
            null,
            UserRole.Admin.Name);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // After the commit: an email promising an account that failed to save would be worse than
        // no email at all. Through the dispatcher, because an unhandled send here answered 500 to an
        // invitation that had in fact been created -- and the retry then met 409 email_taken.
        var delivered = await emails.SendAdminInvitationAsync(invited, invitation.Value, cancellationToken);

        return new InviteAdminResult(invited.Id.Value, invited.Email.Value, expiresAt, delivered);
    }

    public async Task<UnitResult<Error>> Handle(DeactivateAdminCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null || user.Role != UserRole.Admin)
            return UnitResult.Failure(IdentityErrors.UserNotFound);

        // Two invariants the domain cannot hold, because both are about the SET of administrators
        // rather than about one of them.
        //
        // Nobody may lock themselves out mid-action, and nobody may remove the last account that
        // could undo this: there is no back door that creates an administrator, so an empty set is
        // the end of the console.
        if (actor.UserId == user.Id)
            return UnitResult.Failure(IdentityErrors.CannotDeactivateSelf);

        var remaining = await users.CountActiveAdminsExceptAsync(user.Id, cancellationToken);
        if (remaining < 1)
            return UnitResult.Failure(IdentityErrors.LastAdministrator);

        var previousStatus = user.Status.Name;
        var outcome = user.Suspend(request.Reason, clock.UtcNow);
        if (outcome.IsFailure)
            return outcome;

        audit.Record(
            AuditAction.AdminDeactivated,
            AuditEntityType.AdminUser,
            user.Id,
            user.Name.Value,
            previousStatus,
            user.Status.Name,
            request.Reason);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UnitResult.Success<Error>();
    }

    public async Task<UnitResult<Error>> Handle(ReactivateAdminCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null || user.Role != UserRole.Admin)
            return UnitResult.Failure(IdentityErrors.UserNotFound);

        var previousStatus = user.Status.Name;
        var outcome = user.Reactivate(clock.UtcNow);
        if (outcome.IsFailure)
            return outcome;

        audit.Record(
            AuditAction.AdminReactivated,
            AuditEntityType.AdminUser,
            user.Id,
            user.Name.Value,
            previousStatus,
            user.Status.Name);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return UnitResult.Success<Error>();
    }
}
