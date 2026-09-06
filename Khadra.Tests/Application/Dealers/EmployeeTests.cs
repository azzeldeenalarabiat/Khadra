using Khadra.Application.Notifications;
using Khadra.Domain.Notifications.Repositories;
using Khadra.Application.Common;
using Khadra.Application.Common.Ports;
using Khadra.Application.Dealers;
using Khadra.Application.Dealers.ManageEmployees;
using Khadra.Application.Dealers.ReadModels;
using Khadra.Application.IdentityAccess;
using Khadra.Application.IdentityAccess.AcceptInvitation;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Events;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Khadra.Tests.Application.Dealers;

// Spec 4.2: the owner invites staff, grants report access, and can end an employee's access at any
// time -- immediately. The invitation flow is the one to hold hardest: the account must be inert
// until the person proves the mailbox, and a deactivated employee must lose every session they hold.
public sealed class EmployeeTests
{
    private static readonly Id OwnerId = Id.New();
    private static readonly Id OtherOwnerId = Id.New();

    private sealed class Context
    {
        public IDealerRepository Dealers { get; } = Substitute.For<IDealerRepository>();
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public IVerificationTokenRepository Tokens { get; } = Substitute.For<IVerificationTokenRepository>();
        public IPasswordHasher Hasher { get; } = Substitute.For<IPasswordHasher>();
        public FakeOpaqueTokens Opaque { get; } = new();
        public IEmployeeReader Reader { get; } = Substitute.For<IEmployeeReader>();
        public IAuthEmailComposer Composer { get; } = Substitute.For<IAuthEmailComposer>();
        public IEmailSender Sender { get; } = Substitute.For<IEmailSender>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public TestClock Clock { get; } = new(Build.Now);
        public Dealer Dealer { get; }
        public List<User> AddedUsers { get; } = [];
        public List<VerificationToken> IssuedTokens { get; } = [];
        public List<EmailMessage> Sent { get; } = [];

        public Context()
        {
            Dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
            Dealers.GetByOwnerUserIdAsync(OwnerId, Arg.Any<CancellationToken>()).Returns(Dealer);
            Hasher.Hash(Arg.Any<string>()).Returns(call => "$2a$12$hash-of-" + call.Arg<string>().Length);
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            Users.When(repository => repository.AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>()))
                .Do(call => AddedUsers.Add(call.Arg<User>()));
            Tokens.When(repository => repository.AddAsync(Arg.Any<VerificationToken>(), Arg.Any<CancellationToken>()))
                .Do(call => IssuedTokens.Add(call.Arg<VerificationToken>()));
            Composer.EmployeeInvitation(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(call => new EmailMessage(call.Arg<User>().Email.Value, "x", "Invite from " + call.ArgAt<string>(1), call.ArgAt<string>(2), call.ArgAt<string>(2)));
            Sender.When(sender => sender.SendAsync(Arg.Any<EmailMessage>(), Arg.Any<CancellationToken>()))
                .Do(call => Sent.Add(call.Arg<EmailMessage>()));
            Reader.GetAsync(Arg.Any<Id>(), Arg.Any<Id>(), Arg.Any<CancellationToken>())
                .Returns(call => new EmployeeListItem(
                    call.ArgAt<Id>(1).Value, Guid.NewGuid(), "Someone", "x@y.jo", "+962790000000",
                    false, true, "Invited", null, Build.Now, null));
        }

        public EmployeeHandlers Handlers() => new(
            new DealerMembershipResolver(Dealers),
            new EmployeeAccountProvisioner(Users, Tokens, Hasher, Opaque, TestAuthPolicy.Default, Clock),
            Users,
            Reader,
            new AuthEmailDispatcher(Composer, Sender, NullLogger<AuthEmailDispatcher>.Instance),
            new DealerTeamNotifier(Substitute.For<INotifier>(), Users),
            Clock,
            UnitOfWork);

        public AcceptInvitationHandler Accept() =>
            new(Tokens, Users, Hasher, Opaque, TestAuthPolicy.Default, Clock, UnitOfWork);
    }

