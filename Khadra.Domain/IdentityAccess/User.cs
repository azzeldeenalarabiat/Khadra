using CSharpFunctionalExtensions;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess.Events;

namespace Khadra.Domain.IdentityAccess;

// Identity & Access aggregate. Owns credentials, role, account state and email verification.
// Dealer/customer profile data lives in their own contexts and references this aggregate by Id.
public sealed class User : AggregateRoot, ISoftDeletable
{
    private readonly List<CustomerDocument> _documents = [];

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
    // Spec 5.1 and 6: needed for the minimum-age check at registration. Nullable because accounts
    // created before the rule existed, and admin/employee accounts, have no renter age to check.
    public DateOnly? DateOfBirth { get; private set; }
    // Spec 5.1: a foreign renter presents a passport, and possibly an international driving permit
    // once that requirement is decided (spec 2.2, still open).
    public bool IsForeignNational { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public IReadOnlyCollection<CustomerDocument> Documents => _documents.AsReadOnly();

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
        DateTimeOffset now,
        DateOnly? dateOfBirth = null,
        bool isForeignNational = false) =>
        Create(email, phone, name, passwordHash, UserRole.Customer, isEmailVerified: false, mustChangePassword: false, now,
            dateOfBirth, isForeignNational);

    // Dealer owner self-registration (Dealers context submits the business documents separately).
    public static User RegisterDealerOwner(
        EmailAddress email,
        PhoneNumber phone,
        PersonName name,
        PasswordHash passwordHash,
        DateTimeOffset now,
        DateOnly? dateOfBirth = null) =>
        Create(email, phone, name, passwordHash, UserRole.DealerOwner, isEmailVerified: false, mustChangePassword: false, now,
            dateOfBirth, isForeignNational: false);

    // Employees are never self-registered: the owner creates them with a temporary password.
    public static User CreateEmployee(
        EmailAddress email,
        PhoneNumber phone,
        PersonName name,
        PasswordHash temporaryPasswordHash,
        DateTimeOffset now) =>
        Create(email, phone, name, temporaryPasswordHash, UserRole.DealerEmployee, isEmailVerified: true, mustChangePassword: true, now);

    /// <summary>
    /// An employee who has been INVITED but has not yet accepted (spec 4.2's "invite link").
    ///
    /// The account is inert until they do: the password hash is unusable (the caller hashes random
    /// bytes it then discards) and the email is unverified, so CanAuthenticate refuses a login
    /// whatever anyone guesses. Accepting the invitation verifies the address -- which nobody has yet
    /// proved they own -- and sets the first real password in one step.
    /// </summary>
    public static User CreateInvitedEmployee(
        EmailAddress email,
        PhoneNumber phone,
        PersonName name,
        PasswordHash unusablePasswordHash,
        DateTimeOffset now) =>
        Create(email, phone, name, unusablePasswordHash, UserRole.DealerEmployee, isEmailVerified: false, mustChangePassword: false, now);

    /// <summary>An administrator invited by another, whose account does nothing until they accept.</summary>
    /// <remarks>
    /// Mirrors <see cref="CreateInvitedEmployee"/>: an unusable password hash and an unverified
    /// email, so the account cannot be signed into until the one-time link proves the address. The
    /// alternative — creating them with a temporary password — puts a working credential into an
    /// email nobody controls.
    /// </remarks>
    public static User CreateInvitedAdmin(
        EmailAddress email,
        PhoneNumber phone,
        PersonName name,
        PasswordHash unusablePasswordHash,
        DateTimeOffset now) =>
        Create(email, phone, name, unusablePasswordHash, UserRole.Admin, isEmailVerified: false, mustChangePassword: false, now);

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
        DateTimeOffset now,
        DateOnly? dateOfBirth = null,
        bool isForeignNational = false)
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
            DateOfBirth = dateOfBirth,
            IsForeignNational = isForeignNational,
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

    /// <summary>
    /// Records an uploaded identity document.
    ///
    /// Re-uploading a type REPLACES the previous file, because spec 5.1 expects a customer whose
    /// document was unreadable to send a better photo rather than accumulate rejected attempts. The
    /// caller is responsible for deleting the superseded blob; the returned key says which.
    /// </summary>
    public string? AttachDocument(
        CustomerDocumentType type,
        string storageKey,
        string contentType,
        long sizeBytes,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(type);

        var existing = _documents.SingleOrDefault(document => document.Type == type);
        if (existing is not null)
            return existing.Replace(storageKey, contentType, sizeBytes, now);

        _documents.Add(CustomerDocument.Attach(Id, type, storageKey, contentType, sizeBytes, now));
        return null;
    }

    /// <summary>
    /// Spec 5.1: both sides of a licence, plus one identity document -- a national ID for a local
    /// renter, a passport for a foreign one. Whether a foreign renter additionally needs an
    /// international driving permit is still an open owner decision (spec 2.2), so it is not required
    /// here and this will need revisiting once that is settled.
    /// </summary>
    public bool HasCompleteRenterDocuments =>
        _documents.Any(document => document.Type == CustomerDocumentType.DrivingLicenceFront) &&
        _documents.Any(document => document.Type == CustomerDocumentType.DrivingLicenceBack) &&
        _documents.Any(document => document.Type.IsIdentity);

    public CustomerDocument? FindDocument(Id documentId) =>
        _documents.SingleOrDefault(document => document.Id == documentId);

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

    /// <summary>
    /// Corrects the person's own name and phone number.
    /// </summary>
    /// <remarks>
    /// EMAIL IS NOT HERE, and that is not an omission. It is the sign-in identifier and the address a
    /// password reset is sent to, so moving it on the strength of a live session alone would let
    /// anyone who borrowed an unlocked phone take the account permanently. Changing it needs its own
    /// verified flow (pre-launch checklist item 44), and until that exists this endpoint must not
    /// pretend otherwise.
    ///
    /// Date of birth and foreign-national status are likewise absent: they were the inputs to
    /// RenterAgePolicy at registration and the uploaded documents are the evidence for them, so a
    /// customer editing them freely would be editing the answer to a check the platform already made.
    ///
    /// The security stamp is deliberately NOT rotated. Rotating it signs the person out of every
    /// device, and correcting a misspelt name is not a security event.
    /// </remarks>
    public void UpdateContactDetails(PersonName name, PhoneNumber phone)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(phone);

        Name = name;
        Phone = phone;
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

    /// <summary>
    /// Ends every session this person has, without changing anything else about the account.
    ///
    /// This is what a dealer owner deactivating an employee needs (spec 4.2: "their login stops
    /// working immediately"). Suspend is deliberately NOT used for that: it is the Admin's sanction,
    /// it would show the person as platform-suspended in admin screens, and lifting it is the Admin's
    /// tool. Rotating the stamp kills access tokens on their next request; the event revokes the
    /// refresh-token families after commit.
    /// </summary>
    public void RevokeAllSessions(DateTimeOffset now)
    {
        SecurityStamp = Guid.NewGuid();
        AddDomainEvent(new UserSessionsRevoked(Id, now));
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
