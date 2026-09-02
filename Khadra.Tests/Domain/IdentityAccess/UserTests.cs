using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Events;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.IdentityAccess;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = Users.Now;

    private static User Register() => User.RegisterCustomer(
        EmailAddress.Create("ali@example.com").Value,
        PhoneNumber.Create("0791234567").Value,
        PersonName.Create("Ali Ahmad").Value,
        PasswordHash.FromHash("hashed:Passw0rd1"),
        Now);

    [Fact]
    public void Customer_registration_starts_active_unverified_and_raises_an_event()
    {
        var user = Register();

        Assert.False(user.Id.IsEmpty);
        Assert.Same(UserRole.Customer, user.Role);
        Assert.Same(UserStatus.Active, user.Status);
        Assert.False(user.IsEmailVerified);
        Assert.False(user.MustChangePassword);
        Assert.NotEqual(Guid.Empty, user.SecurityStamp);
        Assert.Equal(Now, user.CreatedAt);
        var registered = Assert.IsType<UserRegistered>(Assert.Single(user.DomainEvents));
        Assert.Equal("ali@example.com", registered.Email);
        Assert.Equal("Customer", registered.Role);
    }

    [Fact]
    public void Employees_are_created_verified_with_a_temporary_password()
    {
        var employee = User.CreateEmployee(
            EmailAddress.Create("staff@dealer.jo").Value,
            PhoneNumber.Create("0781234567").Value,
            PersonName.Create("Sara").Value,
            PasswordHash.FromHash("hashed:Temp1234"),
            Now);

        Assert.Same(UserRole.DealerEmployee, employee.Role);
        Assert.True(employee.IsEmailVerified);
        Assert.True(employee.MustChangePassword);
        Assert.True(employee.CanAuthenticate().IsSuccess);
    }

    [Fact]
    public void Unverified_users_cannot_authenticate()
    {
        var user = Register();

        var result = user.CanAuthenticate();

        Assert.True(result.IsFailure);
        Assert.Equal("auth.email_not_verified", result.Error.Code);
    }

    [Fact]
    public void Email_verification_is_idempotent()
    {
        var user = Register();
        user.ClearDomainEvents();

        user.VerifyEmail(Now);
        user.VerifyEmail(Now.AddMinutes(5));

        Assert.True(user.IsEmailVerified);
        Assert.Equal(Now, user.EmailVerifiedAt);
        Assert.Single(user.DomainEvents.OfType<UserEmailVerified>());
        Assert.True(user.CanAuthenticate().IsSuccess);
    }

    [Fact]
    public void Suspension_blocks_sign_in_rotates_the_stamp_and_cannot_repeat()
    {
        var user = Users.Customer();
        var stampBefore = user.SecurityStamp;

        var suspended = user.Suspend("fraud", Now);
        var again = user.Suspend("fraud", Now);

        Assert.True(suspended.IsSuccess);
        Assert.Equal("auth.already_suspended", again.Error.Code);
        Assert.Same(UserStatus.Suspended, user.Status);
        Assert.Equal("fraud", user.SuspensionReason);
        Assert.NotEqual(stampBefore, user.SecurityStamp);
        Assert.Equal("auth.account_suspended", user.CanAuthenticate().Error.Code);
        Assert.Single(user.DomainEvents.OfType<UserSuspended>());
    }

    [Fact]
    public void Reactivation_requires_a_suspended_account()
    {
        var user = Users.Customer();

        Assert.Equal("auth.not_suspended", user.Reactivate(Now).Error.Code);

        user.Suspend("test", Now);
        Assert.True(user.Reactivate(Now).IsSuccess);
        Assert.Same(UserStatus.Active, user.Status);
        Assert.Null(user.SuspensionReason);
        Assert.True(user.CanAuthenticate().IsSuccess);
    }

    [Fact]
    public void Suspension_takes_precedence_over_unverified_email()
    {
        var user = Users.Customer(verified: false);
        user.Suspend("abuse", Now);

        Assert.Equal("auth.account_suspended", user.CanAuthenticate().Error.Code);
    }

    [Fact]
    public void Password_change_rotates_the_stamp_clears_the_forced_flag_and_raises_an_event()
    {
        var user = User.CreateEmployee(
            EmailAddress.Create("staff@dealer.jo").Value,
            PhoneNumber.Create("0781234567").Value,
            PersonName.Create("Sara").Value,
            PasswordHash.FromHash("hashed:Temp1234"),
            Now);
        var stampBefore = user.SecurityStamp;
        user.ClearDomainEvents();

        user.ChangePassword(PasswordHash.FromHash("hashed:NewPass99"), Now.AddDays(1));

        Assert.Equal("hashed:NewPass99", user.PasswordHash.Value);
        Assert.False(user.MustChangePassword);
        Assert.Equal(Now.AddDays(1), user.PasswordChangedAt);
        Assert.NotEqual(stampBefore, user.SecurityStamp);
        Assert.True(user.MatchesSecurityStamp(user.SecurityStamp));
        Assert.False(user.MatchesSecurityStamp(stampBefore));
        Assert.IsType<UserPasswordChanged>(Assert.Single(user.DomainEvents));
    }

    [Fact]
    public void Deleted_accounts_look_like_bad_credentials_and_cannot_be_deleted_twice()
    {
        var user = Users.Customer();

        Assert.True(user.Delete(Now).IsSuccess);
        Assert.Equal("auth.already_deleted", user.Delete(Now).Error.Code);
        Assert.True(user.IsDeleted);
        Assert.Equal(Now, user.DeletedAt);
        Assert.Equal("auth.invalid_credentials", user.CanAuthenticate().Error.Code);
        Assert.Single(user.DomainEvents.OfType<UserDeleted>());
    }

    [Fact]
    public void Records_the_last_successful_login()
    {
        var user = Users.Customer();

        user.RecordSuccessfulLogin(Now.AddHours(2));

        Assert.Equal(Now.AddHours(2), user.LastLoginAt);
    }

    [Fact]
    public void Entities_are_equal_by_id()
    {
        var user = Users.Customer();
        var other = Users.Customer();

        Assert.NotEqual(user, other);
        Assert.Equal(user, user);
        Assert.Throws<DomainException>(() => PasswordHash.FromHash(" "));
    }
}
