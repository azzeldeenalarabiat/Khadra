using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;

namespace Khadra.Application.IdentityAccess;

/// <summary>
/// The self-service registration a customer and a dealer owner share.
///
/// Both flows validate the same fields, enforce the same uniqueness, apply the same minimum-age rule
/// and send the same verification email; only the role differs. Keeping that in one place means the
/// two paths cannot drift apart on something like account enumeration or which failure is reported
/// first, which is exactly the sort of divergence that turns into a security bug.
/// </summary>
public sealed class AccountRegistrar(
    IUserRepository users,
    IVerificationTokenRepository verificationTokens,
    IPasswordHasher passwordHasher,
    IOpaqueTokenService opaqueTokens,
    IAuthPolicySettings policy,
    IBusinessRulesProvider businessRules,
    IReportingCalendar calendar,
    IClock clock,
    IUnitOfWork unitOfWork,
    AuthEmailDispatcher emails)
{
    public delegate User CreateUser(
        EmailAddress email,
        PhoneNumber phone,
        PersonName name,
        PasswordHash passwordHash,
        DateTimeOffset now,
        DateOnly? dateOfBirth);

    public async Task<Result<RegisteredUserDto, Error>> RegisterAsync(
        string rawEmail,
        string rawPhone,
        string rawName,
        string rawPassword,
        DateOnly? dateOfBirth,
        CreateUser create,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(create);

        var email = EmailAddress.Create(rawEmail);
        if (email.IsFailure)
            return email.Error;

        var phone = PhoneNumber.Create(rawPhone);
        if (phone.IsFailure)
            return phone.Error;

        var name = PersonName.Create(rawName);
        if (name.IsFailure)
            return name.Error;

        var password = PasswordPolicy.Validate(rawPassword, policy.PasswordMinimumLength);
        if (password.IsFailure)
            return password.Error;

        var now = clock.UtcNow;
        var rules = await businessRules.GetAsync(cancellationToken);
        // Age is counted against the local calendar day in Jordan, not UTC: someone who turns 21
        // today should not be refused because it is still yesterday in Greenwich.
        var age = RenterAgePolicy.Validate(dateOfBirth, rules.MinimumRenterAge, calendar.Today(now));
        if (age.IsFailure)
            return age.Error;

        if (await users.ExistsByEmailAsync(email.Value, cancellationToken))
            return IdentityErrors.EmailTaken;
        if (await users.ExistsByPhoneAsync(phone.Value, cancellationToken))
            return IdentityErrors.PhoneTaken;

        var user = create(
            email.Value,
            phone.Value,
            name.Value,
            PasswordHash.FromHash(passwordHasher.Hash(rawPassword)),
            now,
            dateOfBirth);
        await users.AddAsync(user, cancellationToken);

        var rawToken = opaqueTokens.Generate();
        var verification = VerificationToken.Issue(
            user.Id,
            VerificationPurpose.EmailVerification,
            rawToken.Hash,
            now,
            policy.EmailVerificationLifetime);
        await verificationTokens.AddAsync(verification, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        // A mail failure must not undo a completed registration; the dispatcher logs and swallows.
        await emails.SendEmailVerificationAsync(user, rawToken.Value, cancellationToken);

        return new RegisteredUserDto(user.Id, user.Email.Value);
    }
}