    private static InviteEmployeeCommand Invite(Id owner) =>
        new(owner, "Ahmad Zaid", "ahmad@alnadeem.jo", "0791234567", CanViewReports: false);

    [Fact]
    public async Task Inviting_creates_an_inert_account_hires_it_and_emails_the_invitation()
    {
        var context = new Context();

        var result = await context.Handlers().Handle(Invite(OwnerId), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);

        var user = Assert.Single(context.AddedUsers);
        Assert.Same(UserRole.DealerEmployee, user.Role);
        // Inert: unverified, so CanAuthenticate refuses it whatever the password.
        Assert.False(user.IsEmailVerified);
        Assert.True(user.CanAuthenticate().IsFailure);

        var employee = Assert.Single(context.Dealer.Employees);
        Assert.Equal(user.Id, employee.UserId);
        Assert.False(employee.CanViewReports);

        var token = Assert.Single(context.IssuedTokens);
        Assert.Same(VerificationPurpose.EmployeeInvitation, token.Purpose);
        Assert.Equal(Build.Now.AddDays(7), token.ExpiresAt);

        // One commit for the user, the token and the hire together; the email goes after it.
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        var email = Assert.Single(context.Sent);
        Assert.Contains(context.Dealer.BusinessName.Value, email.Subject, StringComparison.Ordinal);
        Assert.Equal(context.Opaque.Issued.Single().Value, email.HtmlBody);
    }

    [Fact]
    public async Task An_employee_cannot_invite_anyone()
    {
        var context = new Context();
        var staffId = Id.New();
        context.Dealer.HireEmployee(staffId, false, Build.Now);
        context.Dealers.GetByStaffUserIdAsync(staffId, Arg.Any<CancellationToken>()).Returns(context.Dealer);

        var result = await context.Handlers().Handle(Invite(staffId), CancellationToken.None);

        Assert.Equal("dealer.owner_only", result.Error.Code);
        Assert.Empty(context.AddedUsers);
    }

