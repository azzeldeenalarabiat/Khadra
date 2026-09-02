using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.Login;
using Khadra.Application.IdentityAccess.RegisterCustomer;
using Khadra.Domain.IdentityAccess;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.IdentityAccess;

public sealed class RegisterCustomerHandlerTests
{
    private static RegisterCustomerCommand ValidCommand() =>
        new("Ali@Example.com", "Passw0rd1", "Ali Ahmad", "079 123 4567");

    private static RegisterCustomerHandler Handler(AuthHandlerTestContext context) => new(
        context.UserRepository,
        context.VerificationTokens,
        context.Hasher,
        context.OpaqueTokens,
        context.Policy,
        context.Clock,
        context.UnitOfWork,
        context.Emails);

    [Fact]
    public async Task Creates_an_unverified_customer_with_a_verification_link()
    {
        var context = new AuthHandlerTestContext();

        var result = await Handler(context).Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = Assert.Single(context.AddedUsers);
        Assert.Equal("ali@example.com", user.Email.Value);
        Assert.Equal("+962791234567", user.Phone.Value);
        Assert.Equal("hashed:Passw0rd1", user.PasswordHash.Value);
        Assert.Same(UserRole.Customer, user.Role);
        Assert.False(user.IsEmailVerified);
        Assert.Equal(user.Id.Value, result.Value.UserId);

        var token = Assert.Single(context.AddedVerificationTokens);
        Assert.Same(VerificationPurpose.EmailVerification, token.Purpose);
        Assert.Equal(user.Id, token.UserId);
        Assert.Equal(Users.Now.AddHours(24), token.ExpiresAt);

        var email = Assert.Single(context.SentEmails());
        Assert.Equal($"verify:{context.OpaqueTokens.Issued.Single().Value}", email.TextBody);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejects_duplicate_email_before_touching_the_phone()
    {
        var context = new AuthHandlerTestContext();
        context.UserRepository.ExistsByEmailAsync(Arg.Any<EmailAddress>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Handler(context).Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal("auth.email_taken", result.Error.Code);
        Assert.Empty(context.AddedUsers);
        await context.UserRepository.DidNotReceive().ExistsByPhoneAsync(Arg.Any<PhoneNumber>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejects_duplicate_phone()
    {
        var context = new AuthHandlerTestContext();
        context.UserRepository.ExistsByPhoneAsync(Arg.Any<PhoneNumber>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Handler(context).Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal("auth.phone_taken", result.Error.Code);
    }

    [Theory]
    [InlineData("bad-email", "Passw0rd1", "Ali", "0791234567", "auth.invalid_email")]
    [InlineData("a@b.co", "short", "Ali", "0791234567", "auth.password_policy")]
    [InlineData("a@b.co", "Passw0rd1", "A", "0791234567", "auth.invalid_name")]
    [InlineData("a@b.co", "Passw0rd1", "Ali", "123", "auth.invalid_phone")]
    public async Task Rejects_invalid_input_with_the_specific_code(string email, string password, string name, string phone, string code)
    {
        var context = new AuthHandlerTestContext();

        var result = await Handler(context).Handle(new RegisterCustomerCommand(email, password, name, phone), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
        Assert.Empty(context.AddedUsers);
    }

    [Fact]
    public async Task Email_delivery_failure_does_not_fail_registration()
    {
        var context = new AuthHandlerTestContext();
        context.EmailSender.SendAsync(Arg.Any<Khadra.Application.Common.Ports.EmailMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("smtp down"));

        var result = await Handler(context).Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(context.AddedVerificationTokens);
    }
}

public sealed class LoginHandlerTests
{
    private static readonly ClientInfo Client = new("10.0.0.5", "xunit");

    private static LoginHandler Handler(AuthHandlerTestContext context) => new(
        context.UserRepository,
        context.Hasher,
        context.TokenFactory,
        context.Clock,
        context.UnitOfWork);

    [Fact]
    public async Task Unknown_email_costs_a_hash_and_returns_invalid_credentials()
    {
        var context = new AuthHandlerTestContext();

        var result = await Handler(context).Handle(new LoginCommand("nobody@example.com", "Passw0rd1", Client), CancellationToken.None);

        Assert.Equal("auth.invalid_credentials", result.Error.Code);
        Assert.Equal(1, context.Hasher.VerifyCalls);
        Assert.Empty(context.AddedRefreshTokens);
    }

    [Fact]
    public async Task Malformed_email_is_also_invalid_credentials()
    {
        var context = new AuthHandlerTestContext();

        var result = await Handler(context).Handle(new LoginCommand("not an email", "Passw0rd1", Client), CancellationToken.None);

        Assert.Equal("auth.invalid_credentials", result.Error.Code);
    }

    [Fact]
    public async Task Wrong_password_is_invalid_credentials()
    {
        var context = new AuthHandlerTestContext();
        context.KnownUser(Users.Customer());

        var result = await Handler(context).Handle(new LoginCommand("ali@example.com", "WrongPass1", Client), CancellationToken.None);

        Assert.Equal("auth.invalid_credentials", result.Error.Code);
    }

    [Fact]
    public async Task Unverified_email_is_forbidden_only_after_the_password_matches()
    {
        var context = new AuthHandlerTestContext();
        context.KnownUser(Users.Customer(verified: false));

        var wrongPassword = await Handler(context).Handle(new LoginCommand("ali@example.com", "WrongPass1", Client), CancellationToken.None);
        var rightPassword = await Handler(context).Handle(new LoginCommand("ali@example.com", "Passw0rd1", Client), CancellationToken.None);

        Assert.Equal("auth.invalid_credentials", wrongPassword.Error.Code);
        Assert.Equal("auth.email_not_verified", rightPassword.Error.Code);
    }

    [Fact]
    public async Task Suspended_accounts_are_forbidden()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        user.Suspend("fraud", Users.Now);

        var result = await Handler(context).Handle(new LoginCommand("ali@example.com", "Passw0rd1", Client), CancellationToken.None);

        Assert.Equal("auth.account_suspended", result.Error.Code);
    }

    [Fact]
    public async Task Success_issues_a_new_refresh_family_and_records_the_login()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());

        var result = await Handler(context).Handle(new LoginCommand("ALI@example.com", "Passw0rd1", Client), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"access-for-{user.Id}", result.Value.AccessToken);
        Assert.Equal(user.Email.Value, result.Value.User.Email);
        Assert.Equal(Users.Now, user.LastLoginAt);

        var refreshToken = Assert.Single(context.AddedRefreshTokens);
        Assert.Equal(user.Id, refreshToken.UserId);
        Assert.Equal(context.OpaqueTokens.Hash(result.Value.RefreshToken), refreshToken.TokenHash);
        Assert.Equal(refreshToken.ExpiresAt, result.Value.RefreshTokenExpiresAt);
        Assert.Equal("10.0.0.5", refreshToken.CreatedByIp);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
