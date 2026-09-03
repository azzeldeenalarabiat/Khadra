using System.Security.Cryptography;
using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;

namespace Khadra.Application.IdentityAccess;

/// <summary>An invited employee's account, and the one-time token their invitation email carries.</summary>
public sealed record ProvisionedEmployee(User User, string RawInvitationToken);

/// <summary>
/// Creates the USER half of a new employee (spec 4.2) -- the Identity context's part of a decision
/// the Dealers context makes.
///
/// The Dealers handler cannot build a User itself (a context never constructs another's aggregate),
/// so this is the named door, the way AccountRegistrar is for self-registration. It STAGES the user
/// and the invitation token; the caller's single SaveChangesAsync commits them with the hire, so an
/// employee cannot exist in one context and not the other.
///
/// Invitation rather than temporary password: the secret in the email is single-use and expiring,
/// the address is proved by the act of accepting, and nothing about the account works until then.
/// </summary>
public sealed class EmployeeAccountProvisioner(
    IUserRepository users,
    IVerificationTokenRepository verificationTokens,
    IPasswordHasher passwordHasher,
    IOpaqueTokenService opaqueTokens,
    IAuthPolicySettings policy,
    IClock clock)
{
    public async Task<Result<ProvisionedEmployee, Error>> ProvisionAsync(
        string rawEmail,
        string rawPhone,
        string rawName,
        CancellationToken cancellationToken)
    {
        var email = EmailAddress.Create(rawEmail);
        if (email.IsFailure)
            return email.Error;

        var phone = PhoneNumber.Create(rawPhone);
        if (phone.IsFailure)
            return phone.Error;

        var name = PersonName.Create(rawName);
        if (name.IsFailure)
            return name.Error;

        // Platform-wide uniqueness, deliberately: User.Role is singular, so a person who already has
        // a customer account cannot also be staff under the same address. The owner uses a work one.
        if (await users.ExistsByEmailAsync(email.Value, cancellationToken))
            return IdentityErrors.EmailTaken;
        if (await users.ExistsByPhoneAsync(phone.Value, cancellationToken))
            return IdentityErrors.PhoneTaken;

        var now = clock.UtcNow;

        // A hash of bytes nobody keeps: the account has no usable password until the invitation is
        // accepted, and the unverified email refuses a login anyway.
        var unusable = passwordHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var user = User.CreateInvitedEmployee(email.Value, phone.Value, name.Value, PasswordHash.FromHash(unusable), now);
        await users.AddAsync(user, cancellationToken);

        var invitation = opaqueTokens.Generate();
        await verificationTokens.AddAsync(
            VerificationToken.Issue(user.Id, VerificationPurpose.EmployeeInvitation, invitation.Hash, now, policy.EmployeeInvitationLifetime),
            cancellationToken);

        return new ProvisionedEmployee(user, invitation.Value);
    }

    /// <summary>A fresh invitation for someone who has not accepted yet; every older one dies.</summary>
    public async Task<string> ReissueInvitationAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = clock.UtcNow;
        await verificationTokens.InvalidateActiveAsync(user.Id, VerificationPurpose.EmployeeInvitation, now, cancellationToken);

        var invitation = opaqueTokens.Generate();
        await verificationTokens.AddAsync(
            VerificationToken.Issue(user.Id, VerificationPurpose.EmployeeInvitation, invitation.Hash, now, policy.EmployeeInvitationLifetime),
            cancellationToken);

        return invitation.Value;
    }
}