    [Fact]
    public async Task A_taken_email_is_refused_before_anything_is_staged()
    {
        var context = new Context();
        context.Users.ExistsByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await context.Handlers().Handle(Invite(OwnerId), CancellationToken.None);

        Assert.Equal("auth.email_taken", result.Error.Code);
        Assert.Empty(context.Dealer.Employees);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Accepting_the_invitation_proves_the_mailbox_and_sets_the_first_password()
    {
        var context = new Context();
        await context.Handlers().Handle(Invite(OwnerId), CancellationToken.None);
        var user = context.AddedUsers.Single();
        var token = context.IssuedTokens.Single();
        var raw = context.Opaque.Issued.Single().Value;
        context.Tokens.GetByHashAsync(context.Opaque.Hash(raw), VerificationPurpose.EmployeeInvitation, Arg.Any<CancellationToken>())
            .Returns(token);
        context.Users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var stampBefore = user.SecurityStamp;

        var accepted = await context.Accept().Handle(new AcceptInvitationCommand(raw, "Passw0rd1"), CancellationToken.None);
        var twice = await context.Accept().Handle(new AcceptInvitationCommand(raw, "Passw0rd1"), CancellationToken.None);

        Assert.True(accepted.IsSuccess, accepted.IsFailure ? accepted.Error.Code : null);
        Assert.True(user.IsEmailVerified);
        Assert.True(user.CanAuthenticate().IsSuccess);
        Assert.NotEqual(stampBefore, user.SecurityStamp);
        // Single use.
        Assert.True(twice.IsFailure);
    }

    [Fact]
    public async Task Resending_is_refused_once_the_invitation_was_accepted()
    {
        var context = new Context();
        await context.Handlers().Handle(Invite(OwnerId), CancellationToken.None);
        var user = context.AddedUsers.Single();
        var employee = context.Dealer.Employees.Single();
        context.Users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var pending = await context.Handlers().Handle(new ResendEmployeeInvitationCommand(OwnerId, employee.Id), CancellationToken.None);
        user.VerifyEmail(Build.Now);
        var accepted = await context.Handlers().Handle(new ResendEmployeeInvitationCommand(OwnerId, employee.Id), CancellationToken.None);

        Assert.True(pending.IsSuccess);
        Assert.Equal(2, context.Sent.Count);
        Assert.Equal("dealer.invitation_accepted", accepted.Error.Code);
    }

    [Fact]
    public async Task Deactivating_ends_every_session_and_the_person_loses_standing_at_once()
    {
        var context = new Context();
        await context.Handlers().Handle(Invite(OwnerId), CancellationToken.None);
        var user = context.AddedUsers.Single();
        var employee = context.Dealer.Employees.Single();
        context.Users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        context.Dealers.GetByStaffUserIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(context.Dealer);
        var stampBefore = user.SecurityStamp;
        user.ClearDomainEvents();

        var result = await context.Handlers().Handle(new DeactivateEmployeeCommand(OwnerId, employee.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.False(employee.IsActive);
        // Access tokens die on their next request (stamp), refresh families after commit (event).
        Assert.NotEqual(stampBefore, user.SecurityStamp);
        Assert.Contains(user.DomainEvents, domainEvent => domainEvent is UserSessionsRevoked);
        // And the membership resolver no longer knows them.
        var standing = await new DealerMembershipResolver(context.Dealers).ResolveAsync(user.Id);
        Assert.Equal("dealer.not_registered", standing.Error.Code);

        var again = await context.Handlers().Handle(new DeactivateEmployeeCommand(OwnerId, employee.Id), CancellationToken.None);
        Assert.Equal("dealer.employee_inactive", again.Error.Code);
    }

    [Fact]
    public async Task Reactivation_restores_the_same_identity_with_its_previous_permissions()
    {
        var context = new Context();
        await context.Handlers().Handle(new InviteEmployeeCommand(OwnerId, "Omar Tarawneh", "omar@alnadeem.jo", "0797654321", CanViewReports: true), CancellationToken.None);
        var user = context.AddedUsers.Single();
        var employee = context.Dealer.Employees.Single();
        context.Users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        await context.Handlers().Handle(new DeactivateEmployeeCommand(OwnerId, employee.Id), CancellationToken.None);

        var result = await context.Handlers().Handle(new ReactivateEmployeeCommand(OwnerId, employee.Id), CancellationToken.None);
        var again = await context.Handlers().Handle(new ReactivateEmployeeCommand(OwnerId, employee.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.True(employee.IsActive);
        Assert.True(employee.CanViewReports);
        Assert.Equal("dealer.employee_already_active", again.Error.Code);
    }

    [Fact]
    public async Task Report_access_is_the_owners_to_grant_and_withdraw()
    {
        var context = new Context();
        await context.Handlers().Handle(Invite(OwnerId), CancellationToken.None);
        var user = context.AddedUsers.Single();
        var employee = context.Dealer.Employees.Single();
        context.Users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var granted = await context.Handlers().Handle(new SetEmployeeReportAccessCommand(OwnerId, employee.Id, true), CancellationToken.None);
        Assert.True(granted.IsSuccess);
        Assert.True(context.Dealer.CanViewReports(user.Id));

        var withdrawn = await context.Handlers().Handle(new SetEmployeeReportAccessCommand(OwnerId, employee.Id, false), CancellationToken.None);
        Assert.True(withdrawn.IsSuccess);
        Assert.False(context.Dealer.CanViewReports(user.Id));
    }

    [Fact]
    public async Task Another_owner_cannot_touch_this_dealers_staff()
    {
        var context = new Context();
        await context.Handlers().Handle(Invite(OwnerId), CancellationToken.None);
        var employee = context.Dealer.Employees.Single();
        var otherDealer = Build.ApprovedDealer(ownerUserId: OtherOwnerId);
        context.Dealers.GetByOwnerUserIdAsync(OtherOwnerId, Arg.Any<CancellationToken>()).Returns(otherDealer);

        var result = await context.Handlers().Handle(new DeactivateEmployeeCommand(OtherOwnerId, employee.Id), CancellationToken.None);

        Assert.Equal("dealer.employee_not_found", result.Error.Code);
        Assert.True(employee.IsActive);
    }
}
