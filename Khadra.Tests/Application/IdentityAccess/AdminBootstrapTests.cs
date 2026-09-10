using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess;
using Khadra.Application.IdentityAccess.AdminUsers;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Khadra.Tests.Application.IdentityAccess;

// How the platform's FIRST administrator comes to exist.
//
// AdminUserTests holds the other half of this rule: nothing creates an administrator except another
// administrator, and the last one can never be deactivated. That leaves exactly one hole — a database
// that has never had one — and this is what fills it, ONCE, by invitation. These tests are what stop
// it becoming a standing back door in either direction: creating a second administrator on a platform
// that already has one, or leaving a platform locked out because the only invitation expired unread.
public sealed class AdminBootstrapTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

    private sealed record Settings(string? Email, string? Phone, string? FullName) : IAdminBootstrapSettings
    {
        public static Settings Configured { get; } = new("founder@khadra.jo", "0790000001", "Rania Haddad");

        public static Settings None { get; } = new(null, null, null);
    }

    private sealed class Context
    {
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IVerificationTokenRepository Tokens { get; } = Substitute.For<IVerificationTokenRepository>();
        public IEmailSender Email { get; } = Substitute.For<IEmailSender>();
        public IAuthEmailComposer Composer { get; } = Substitute.For<IAuthEmailComposer>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IAuditTrail AuditTrail { get; } = Substitute.For<IAuditTrail>();
        public FakePasswordHasher Hasher { get; } = new();
        public FakeOpaqueTokens OpaqueTokens { get; } = new();
        public TestClock Clock { get; } = new(Now);
        public List<User> Added { get; } = [];
        public List<AuditEntry> Recorded { get; } = [];
        public List<VerificationToken> Issued { get; } = [];
        // Recording, because several branches here do nothing on purpose, and "did nothing and said
        // why" is the behaviour under test -- a silent decline is what leaves an operator with no
        // way to tell a working bootstrap from an unread setting.
        public RecordingLogger<AdminBootstrapper> Log { get; } = new();

        public Context()
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            Users.When(repo => repo.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()))
                .Do(call => Added.Add(call.Arg<User>()));
            Tokens.When(repo => repo.AddAsync(Arg.Any<VerificationToken>(), Arg.Any<CancellationToken>()))
                .Do(call => Issued.Add(call.Arg<VerificationToken>()));
            AuditTrail.When(trail => trail.Record(Arg.Any<AuditEntry>()))
                .Do(call => Recorded.Add(call.Arg<AuditEntry>()));
            Composer.AdminInvitation(Arg.Any<User>(), Arg.Any<string>())
                .Returns(new EmailMessage("to@khadra.jo", "Name", "Subject", "<p>html</p>", "text"));
        }

        public AdminBootstrapper Bootstrapper(IAdminBootstrapSettings settings) => new(
            Users,
            Tokens,
            Hasher,
            OpaqueTokens,
            TestAuthPolicy.Default,
            settings,
            new AuthEmailDispatcher(Composer, Email, NullLogger<AuthEmailDispatcher>.Instance),
            AuditTrail,
            UnitOfWork,
            Clock,
            Log);
    }

    private static User InvitedFounder() =>
        User.CreateInvitedAdmin(
            EmailAddress.Create("founder@khadra.jo").Value,
            PhoneNumber.Create("0790000001").Value,
            PersonName.Create("Rania Haddad").Value,
            PasswordHash.FromHash("unusable"),
            Now.AddDays(-30));

    [Fact]
    public async Task An_empty_platform_gets_its_first_administrator_by_invitation()
    {
        var context = new Context();
        context.Users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(false);

        await context.Bootstrapper(Settings.Configured).EnsureAsync();

        var invited = Assert.Single(context.Added);
        Assert.Equal("founder@khadra.jo", invited.Email.Value);
        Assert.Equal(UserRole.Admin, invited.Role);

        var token = Assert.Single(context.Issued);
        Assert.Equal(VerificationPurpose.AdminInvitation, token.Purpose);
        Assert.Equal(invited.Id, token.UserId);

        await context.Email.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The account is inert until the emailed link is accepted.
    ///
    /// This is what makes a configured address safe to ship. The hash is over bytes the bootstrapper
    /// generated and then threw away, and the address is unverified, so CanAuthenticate refuses a
    /// sign-in on both counts. Nothing anybody can read out of configuration is a credential.
    /// </summary>
    [Fact]
    public async Task The_bootstrapped_account_cannot_be_signed_into_until_it_is_accepted()
    {
        var context = new Context();
        context.Users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(false);

        await context.Bootstrapper(Settings.Configured).EnsureAsync();

        var invited = Assert.Single(context.Added);
        Assert.False(invited.IsEmailVerified);
        Assert.True(invited.CanAuthenticate().IsFailure);
        // Whatever was hashed, it was not anything the configuration contains.
        Assert.DoesNotContain("founder@khadra.jo", invited.PasswordHash.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_first_administrator_is_recorded_in_the_audit_trail_with_no_actor()
    {
        var context = new Context();
        context.Users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(false);

        await context.Bootstrapper(Settings.Configured).EnsureAsync();

        var entry = Assert.Single(context.Recorded);
        Assert.Equal(AuditAction.AdminInvited, entry.Action);
        Assert.Equal(AuditEntityType.AdminUser, entry.EntityType);
        // BySystem, because there is nobody to attribute it to — that is the whole point of this path.
        Assert.Null(entry.ActorUserId);
    }

    [Fact]
    public async Task Nothing_happens_when_no_bootstrap_is_configured()
    {
        var context = new Context();

        await context.Bootstrapper(Settings.None).EnsureAsync();

        // Not even the question is asked: the API smoke tests boot this pipeline with no database
        // behind it, and an unconfigured bootstrap must not be what makes startup touch one.
        await context.Users.DidNotReceive().AnyAdminExistsAsync(Arg.Any<CancellationToken>());
        Assert.Empty(context.Added);
    }

    /// <summary>
    /// A platform that already has an administrator is never given another one — and "has" is
    /// counted broadly, so suspending or deleting the incumbent does not reopen the door.
    /// </summary>
    [Fact]
    public async Task A_platform_that_already_has_an_administrator_is_left_alone()
    {
        var context = new Context();
        context.Users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(true);
        context.Users.GetByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        await context.Bootstrapper(Settings.Configured).EnsureAsync();

        Assert.Empty(context.Added);
        Assert.Empty(context.Issued);
        await context.Email.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The lockout this exists to prevent.
    ///
    /// The first invitation expired unread. The account row still satisfies "an administrator
    /// exists", so nothing creates another; no administrator can sign in to re-invite, because the
    /// address was never verified; and a password reset cannot help for the same reason. Without
    /// this branch the platform is shut out of its own console permanently.
    /// </summary>
    [Fact]
    public async Task An_invitation_that_expired_unaccepted_is_sent_again()
    {
        var context = new Context();
        var stranded = InvitedFounder();
        context.Users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(true);
        context.Users.GetByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>()).Returns(stranded);
        context.Tokens.HasActiveAsync(
                stranded.Id, VerificationPurpose.AdminInvitation, Now, Arg.Any<CancellationToken>())
            .Returns(false);

        await context.Bootstrapper(Settings.Configured).EnsureAsync();

        // A new link for the SAME account: no second administrator is created.
        Assert.Empty(context.Added);
        var token = Assert.Single(context.Issued);
        Assert.Equal(stranded.Id, token.UserId);
        await context.Email.Received(1).SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_live_invitation_is_not_replaced_by_a_restart()
    {
        var context = new Context();
        var stranded = InvitedFounder();
        context.Users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(true);
        context.Users.GetByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>()).Returns(stranded);
        context.Tokens.HasActiveAsync(
                stranded.Id, VerificationPurpose.AdminInvitation, Now, Arg.Any<CancellationToken>())
            .Returns(true);

        await context.Bootstrapper(Settings.Configured).EnsureAsync();

        // Restarting the API twice in a minute must not invalidate the link sitting in their inbox.
        Assert.Empty(context.Issued);
        await context.Email.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_administrator_who_has_accepted_is_never_re_invited()
    {
        var context = new Context();
        var accepted = User.CreateAdmin(
            EmailAddress.Create("founder@khadra.jo").Value,
            PhoneNumber.Create("0790000001").Value,
            PersonName.Create("Rania Haddad").Value,
            PasswordHash.FromHash("hash"),
            Now.AddDays(-30));
        context.Users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(true);
        context.Users.GetByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>()).Returns(accepted);

        await context.Bootstrapper(Settings.Configured).EnsureAsync();

        Assert.Empty(context.Issued);
        await context.Email.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A suspended administrator is not rescued by editing configuration.
    ///
    /// Otherwise the bootstrap would be a way around a decision their colleagues took: suspend the
    /// account, restart the API, and it invites itself back in.
    /// </summary>
    [Fact]
    public async Task A_suspended_administrator_is_not_re_invited()
    {
        var context = new Context();
        var suspended = InvitedFounder();
        suspended.Suspend("Under investigation.", Now.AddDays(-1));
        context.Users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(true);
        context.Users.GetByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>()).Returns(suspended);

        await context.Bootstrapper(Settings.Configured).EnsureAsync();

        Assert.Empty(context.Issued);
        await context.Email.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_bootstrap_that_is_configured_but_invalid_stops_startup()
    {
        var context = new Context();
        context.Users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(false);
        var nonsense = new Settings("not-an-email", "not-a-phone", "");

        // Loudly, not quietly: a bootstrap that silently did nothing would leave a platform with no
        // administrator and nobody aware of it until the database was no longer empty.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Bootstrapper(nonsense).EnsureAsync());
        Assert.Empty(context.Added);
    }

    /// <summary>
    /// Two replicas starting against the same empty database both see "no administrator" and both
    /// insert; the unique index on users.email lets one win. Losing that race is the system working.
    /// </summary>
    [Fact]
    public async Task Losing_the_race_to_another_instance_does_not_stop_startup()
    {
        var context = new Context();
        context.Users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(false);
        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("duplicate key value violates unique constraint"));

        await context.Bootstrapper(Settings.Configured).EnsureAsync();

        // No exception, and no email promising an account that was not saved.
        await context.Email.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }
    /// <summary>
    /// A relay that refuses the invitation must not take the process down with it, or strand the
    /// platform for a week.
    ///
    /// Startup awaits EnsureAsync with no catch of its own, and does so BEFORE the line that reports
    /// whether mail works, so an unhandled send meant a first boot against a misconfigured relay died
    /// silently — after committing the administrator and its token. The restart was the real damage:
    /// the row now satisfies "an administrator exists", and ReissueIfStrandedAsync declines while a
    /// live token is outstanding, so nobody could get in until it expired seven days later.
    ///
    /// Retiring the token on a refused send turns that week into the next restart.
    /// </summary>
    [Fact]
    public async Task An_invitation_the_mail_server_refused_retires_its_token_instead_of_killing_startup()
    {
        var context = new Context();
        context.Users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(false);
        context.Email.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("The mail server refused the message."));

        // Startup does not get an exception...
        await context.Bootstrapper(Settings.Configured).EnsureAsync();

        // ...the administrator and the audit entry still stand...
        var invited = Assert.Single(context.Added);
        Assert.Single(context.Recorded);

        // ...and the link nobody received was retired, so the next start issues another rather than
        // seeing a live token and doing nothing.
        await context.Tokens.Received(1).InvalidateActiveAsync(
            invited.Id,
            VerificationPurpose.AdminInvitation,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }
}

/// <summary>
/// The two ways a configured bootstrap can decline, and what a first login actually requires.
/// </summary>
/// <remarks>
/// Written while creating the first administrator on a live deployment. Both cases below produce a
/// platform with no usable administrator, and before this neither said anything: the operator sets
/// the variable, restarts, and no line in the log so much as mentions the subject — so the only
/// conclusion available is that the setting was not read, which is the wrong problem.
/// </remarks>
public sealed class AdminBootstrapRefusalTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);

    private sealed record BootstrapSettings(string? Email, string? Phone, string? FullName)
        : IAdminBootstrapSettings;

    /// <summary>The collaborators, with the two the refusal paths actually turn on left to the test.</summary>
    private sealed class AdminBootstrapTestBed
    {
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IVerificationTokenRepository Tokens { get; } = Substitute.For<IVerificationTokenRepository>();
        public IEmailSender Email { get; } = Substitute.For<IEmailSender>();
        public IAuthEmailComposer Composer { get; } = Substitute.For<IAuthEmailComposer>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IAuditTrail AuditTrail { get; } = Substitute.For<IAuditTrail>();
        public RecordingLogger<AdminBootstrapper> Log { get; } = new();
        public List<User> Added { get; } = [];

        public AdminBootstrapTestBed()
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            Users.When(repo => repo.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()))
                .Do(call => Added.Add(call.Arg<User>()));
            Composer.AdminInvitation(Arg.Any<User>(), Arg.Any<string>())
                .Returns(new EmailMessage("to@khadra.jo", "Name", "Subject", "<p>html</p>", "text"));
        }

        public AdminBootstrapper Bootstrapper() => new(
            Users,
            Tokens,
            new FakePasswordHasher(),
            new FakeOpaqueTokens(),
            TestAuthPolicy.Default,
            new BootstrapSettings("founder@khadra.jo", "0790000001", "Rania Haddad"),
            new AuthEmailDispatcher(Composer, Email, NullLogger<AuthEmailDispatcher>.Instance),
            AuditTrail,
            UnitOfWork,
            new TestClock(Now),
            Log);
    }

    private static User InvitedAdmin(string email) =>
        User.CreateInvitedAdmin(
            EmailAddress.Create(email).Value,
            PhoneNumber.Create("0790000001").Value,
            PersonName.Create("Rania Haddad").Value,
            PasswordHash.FromHash("unusable"),
            Now);

    /// <summary>
    /// Reachable, and likeliest on exactly the deployment this matters for: the owner registered
    /// through the customer app with the address they later configure here.
    ///
    /// The insert would hit the unique index on users.email and land in the race-loss catch, which
    /// reports "created by another instance" — false, and it sends the reader hunting for a second
    /// instance that does not exist.
    /// </summary>
    [Fact]
    public async Task An_address_already_held_by_somebody_else_stops_startup_instead_of_blaming_a_race()
    {
        var context = new AdminBootstrapTestBed();
        context.Users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(false);
        context.Users.ExistsByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>()).Returns(true);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Bootstrapper().EnsureAsync(CancellationToken.None));

        Assert.Contains("the email address is", failure.Message, StringComparison.Ordinal);
        Assert.Empty(context.Added);
    }

    /// <summary>
    /// The phone collides exactly the same way, and is the LIKELIER of the two: trying the customer
    /// app on your own handset is the obvious first thing an owner does. ix_users_phone is unique and
    /// not partial, so a soft-deleted row still holds the number.
    /// </summary>
    [Fact]
    public async Task A_phone_number_already_held_by_somebody_else_stops_startup_too()
    {
        var context = new AdminBootstrapTestBed();
        context.Users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(false);
        context.Users.ExistsByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>()).Returns(false);
        context.Users.ExistsByPhoneAsync(Arg.Any<PhoneNumber>(), Arg.Any<CancellationToken>()).Returns(true);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Bootstrapper().EnsureAsync(CancellationToken.None));

        Assert.Contains("the phone number is", failure.Message, StringComparison.Ordinal);
        Assert.Empty(context.Added);
    }

    /// <summary>
    /// A collision that turns out to be a replica winning the race is not a misconfiguration. Asked
    /// again rather than assumed, so the operator is not sent to look at settings that are correct.
    /// </summary>
    [Fact]
    public async Task A_collision_that_is_actually_a_replica_winning_the_race_does_not_stop_startup()
    {
        var context = new AdminBootstrapTestBed();
        context.Users.ExistsByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>()).Returns(true);
        // False on the first look, true by the time the collision is examined.
        context.Users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(false, true);

        await context.Bootstrapper().EnsureAsync(CancellationToken.None);

        Assert.Empty(context.Added);
        Assert.Contains("another instance won the race", context.Log.AllText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The lockout case. An administrator exists for some OTHER address — including a soft-deleted
    /// one, because AnyAdminExistsAsync ignores the query filter while GetByEmailAsync honours it, so
    /// a deleted administrator both blocks the bootstrap and is invisible to the reissue path.
    ///
    /// Nothing can be done automatically and nothing should be, but it must be said out loud.
    /// </summary>
    [Fact]
    public async Task An_administrator_at_a_different_address_is_reported_rather_than_ignored()
    {
        var context = new AdminBootstrapTestBed();
        context.Users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(true);
        context.Users.GetByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        await context.Bootstrapper().EnsureAsync(CancellationToken.None);

        Assert.Empty(context.Added);
        Assert.Contains("ALREADY has an administrator", context.Log.AllText, StringComparison.Ordinal);
    }

    /// <summary>An account that holds the address but is not an administrator is named as such.</summary>
    [Fact]
    public async Task The_configured_address_belonging_to_a_non_administrator_is_reported()
    {
        var context = new AdminBootstrapTestBed();
        context.Users.AnyAdminExistsAsync(Arg.Any<CancellationToken>()).Returns(true);
        context.Users.GetByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>())
            .Returns(Users.Customer(email: "founder@khadra.jo"));

        await context.Bootstrapper().EnsureAsync(CancellationToken.None);

        Assert.Empty(context.Added);
        Assert.Contains("is NOT an administrator", context.Log.AllText, StringComparison.Ordinal);
    }

    /// <summary>
    /// What a first login actually requires, pinned because it is the question every operator asks.
    ///
    /// Forgot Password works on this account — no status or verification gate stands in its way, so a
    /// link is issued and a password is set. It still does not produce a login: ResetPasswordHandler
    /// changes the password and never touches IsEmailVerified, and CanAuthenticate refuses an
    /// unverified address. The invitation is the only route, because accepting it is the only thing
    /// that proves the mailbox.
    /// </summary>
    [Fact]
    public void Resetting_the_password_does_not_let_an_unaccepted_administrator_sign_in()
    {
        var invited = InvitedAdmin("founder@khadra.jo");

        // Exactly what ResetPasswordHandler does on a consumed reset token.
        invited.ChangePassword(PasswordHash.FromHash("a-genuinely-chosen-password"), Now);

        var refusal = invited.CanAuthenticate();
        Assert.True(refusal.IsFailure);
        Assert.Equal(IdentityErrors.EmailNotVerified.Code, refusal.Error.Code);
    }

    /// <summary>And the invitation, which does both, is what makes the account usable.</summary>
    [Fact]
    public void Accepting_the_invitation_verifies_the_address_and_sets_the_password_together()
    {
        var invited = InvitedAdmin("founder@khadra.jo");

        // Exactly what AcceptInvitationCommand does.
        invited.VerifyEmail(Now);
        invited.ChangePassword(PasswordHash.FromHash("a-genuinely-chosen-password"), Now);

        Assert.True(invited.CanAuthenticate().IsSuccess);
        Assert.Same(UserRole.Admin, invited.Role);
        Assert.True(invited.IsEmailVerified);
    }
}
