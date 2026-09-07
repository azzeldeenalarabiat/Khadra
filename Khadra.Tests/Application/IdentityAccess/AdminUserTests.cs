using Khadra.Application.Auditing;
using Khadra.Application.Common;
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

// Who may administer the platform, and the two ways the console could be locked out of itself.
//
// Nothing in the system creates an administrator except another administrator, so an empty set is
// permanent: no support tool, and no seeder any more. The one exception is AdminBootstrapper, which
// fires only on a database that has NEVER held an administrator — suspended and deleted ones count —
// so it cannot be used to reopen a set that has been emptied. AdminBootstrapTests holds that half.
//
// The domain cannot hold either invariant, because both are about the SET of administrators rather
// than about any one of them, so the handler does — and these are what keep it honest.
public sealed class AdminUserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

    private sealed class Context
    {
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IVerificationTokenRepository Tokens { get; } = Substitute.For<IVerificationTokenRepository>();
        public IEmailSender Email { get; } = Substitute.For<IEmailSender>();
        public IAuthEmailComposer Composer { get; } = Substitute.For<IAuthEmailComposer>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IAuditTrail AuditTrail { get; } = Substitute.For<IAuditTrail>();
        public ICurrentActor Actor { get; } = Substitute.For<ICurrentActor>();
        public TestClock Clock { get; } = new(Now);
        public List<AuditEntry> Recorded { get; } = [];
        public List<User> Added { get; } = [];
        public Id ActingAdminId { get; } = Id.New();

        public Context()
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            AuditTrail.When(trail => trail.Record(Arg.Any<AuditEntry>()))
                .Do(call => Recorded.Add(call.Arg<AuditEntry>()));
            Users.When(repo => repo.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()))
                .Do(call => Added.Add(call.Arg<User>()));
            Actor.UserId.Returns(ActingAdminId);
            Actor.Role.Returns(UserRole.Admin);
            Actor.Name.Returns("Rania Haddad");
            Actor.CorrelationId.Returns("test-correlation");
            // One other administrator standing, unless a test says otherwise.
            Users.CountActiveAdminsExceptAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>()).Returns(1);
        }

        public User Given(User user)
        {
            Users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
            return user;
        }

        public AdminUserCommandHandlers Handlers() => new(
            Users,
            Tokens,
            new FakePasswordHasher(),
            new FakeOpaqueTokens(),
            TestAuthPolicy.Default,
            new AuthEmailDispatcher(Composer, Email, NullLogger<AuthEmailDispatcher>.Instance),
            new AdminActionRecorder(AuditTrail, Actor, Clock),
            Actor,
            UnitOfWork,
            Clock);
    }

    private static User Admin(string email = "omar@khadra.jo", string phone = "0790000002") =>
        User.CreateAdmin(
            EmailAddress.Create(email).Value,
            PhoneNumber.Create(phone).Value,
            PersonName.Create("Omar Deeb").Value,
            PasswordHash.FromHash("hash"),
            Now.AddYears(-1));

    [Fact]
    public async Task An_administrator_cannot_deactivate_their_own_account()
    {
        var context = new Context();
        var self = Admin();
        // The acting admin IS the target.
        context.Actor.UserId.Returns(self.Id);
        context.Given(self);

        var result = await context.Handlers().Handle(
            new DeactivateAdminCommand(self.Id, "Testing."),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IdentityErrors.CannotDeactivateSelf.Code, result.Error.Code);
        Assert.Same(UserStatus.Active, self.Status);
        Assert.Empty(context.Recorded);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_last_active_administrator_cannot_be_deactivated()
    {
        var context = new Context();
        var last = context.Given(Admin());
        // Nobody else is left standing.
        context.Users.CountActiveAdminsExceptAsync(last.Id, Arg.Any<CancellationToken>()).Returns(0);

        var result = await context.Handlers().Handle(
            new DeactivateAdminCommand(last.Id, "Leaving."),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IdentityErrors.LastAdministrator.Code, result.Error.Code);
        Assert.Same(UserStatus.Active, last.Status);
        Assert.Empty(context.Recorded);
    }

    [Fact]
    public async Task Another_administrator_is_deactivated_when_one_is_left_to_undo_it()
    {
        var context = new Context();
        var other = context.Given(Admin());

        var result = await context.Handlers().Handle(
            new DeactivateAdminCommand(other.Id, "Left the company."),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(UserStatus.Suspended, other.Status);
        var entry = Assert.Single(context.Recorded);
        Assert.Same(AuditAction.AdminDeactivated, entry.Action);
        Assert.Equal("Left the company.", entry.Reason);
    }

    [Fact]
    public async Task A_customer_cannot_be_deactivated_through_the_admin_users_route()
    {
        var context = new Context();
        var customer = context.Given(User.RegisterCustomer(
            EmailAddress.Create("rana@example.jo").Value,
            PhoneNumber.Create("0791234567").Value,
            PersonName.Create("Rana Sharif").Value,
            PasswordHash.FromHash("hash"),
            Now.AddYears(-1)));

        var result = await context.Handlers().Handle(
            new DeactivateAdminCommand(customer.Id, "Wrong route."),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IdentityErrors.UserNotFound.Code, result.Error.Code);
        Assert.Same(UserStatus.Active, customer.Status);
    }

    [Fact]
    public async Task An_invited_administrator_has_no_usable_password_and_an_unverified_address()
    {
        var context = new Context();
        context.Users.ExistsByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>()).Returns(false);
        context.Users.ExistsByPhoneAsync(Arg.Any<PhoneNumber>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await context.Handlers().Handle(
            new InviteAdminCommand("yousef@khadra.jo", "0790000003", "Yousef Barakat"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var invited = Assert.Single(context.Added);
        Assert.Same(UserRole.Admin, invited.Role);
        // Nothing about the account works until the one-time link proves the address.
        Assert.False(invited.IsEmailVerified);
        Assert.Same(AuditAction.AdminInvited, Assert.Single(context.Recorded).Action);
        // The expiry is the token's own, so the console never states a lifetime it guessed.
        Assert.Equal(Now.Add(TestAuthPolicy.Default.EmployeeInvitationLifetime), result.Value.ExpiresAt);
    }

    [Fact]
    public async Task An_address_that_already_belongs_to_someone_is_refused()
    {
        var context = new Context();
        context.Users.ExistsByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await context.Handlers().Handle(
            new InviteAdminCommand("admin@khadra.jo", "0790000009", "Someone Else"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(context.Added);
        await context.Email.DidNotReceive().SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reactivating_returns_a_deactivated_administrator_to_work()
    {
        var context = new Context();
        var other = context.Given(Admin());
        other.Suspend("Left the company.", Now);

        var result = await context.Handlers().Handle(
            new ReactivateAdminCommand(other.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(UserStatus.Active, other.Status);
        Assert.Same(AuditAction.AdminReactivated, Assert.Single(context.Recorded).Action);
    }
}
