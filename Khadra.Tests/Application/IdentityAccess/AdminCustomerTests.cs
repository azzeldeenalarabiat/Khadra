using Khadra.Application.Auditing;
using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.AdminCustomers;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Domain.Auditing;
using Khadra.Domain.Auditing.Repositories;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Repositories;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.IdentityAccess;

// Suspending an account through the customers route.
//
// Two things have to hold. `User.Suspend` works on ANY role, so the route has to refuse a user who is
// not a customer — suspending a dealer owner here would lock them out while their dealership went on
// trading, and record it under an action that says "customer". And the audit entry must carry a
// reference rather than a name: `audit_entries` is append-only by trigger and by guard, so identity
// written into it can never be taken back out (pre-launch item 18).
public sealed class AdminCustomerTests
{
    private static readonly Id AdminId = Id.New();

    private sealed class Context
    {
        public IUserRepository Users { get; } = Substitute.For<IUserRepository>();
        public ICustomerAdminReader Reader { get; } = Substitute.For<ICustomerAdminReader>();
        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
        public IAuditTrail AuditTrail { get; } = Substitute.For<IAuditTrail>();
        public ICurrentActor Actor { get; } = Substitute.For<ICurrentActor>();
        public TestClock Clock { get; } = new(Users_.Now);
        public List<AuditEntry> Recorded { get; } = [];

        public Context()
        {
            UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
            AuditTrail.When(trail => trail.Record(Arg.Any<AuditEntry>()))
                .Do(call => Recorded.Add(call.Arg<AuditEntry>()));
            Actor.UserId.Returns(AdminId);
            Actor.Role.Returns(UserRole.Admin);
            Actor.Name.Returns("Rania Haddad");
            Actor.CorrelationId.Returns("test-correlation");
        }

        public User Given(User user)
        {
            Users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
            Reader.GetAsync(user.Id, Arg.Any<CancellationToken>()).Returns(Profile(user));
            return user;
        }

        public AdminCustomerCommandHandlers Handlers() =>
            new(Users, Reader, new AdminActionRecorder(AuditTrail, Actor, Clock), UnitOfWork, Clock);

        private static CustomerProfile Profile(User user) => new(
            user.Id.Value, user.Name.Value, user.Email.Value, user.Phone.Value, user.Status.Name,
            user.SuspensionReason, user.IsEmailVerified, user.EmailVerifiedAt, user.DateOfBirth,
            user.IsForeignNational, user.CreatedAt, user.LastLoginAt, user.PasswordChangedAt,
            user.HasCompleteRenterDocuments, [], new CustomerBookingTotals(0, 0, 0, 0, 0));
    }

    private static class Users_
    {
        public static readonly DateTimeOffset Now = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);
    }

    private static User Customer() =>
        User.RegisterCustomer(
            EmailAddress.Create("rana@example.jo").Value,
            PhoneNumber.Create("0791234567").Value,
            PersonName.Create("Rana Sharif").Value,
            PasswordHash.FromHash("hash"),
            Users_.Now.AddYears(-1),
            new DateOnly(1995, 4, 12));

    private static User DealerOwner() =>
        User.RegisterDealerOwner(
            EmailAddress.Create("owner@khadra-dealers.jo").Value,
            PhoneNumber.Create("0799999999").Value,
            PersonName.Create("Al-Nadeem Owner").Value,
            PasswordHash.FromHash("hash"),
            Users_.Now.AddYears(-1));

    [Fact]
    public async Task A_customer_is_suspended_and_their_sessions_are_cut()
    {
        var context = new Context();
        var user = context.Given(Customer());
        var stampBefore = user.SecurityStamp;

        var result = await context.Handlers().Handle(
            new SuspendCustomerCommand(user.Id, "Two no-shows in thirty days."),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(UserStatus.Suspended, user.Status);
        Assert.Equal("Two no-shows in thirty days.", user.SuspensionReason);
        // The rotated stamp is what ends every live session, not a separate revoke call.
        Assert.NotEqual(stampBefore, user.SecurityStamp);
    }

    [Fact]
    public async Task A_dealer_owner_cannot_be_suspended_through_the_customers_route()
    {
        var context = new Context();
        var owner = context.Given(DealerOwner());

        var result = await context.Handlers().Handle(
            new SuspendCustomerCommand(owner.Id, "Wrong route."),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        // Not-found, not forbidden: answering "wrong role" would confirm whose id this is.
        Assert.Equal(IdentityErrors.UserNotFound.Code, result.Error.Code);
        Assert.Same(UserStatus.Active, owner.Status);
        Assert.Empty(context.Recorded);
        await context.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_administrator_cannot_be_suspended_through_the_customers_route()
    {
        var context = new Context();
        var admin = context.Given(User.CreateAdmin(
            EmailAddress.Create("omar@khadra.jo").Value,
            PhoneNumber.Create("0790000002").Value,
            PersonName.Create("Omar Deeb").Value,
            PasswordHash.FromHash("hash"),
            Users_.Now.AddYears(-1)));

        var result = await context.Handlers().Handle(
            new SuspendCustomerCommand(admin.Id, "Wrong route."),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IdentityErrors.UserNotFound.Code, result.Error.Code);
        Assert.Same(UserStatus.Active, admin.Status);
    }

    [Fact]
    public async Task The_audit_entry_names_no_customer_and_carries_no_contact_details()
    {
        var context = new Context();
        var user = context.Given(Customer());

        await context.Handlers().Handle(
            new SuspendCustomerCommand(user.Id, "Two no-shows in thirty days."),
            CancellationToken.None);

        var entry = Assert.Single(context.Recorded);
        Assert.Same(AuditAction.CustomerSuspended, entry.Action);
        Assert.Same(AuditEntityType.Customer, entry.EntityType);
        Assert.Equal(user.Id, entry.EntityId);

        // The label is a reference. The table can never be erased, so a name, an email or a phone
        // number written here would outlive any request to remove it.
        var forbidden = new[] { "Rana", "Sharif", "rana@example.jo", "0791234567" };
        foreach (var value in forbidden)
        {
            Assert.DoesNotContain(value, entry.SubjectLabel, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(value, entry.PreviousValue ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(value, entry.NewValue ?? "", StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal("Active", entry.PreviousValue);
        Assert.Equal("Suspended", entry.NewValue);
    }

    [Fact]
    public async Task Suspending_an_already_suspended_customer_is_refused_rather_than_recorded_twice()
    {
        var context = new Context();
        var user = context.Given(Customer());
        user.Suspend("Already done.", Users_.Now);

        var result = await context.Handlers().Handle(
            new SuspendCustomerCommand(user.Id, "Again."),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(context.Recorded);
    }

    [Fact]
    public async Task Reactivating_returns_the_customer_to_active_and_clears_the_reason()
    {
        var context = new Context();
        var user = context.Given(Customer());
        user.Suspend("Two no-shows in thirty days.", Users_.Now);

        var result = await context.Handlers().Handle(
            new ReactivateCustomerCommand(user.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(UserStatus.Active, user.Status);
        Assert.Null(user.SuspensionReason);
        Assert.Same(AuditAction.CustomerReactivated, Assert.Single(context.Recorded).Action);
    }
}
