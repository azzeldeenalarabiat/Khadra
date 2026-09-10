using Khadra.Application.IdentityAccess.ForgotPassword;
using Khadra.Application.IdentityAccess.ResendVerification;
using Khadra.Application.IdentityAccess.ResetPassword;
using Khadra.Application.IdentityAccess.VerifyEmail;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Events;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.IdentityAccess;

public sealed class VerifyEmailHandlerTests
{
    private static VerifyEmailHandler Handler(AuthHandlerTestContext context) => new(
        context.VerificationTokens,
        context.UserRepository,
        context.OpaqueTokens,
        context.Clock,
        context.UnitOfWork);

    private static VerificationToken Stored(AuthHandlerTestContext context, User user, VerificationPurpose purpose, out string raw)
    {
        var generated = context.OpaqueTokens.Generate();
        raw = generated.Value;
        var token = VerificationToken.Issue(user.Id, purpose, generated.Hash, Users.Now, TimeSpan.FromHours(24));
        context.VerificationTokens.GetByHashAsync(generated.Hash, purpose, Arg.Any<CancellationToken>()).Returns(token);
        return token;
    }

    [Fact]
    public async Task Valid_link_verifies_the_user_and_consumes_the_token()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer(verified: false));
        var token = Stored(context, user, VerificationPurpose.EmailVerification, out var raw);

        var result = await Handler(context).Handle(new VerifyEmailCommand(raw), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(user.IsEmailVerified);
        Assert.True(token.IsConsumed);
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consumed_expired_or_unknown_links_are_invalid()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer(verified: false));
        var consumed = Stored(context, user, VerificationPurpose.EmailVerification, out var consumedRaw);
        consumed.Consume(Users.Now);
        Stored(context, user, VerificationPurpose.EmailVerification, out var expiredRaw);

        var replay = await Handler(context).Handle(new VerifyEmailCommand(consumedRaw), CancellationToken.None);
        var unknown = await Handler(context).Handle(new VerifyEmailCommand("garbage"), CancellationToken.None);
        context.Clock.Advance(TimeSpan.FromHours(25));
        var expired = await Handler(context).Handle(new VerifyEmailCommand(expiredRaw), CancellationToken.None);

        Assert.Equal("auth.invalid_token", replay.Error.Code);
        Assert.Equal("auth.invalid_token", unknown.Error.Code);
        Assert.Equal("auth.invalid_token", expired.Error.Code);
        Assert.False(user.IsEmailVerified);
    }

    [Fact]
    public async Task A_password_reset_token_cannot_verify_an_email()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer(verified: false));
        Stored(context, user, VerificationPurpose.PasswordReset, out var raw);

        var result = await Handler(context).Handle(new VerifyEmailCommand(raw), CancellationToken.None);

        Assert.Equal("auth.invalid_token", result.Error.Code);
    }
}

public sealed class ResendVerificationHandlerTests
{
    private static ResendVerificationHandler Handler(AuthHandlerTestContext context) => new(
        context.UserRepository,
        context.VerificationTokens,
        context.OpaqueTokens,
        context.Policy,
        context.Clock,
        context.UnitOfWork,
        context.Emails);

    [Fact]
    public async Task Unknown_or_already_verified_accounts_get_no_email_but_still_succeed()
    {
        var context = new AuthHandlerTestContext();
        context.KnownUser(Users.Customer(verified: true));

        var unknown = await Handler(context).Handle(new ResendVerificationCommand("ghost@example.com"), CancellationToken.None);
        var verified = await Handler(context).Handle(new ResendVerificationCommand("ali@example.com"), CancellationToken.None);
        var malformed = await Handler(context).Handle(new ResendVerificationCommand("not-an-email"), CancellationToken.None);

        Assert.True(unknown.IsSuccess);
        Assert.True(verified.IsSuccess);
        Assert.True(malformed.IsSuccess);
        Assert.Empty(context.SentEmails());
        Assert.Empty(context.AddedVerificationTokens);
    }

    [Fact]
    public async Task Unverified_account_gets_a_fresh_link_and_older_links_are_invalidated()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer(verified: false));

        var result = await Handler(context).Handle(new ResendVerificationCommand("ali@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await context.VerificationTokens.Received(1)
            .InvalidateActiveAsync(user.Id, VerificationPurpose.EmailVerification, Users.Now, Arg.Any<CancellationToken>());
        var token = Assert.Single(context.AddedVerificationTokens);
        Assert.Same(VerificationPurpose.EmailVerification, token.Purpose);
        Assert.Equal($"verify:{context.OpaqueTokens.Issued.Single().Value}", Assert.Single(context.SentEmails()).TextBody);
    }
}

