using CSharpFunctionalExtensions;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using MediatR;

namespace Khadra.Application.IdentityAccess.RegisterCustomer;

public sealed class RegisterCustomerHandler(
    IUserRepository users,
    IVerificationTokenRepository verificationTokens,
    IPasswordHasher passwordHasher,
    IOpaqueTokenService opaqueTokens,
    IAuthPolicySettings policy,
    IClock clock,
    IUnitOfWork unitOfWork,
    AuthEmailDispatcher emails)
    : IRequestHandler<RegisterCustomerCommand, Result<RegisteredUserDto, Error>>
{
    public async Task<Result<RegisteredUserDto, Error>> Handle(
        RegisterCustomerCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = EmailAddress.Create(request.Email);
        if (email.IsFailure)
            return email.Error;

        var phone = PhoneNumber.Create(request.Phone);
        if (phone.IsFailure)
            return phone.Error;

        var name = PersonName.Create(request.FullName);
        if (name.IsFailure)
            return name.Error;

        var password = PasswordPolicy.Validate(request.Password, policy.PasswordMinimumLength);
        if (password.IsFailure)
            return password.Error;

        if (await users.ExistsByEmailAsync(email.Value, cancellationToken))
            return IdentityErrors.EmailTaken;
        if (await users.ExistsByPhoneAsync(phone.Value, cancellationToken))
            return IdentityErrors.PhoneTaken;

        var now = clock.UtcNow;
        var user = User.RegisterCustomer(
            email.Value,
            phone.Value,
            name.Value,
            PasswordHash.FromHash(passwordHasher.Hash(request.Password)),
            now);
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
        await emails.SendEmailVerificationAsync(user, rawToken.Value, cancellationToken);

        return new RegisteredUserDto(user.Id, user.Email.Value);
    }
}
