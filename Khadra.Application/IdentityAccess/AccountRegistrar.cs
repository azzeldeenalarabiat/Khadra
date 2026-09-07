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
/// Both flows validate the same fields, enforce the same uniqueness and send the same verification
/// email. Keeping that in one place means the two paths cannot drift apart on something like account
/// enumeration or which failure is reported first, which is exactly the sort of divergence that
/// turns into a security bug.
///
/// The minimum age is the one rule they do NOT share, and it is a parameter for that reason. Spec
/// 5.1's age limit is about who may RENT a car; it was being applied to the person who owns the
/// rental office, so a 21-year-old bound was refusing gallery owners and telling them "Renters must
/// be at least 21 years old" — a rule they are not subject to, in words that do not describe them.
/// Whoever an owner's age matters to, it is the administrator reading their identity document at
/// licence review, not this handler.
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

    /// <param name="enforceMinimumAge">
    /// Whether the configured renter minimum age applies to this registration. True for a customer,
    /// false for a gallery owner: see the class remarks.
    /// </param>
    public async Task<Result<RegisteredUserDto, Error>> RegisterAsync(
        string rawEmail,
        string rawPhone,
        string rawName,
        string rawPassword,
        DateOnly? dateOfBirth,
        bool enforceMinimumAge,
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
        if (enforceMinimumAge)
        {
            var rules = await businessRules.GetAsync(cancellationToken);
            // Age is counted against the local calendar day in Jordan, not UTC: someone who turns 21
            // today should not be refused because it is still yesterday in Greenwich.
            var age = RenterAgePolicy.Validate(dateOfBirth, rules.MinimumRenterAge, calendar.Today(now));
            if (age.IsFailure)
                return age.Error;
        }

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
        // A mail failure must not undo a completed registration — the account and the token are
        // already committed — but it is carried back to the caller so the screen can say so instead
        // of promising an email that nobody sent.
        var delivered = await emails.SendEmailVerificationAsync(user, rawToken.Value, cancellationToken);

        return new RegisteredUserDto(user.Id, user.Email.Value, delivered);
    }
}