public sealed class ForgotPasswordHandlerTests
{
    // Delegated rather than assembled again: this helper drifted from the harness the moment the
    // handler took a new collaborator, and a second copy of a constructor call is a second place to
    // remember.
    private static ForgotPasswordHandler Handler(AuthHandlerTestContext context) => context.ForgotPassword();

    [Fact]
    public async Task Unknown_accounts_succeed_silently()
    {
        var context = new AuthHandlerTestContext();

        var result = await Handler(context).Handle(new ForgotPasswordCommand("ghost@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(context.SentEmails());
    }

    [Fact]
    public async Task Known_accounts_get_a_reset_link_with_the_short_lifetime()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());

        var result = await Handler(context).Handle(new ForgotPasswordCommand("Ali@Example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await context.VerificationTokens.Received(1)
            .InvalidateActiveAsync(user.Id, VerificationPurpose.PasswordReset, Users.Now, Arg.Any<CancellationToken>());
        var token = Assert.Single(context.AddedVerificationTokens);
        Assert.Same(VerificationPurpose.PasswordReset, token.Purpose);
        Assert.Equal(Users.Now.AddMinutes(60), token.ExpiresAt);
        Assert.Equal($"reset:{context.OpaqueTokens.Issued.Single().Value}", Assert.Single(context.SentEmails()).TextBody);
    }
}

public sealed class ResetPasswordHandlerTests
{
    private static ResetPasswordHandler Handler(AuthHandlerTestContext context) => new(
        context.VerificationTokens,
        context.UserRepository,
        context.Hasher,
        context.OpaqueTokens,
        context.Policy,
        context.Clock,
        context.UnitOfWork,
        context.Emails);

    private static VerificationToken StoredReset(AuthHandlerTestContext context, User user, out string raw)
    {
        var generated = context.OpaqueTokens.Generate();
        raw = generated.Value;
        var token = VerificationToken.Issue(user.Id, VerificationPurpose.PasswordReset, generated.Hash, Users.Now, TimeSpan.FromMinutes(60));
        context.VerificationTokens.GetByHashAsync(generated.Hash, VerificationPurpose.PasswordReset, Arg.Any<CancellationToken>()).Returns(token);
        return token;
    }

    [Fact]
    public async Task Weak_password_is_rejected_before_the_token_is_touched()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        var token = StoredReset(context, user, out var raw);

        var result = await Handler(context).Handle(new ResetPasswordCommand(raw, "weak"), CancellationToken.None);

        Assert.Equal("auth.password_policy", result.Error.Code);
        Assert.False(token.IsConsumed);
    }

    [Fact]
    public async Task Valid_link_changes_the_password_consumes_the_token_and_raises_the_revocation_event()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        var token = StoredReset(context, user, out var raw);

        var result = await Handler(context).Handle(new ResetPasswordCommand(raw, "NewPass99"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("hashed:NewPass99", user.PasswordHash.Value);
        Assert.True(token.IsConsumed);
        Assert.Contains(user.DomainEvents, domainEvent => domainEvent is UserPasswordChanged);
        Assert.Equal("changed", Assert.Single(context.SentEmails()).Subject);
    }

    [Fact]
    public async Task Links_are_single_use()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        StoredReset(context, user, out var raw);

        var first = await Handler(context).Handle(new ResetPasswordCommand(raw, "NewPass99"), CancellationToken.None);
        var second = await Handler(context).Handle(new ResetPasswordCommand(raw, "Other1234"), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal("auth.invalid_token", second.Error.Code);
        Assert.Equal("hashed:NewPass99", user.PasswordHash.Value);
    }

    [Fact]
    public async Task Unknown_link_is_invalid()
    {
        var context = new AuthHandlerTestContext();

        var result = await Handler(context).Handle(new ResetPasswordCommand("nope", "NewPass99"), CancellationToken.None);

        Assert.Equal("auth.invalid_token", result.Error.Code);
    }
}

public sealed class RevokeSessionsEventHandlerTests
{
    [Fact]
    public async Task Security_events_revoke_every_refresh_family_of_the_user()
    {
        var context = new AuthHandlerTestContext();
        var userId = Id.New();

        await new Khadra.Application.IdentityAccess.EventHandlers.RevokeSessionsOnPasswordChanged(context.RefreshTokens, context.Clock)
            .HandleAsync(new UserPasswordChanged(userId, Users.Now));
        await new Khadra.Application.IdentityAccess.EventHandlers.RevokeSessionsOnSuspended(context.RefreshTokens, context.Clock)
            .HandleAsync(new UserSuspended(userId, "x", Users.Now));
        await new Khadra.Application.IdentityAccess.EventHandlers.RevokeSessionsOnDeleted(context.RefreshTokens, context.Clock)
            .HandleAsync(new UserDeleted(userId, Users.Now));

        await context.RefreshTokens.Received(3).RevokeAllForUserAsync(userId, Users.Now, Arg.Any<CancellationToken>());
    }
}
