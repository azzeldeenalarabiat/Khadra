using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess.Events;

namespace Khadra.Domain.IdentityAccess;

// Identity & Access aggregate. Owns credentials, role, account state and email verification.
// Dealer/customer profile data lives in their own contexts and references this aggregate by Id.
public sealed class User : AggregateRoot, ISoftDeletable
{
    public EmailAddress Email { get; private set; } = null!;
    public PhoneNumber Phone { get; private set; } = null!;
    public PersonName Name { get; private set; } = null!;
    public PasswordHash PasswordHash { get; private set; } = null!;
    public UserRole Role { get; private set; } = null!;
    public UserStatus Status { get; private set; } = null!;
    public bool IsEmailVerified { get; private set; }
    public DateTimeOffset? EmailVerifiedAt { get; private set; }
    // Set for accounts created with a temporary password (employees). Cleared on first change.
    public bool MustChangePassword { get; private set; }
    // Rotated on password change, suspension and deletion so already-issued JWTs stop validating.
    public Guid SecurityStamp { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset? PasswordChangedAt { get; private set; }
    public string? SuspensionReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private User()
    {
    }

    private User(Id id) : base(id)
    {
    }

    // Self-service registration from the mobile app. Email must be verified before sign-in.
    public static User RegisterCustomer(
        EmailAddress email,
        PhoneNumber phone,
        PersonName name,
        PasswordHash passwordHash,
        DateTimeOffset now) =>
        Create(email, phone, name, passwordHash, UserRole.Customer, isEmailVerified: false, mustChangePassword: false, now);

    // Dealer owner self-registration (Dealers context submits the business documents separately).
    public static User RegisterDealerOwner(
        EmailAddress email,
        PhoneNumber phone,
        PersonName name,
        PasswordHash passwordHash,
        DateTimeOffset now) =>
        Create(email, phone, name, passwordHash, UserRole.DealerOwner, isEmailVerified: false, mustChangePassword: false, now);

    // Employees are never self-registered: the owner creates them with a temporary password.
    public static User CreateEmployee(
        EmailAddress email,
        PhoneNumber phone,
        PersonName name,
        PasswordHash temporaryPasswordHash,
        DateTimeOffset now) =>
        Create(email, phone, name, temporaryPasswordHash, UserRole.DealerEmployee, isEmailVerified: true, mustChangePassword: true, now);

    // Platform administrators are seeded or created by another admin, never self-registered.
    public static User CreateAdmin(
        EmailAddress email,
        PhoneNumber phone,
        PersonName name,
        PasswordHash passwordHash,
        DateTimeOffset now) =>
        Create(email, phone, name, passwordHash, UserRole.Admin, isEmailVerified: true, mustChangePassword: false, now);

    private static User Create(
        EmailAddress email,
        PhoneNumber phone,
        PersonName name,
        PasswordHash passwordHash,
        UserRole role,
        bool isEmailVerified,
        bool mustChangePassword,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(phone);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(passwordHash);
        ArgumentNullException.ThrowIfNull(role);

        var user = new User(Id.New())
        {
            Email = email,
            Phone = phone,
            Name = name,
            PasswordHash = passwordHash,
            Role = role,
            Status = UserStatus.Active,
            IsEmailVerified = isEmailVerified,
            EmailVerifiedAt = isEmailVerified ? now : null,
            MustChangePassword = mustChangePassword,
            SecurityStamp = Guid.NewGuid(),
            CreatedAt = now
        };
        user.AddDomainEvent(new UserRegistered(user.Id, email.Value, role.Name, now));
        return user;
    }

    // Why the account may not sign in right now. Deleted accounts look like bad credentials on purpose.
    public UnitResult<Error> CanAuthenticate()
    {
        if (IsDeleted)
            return UnitResult.Failure(IdentityErrors.InvalidCredentials);
        if (Status == UserStatus.Suspended)
            return UnitResult.Failure(IdentityErrors.AccountSuspended);
        if (!IsEmailVerified)
            return UnitResult.Failure(IdentityErrors.EmailNotVerified);

        return UnitResult.Success<Error>();
    }

    public bool MatchesSecurityStamp(Guid stamp) => SecurityStamp == stamp;

    // Idempotent: verifying twice is not an error (the user may click the link again).
    public void VerifyEmail(DateTimeOffset now)
    {
        if (IsEmailVerified)
            return;

        IsEmailVerified = true;
        EmailVerifiedAt = now;
        AddDomainEvent(new UserEmailVerified(Id, now));
    }

    public void RecordSuccessfulLogin(DateTimeOffset now) => LastLoginAt = now;

    public void ChangePassword(PasswordHash newPasswordHash, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(newPasswordHash);

        PasswordHash = newPasswordHash;
        PasswordChangedAt = now;
        MustChangePassword = false;
        SecurityStamp = Guid.NewGuid();
        AddDomainEvent(new UserPasswordChanged(Id, now));
    }

    public UnitResult<Error> Suspend(string reason, DateTimeOffset now)
    {
        if (Status == UserStatus.Suspended)
            return UnitResult.Failure(IdentityErrors.AlreadySuspended);

        Status = UserStatus.Suspended;
        SuspensionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        SecurityStamp = Guid.NewGuid();
        AddDomainEvent(new UserSuspended(Id, SuspensionReason ?? string.Empty, now));
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Reactivate(DateTimeOffset now)
    {
        if (Status != UserStatus.Suspended)
            return UnitResult.Failure(IdentityErrors.NotSuspended);

        Status = UserStatus.Active;
        SuspensionReason = null;
        AddDomainEvent(new UserReactivated(Id, now));
        return UnitResult.Success<Error>();
    }

    // Soft delete (account deletion request, spec §7). Sessions die through the rotated stamp + event.
    public UnitResult<Error> Delete(DateTimeOffset now)
    {
        if (IsDeleted)
            return UnitResult.Failure(IdentityErrors.AlreadyDeleted);

        IsDeleted = true;
        DeletedAt = now;
        SecurityStamp = Guid.NewGuid();
        AddDomainEvent(new UserDeleted(Id, now));
        return UnitResult.Success<Error>();
    }
}
