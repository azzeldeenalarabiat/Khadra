using System.Security.Cryptography;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Microsoft.Extensions.Logging;

namespace Khadra.Application.IdentityAccess.AdminUsers;

/// <summary>
/// How the platform's FIRST administrator comes into existence.
///
/// Every other administrator is invited by one who is already signed in (<c>InviteAdminCommand</c>),
/// and the "last administrator" guard means the set can never empty. That is the right rule and it
/// leaves one hole: on a database that has never had an administrator, nobody can create the first,
/// because creating one requires being one. Until now the only thing that filled that hole was the
/// development seeder inventing accounts, which is not a way to run a real platform.
///
/// This runs at startup, once per database, and issues an INVITATION — the same recipe as
/// <c>InviteAdminCommand</c> minus the actor. The account it creates cannot be signed into: the
/// password hash is random bytes nobody keeps and the address is unverified, so
/// <c>User.CanAuthenticate</c> refuses it whatever anyone guesses. It becomes usable only when the
/// person holding the mailbox opens the emailed link and chooses a password.
///
/// That shape is what makes a configured address safe. On any platform that already has an
/// administrator this is inert, so it cannot be used to add one; on a database that has none,
/// whoever sets the configuration already holds the connection string and the JWT signing key, and
/// could mint any account they liked regardless.
/// </summary>
public sealed partial class AdminBootstrapper(
    IUserRepository users,
    IVerificationTokenRepository verificationTokens,
    IPasswordHasher passwordHasher,
    IOpaqueTokenService opaqueTokens,
    IAuthPolicySettings policy,
    IAdminBootstrapSettings settings,
    IEmailSender email,
    IAuthEmailComposer composer,
    IAuditTrail auditTrail,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<AdminBootstrapper> logger)
{
    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        // Nothing configured: return before touching the database. The API smoke tests boot the real
        // pipeline with no reachable Postgres behind it, and this must not be what breaks them.
        if (string.IsNullOrWhiteSpace(settings.Email))
            return;

        // A misconfigured bootstrap fails startup rather than doing nothing quietly. Silence here
        // would leave a platform with no administrator and nobody aware of it until someone tried to
        // sign in — by which time the database is no longer empty and this will never run again.
        var address = EmailAddress.Create(settings.Email);
        var phone = PhoneNumber.Create(settings.Phone ?? string.Empty);
        var name = PersonName.Create(settings.FullName ?? string.Empty);
        if (address.IsFailure || phone.IsFailure || name.IsFailure)
        {
            throw new InvalidOperationException(
                "Admin:Bootstrap is configured but not valid: " +
                string.Join(
                    " ",
                    new[] { address.IsFailure ? address.Error.Message : null, phone.IsFailure ? phone.Error.Message : null, name.IsFailure ? name.Error.Message : null }
                        .Where(message => message is not null)));
        }

        var now = clock.UtcNow;

        if (await users.AnyAdminExistsAsync(cancellationToken))
        {
            await ReissueIfStrandedAsync(address.Value, now, cancellationToken);
            return;
        }

        // The same unusable credential InviteAdminCommand creates: bytes hashed and discarded, so
        // there is no password until the invitation is accepted.
        var unusable = passwordHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var invited = User.CreateInvitedAdmin(
            address.Value, phone.Value, name.Value, PasswordHash.FromHash(unusable), now);
        await users.AddAsync(invited, cancellationToken);

        var invitation = opaqueTokens.Generate();
        await verificationTokens.AddAsync(
            VerificationToken.Issue(
                invited.Id,
                VerificationPurpose.AdminInvitation,
                invitation.Hash,
                now,
                policy.EmployeeInvitationLifetime),
            cancellationToken);

        // BySystem, not By: there is no actor, which is the whole point of this code path.
        auditTrail.Record(AuditEntry.BySystem(
            AuditAction.AdminInvited,
            AuditEntityType.AdminUser,
            invited.Id,
            invited.Name.Value,
            now,
            newValue: UserRole.Admin.Name,
            reason: "Configured bootstrap administrator: the platform had none."));

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Two replicas starting against the same empty database both see "no administrator" and
            // both insert; the unique index on users.email lets exactly one win. Losing that race is
            // the system working, not a reason to refuse to start.
            LogRaceLost(logger, exception);
            return;
        }

        // After the commit: an email promising an account that failed to save is worse than none.
        await email.SendAsync(composer.AdminInvitation(invited, invitation.Value), cancellationToken);
        LogInvited(logger, invited.Email.Value);
    }

    /// <summary>
    /// The lockout this method exists to prevent.
    ///
    /// The first invitation expires after <see cref="IAuthPolicySettings.EmployeeInvitationLifetime"/>.
    /// If nobody accepted it, the account row still satisfies "an administrator exists", so the
    /// branch above never runs again — and nothing else can help: no administrator can sign in to
    /// re-invite, and a password reset cannot rescue it either, because <c>CanAuthenticate</c> still
    /// refuses an unverified address. The platform would be locked out of its own console with no
    /// route back except editing the database by hand.
    ///
    /// So: if the configured address is an administrator who has never proved their mailbox, is not
    /// suspended, and has no live link outstanding, send another one. Anything else — a verified
    /// administrator, a suspended one, a live link still in the recipient's inbox, a different
    /// address entirely — is left alone.
    /// </summary>
    private async Task ReissueIfStrandedAsync(
        EmailAddress address,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await users.GetByEmailAsync(address, cancellationToken);
        if (existing is null
            || existing.Role != UserRole.Admin
            || existing.IsEmailVerified
            || existing.Status != UserStatus.Active)
        {
            return;
        }

        if (await verificationTokens.HasActiveAsync(
                existing.Id, VerificationPurpose.AdminInvitation, now, cancellationToken))
        {
            return;
        }

        var invitation = opaqueTokens.Generate();
        await verificationTokens.AddAsync(
            VerificationToken.Issue(
                existing.Id,
                VerificationPurpose.AdminInvitation,
                invitation.Hash,
                now,
                policy.EmployeeInvitationLifetime),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await email.SendAsync(composer.AdminInvitation(existing, invitation.Value), cancellationToken);
        LogReissued(logger, existing.Email.Value);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Bootstrap administrator invited: {Email}. The account does nothing until the emailed link is accepted.")]
    private static partial void LogInvited(ILogger logger, string email);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The bootstrap administrator {Email} never accepted their invitation and it has expired. A new one has been sent.")]
    private static partial void LogReissued(ILogger logger, string email);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The bootstrap administrator was created by another instance; this one did nothing.")]
    private static partial void LogRaceLost(ILogger logger, Exception exception);
}
