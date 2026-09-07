using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.ChangePassword;
using Khadra.Application.IdentityAccess.GetCurrentUser;
using Khadra.Application.IdentityAccess.Logout;
using Khadra.Application.IdentityAccess.RefreshTokens;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Events;
using Khadra.Tests.Support;
using NSubstitute;

namespace Khadra.Tests.Application.IdentityAccess;

public sealed class RefreshTokensHandlerTests
{
    private static readonly ClientInfo Client = new("10.0.0.5", "xunit");

    private static RefreshTokensHandler Handler(AuthHandlerTestContext context) => new(
        context.RefreshTokens,
        context.UserRepository,
        context.OpaqueTokens,
        context.TokenFactory,
        context.Policy,
        context.Clock,
        context.UnitOfWork);

    private static RefreshToken Stored(AuthHandlerTestContext context, User user, out string raw)
    {
        var token = Users.ActiveRefreshToken(user, context.OpaqueTokens, Users.Now, out raw);
        context.RefreshTokens.GetByHashAsync(token.TokenHash, Arg.Any<CancellationToken>()).Returns(token);
        return token;
    }

    [Fact]
    public async Task Unknown_token_is_rejected()
    {
        var context = new AuthHandlerTestContext();

        var result = await Handler(context).Handle(new RefreshTokensCommand("nope", Client), CancellationToken.None);

        Assert.Equal("auth.invalid_refresh_token", result.Error.Code);
    }

    [Fact]
    public async Task Replaying_a_rotated_token_revokes_the_whole_family()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        var token = Stored(context, user, out var raw);
        token.Rotate(Users.Now, Id.New());

        var result = await Handler(context).Handle(new RefreshTokensCommand(raw, Client), CancellationToken.None);

        Assert.Equal("auth.invalid_refresh_token", result.Error.Code);
        await context.RefreshTokens.Received(1).RevokeFamilyAsync(token.FamilyId, Users.Now, Arg.Any<CancellationToken>());
        Assert.Empty(context.AddedRefreshTokens);
    }

    [Fact]
    public async Task Expired_token_is_rejected_without_revoking_the_family()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        Stored(context, user, out var raw);
        context.Clock.Advance(TimeSpan.FromDays(15));

        var result = await Handler(context).Handle(new RefreshTokensCommand(raw, Client), CancellationToken.None);

        Assert.Equal("auth.invalid_refresh_token", result.Error.Code);
        await context.RefreshTokens.DidNotReceive().RevokeFamilyAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suspended_user_cannot_refresh()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        user.Suspend("fraud", Users.Now);
        Stored(context, user, out var raw);

        var result = await Handler(context).Handle(new RefreshTokensCommand(raw, Client), CancellationToken.None);

        Assert.Equal("auth.account_suspended", result.Error.Code);
    }

    [Fact]
    public async Task Success_rotates_into_the_same_family()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        var current = Stored(context, user, out var raw);
        context.Clock.Advance(TimeSpan.FromHours(1));

        var result = await Handler(context).Handle(new RefreshTokensCommand(raw, Client), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var replacement = Assert.Single(context.AddedRefreshTokens);
        Assert.True(current.IsRevoked);
        Assert.Equal(replacement.Id, current.ReplacedByTokenId);
        Assert.Equal(current.FamilyId, replacement.FamilyId);
        Assert.Equal(context.OpaqueTokens.Hash(result.Value.RefreshToken), replacement.TokenHash);
        Assert.NotEqual(raw, result.Value.RefreshToken);
    }

    /// <summary>
    /// The loser of a race is refused, and the family survives.
    /// </summary>
    /// <remarks>
    /// This used to kill the family, and that was backwards. Two refreshes of one token can only
    /// race because a client sent both; the WINNER committed first and is holding a perfectly good
    /// replacement, so revoking the family destroys a token the customer legitimately has, over a
    /// bug on their own device. The loser is simply refused, and on its retry the reuse grace hands
    /// it the winner's replacement.
    /// </remarks>
    [Fact]
    public async Task A_lost_race_refuses_the_loser_without_killing_the_winners_session()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        var current = Stored(context, user, out var raw);
        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new ConcurrencyConflictException("xmin changed"));

        var result = await Handler(context).Handle(new RefreshTokensCommand(raw, Client), CancellationToken.None);

        Assert.Equal("auth.invalid_refresh_token", result.Error.Code);
        await context.RefreshTokens.DidNotReceive()
            .RevokeFamilyAsync(current.FamilyId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A refresh whose RESPONSE was lost is a retry, not a replay.
    /// </summary>
    /// <remarks>
    /// The request arrived, the rotation committed, and the reply never reached the phone — a radio
    /// handover, a tunnel, the app suspended. The customer still holds the old token. Presenting it
    /// again is indistinguishable from an attack by shape, and distinguishable by two facts: it
    /// happened seconds ago, and nobody has spent the replacement.
    /// </remarks>
    [Fact]
    public async Task A_lost_response_hands_back_the_replacement_the_customer_never_received()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        var current = Stored(context, user, out var raw);

        // The rotation that committed, and the replacement whose response was lost.
        var replacement = Users.ActiveRefreshToken(user, context.OpaqueTokens, Users.Now, out _);
        current.Rotate(Users.Now, replacement.Id);
        context.RefreshTokens.GetByIdAsync(replacement.Id, Arg.Any<CancellationToken>()).Returns(replacement);
        context.Clock.UtcNow = Users.Now.AddSeconds(5);

        var result = await Handler(context).Handle(new RefreshTokensCommand(raw, Client), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(raw, result.Value.RefreshToken);
        // The session is intact: nothing was revoked wholesale.
        await context.RefreshTokens.DidNotReceive()
            .RevokeFamilyAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Past the grace, replay detection is exactly as strict as it was.
    /// </summary>
    [Fact]
    public async Task An_old_consumed_token_is_still_replay_and_still_kills_the_family()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        var current = Stored(context, user, out var raw);

        var replacement = Users.ActiveRefreshToken(user, context.OpaqueTokens, Users.Now, out _);
        current.Rotate(Users.Now, replacement.Id);
        context.RefreshTokens.GetByIdAsync(replacement.Id, Arg.Any<CancellationToken>()).Returns(replacement);
        // Well outside the sixty-second window: a client whose response was lost comes back in
        // seconds, so an hour later this can only be someone else's copy.
        context.Clock.UtcNow = Users.Now.AddHours(1);

        var result = await Handler(context).Handle(new RefreshTokensCommand(raw, Client), CancellationToken.None);

        Assert.Equal("auth.invalid_refresh_token", result.Error.Code);
        await context.RefreshTokens.Received(1)
            .RevokeFamilyAsync(current.FamilyId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// If the replacement has been SPENT, two parties hold live tokens. That is the attack the
    /// whole mechanism exists to catch, and the grace does not soften it.
    /// </summary>
    [Fact]
    public async Task A_replacement_that_someone_has_already_used_is_a_real_replay()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        var current = Stored(context, user, out var raw);

        var replacement = Users.ActiveRefreshToken(user, context.OpaqueTokens, Users.Now, out _);
        current.Rotate(Users.Now, replacement.Id);
        // Spent, and with nothing after it: the chain ends at a used token.
        replacement.Revoke(Users.Now);
        context.RefreshTokens.GetByIdAsync(replacement.Id, Arg.Any<CancellationToken>()).Returns(replacement);
        context.Clock.UtcNow = Users.Now.AddSeconds(5);

        var result = await Handler(context).Handle(new RefreshTokensCommand(raw, Client), CancellationToken.None);

        Assert.Equal("auth.invalid_refresh_token", result.Error.Code);
        await context.RefreshTokens.Received(1)
            .RevokeFamilyAsync(current.FamilyId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}

public sealed class LogoutHandlerTests
{
    private static LogoutHandler Handler(AuthHandlerTestContext context) =>
        new(context.RefreshTokens, context.OpaqueTokens, context.Clock);

    [Fact]
    public async Task Revokes_the_presented_family_when_it_belongs_to_the_caller()
    {
        var context = new AuthHandlerTestContext();
        var user = Users.Customer();
        var token = Users.ActiveRefreshToken(user, context.OpaqueTokens, Users.Now, out var raw);
        context.RefreshTokens.GetByHashAsync(token.TokenHash, Arg.Any<CancellationToken>()).Returns(token);

        var result = await Handler(context).Handle(new LogoutCommand(user.Id, raw, AllDevices: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await context.RefreshTokens.Received(1).RevokeFamilyAsync(token.FamilyId, Users.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_tokens_of_other_users_and_unknown_tokens()
    {
        var context = new AuthHandlerTestContext();
        var owner = Users.Customer();
        var token = Users.ActiveRefreshToken(owner, context.OpaqueTokens, Users.Now, out var raw);
        context.RefreshTokens.GetByHashAsync(token.TokenHash, Arg.Any<CancellationToken>()).Returns(token);

        var foreign = await Handler(context).Handle(new LogoutCommand(Id.New(), raw, AllDevices: false), CancellationToken.None);
        var unknown = await Handler(context).Handle(new LogoutCommand(owner.Id, "unknown", AllDevices: false), CancellationToken.None);

        Assert.True(foreign.IsSuccess);
        Assert.True(unknown.IsSuccess);
        await context.RefreshTokens.DidNotReceive().RevokeFamilyAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task All_devices_revokes_every_family_of_the_caller()
    {
        var context = new AuthHandlerTestContext();
        var user = Users.Customer();

        var result = await Handler(context).Handle(new LogoutCommand(user.Id, "anything", AllDevices: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await context.RefreshTokens.Received(1).RevokeAllForUserAsync(user.Id, Users.Now, Arg.Any<CancellationToken>());
    }
}

public sealed class ChangePasswordHandlerTests
{
    private static readonly ClientInfo Client = new("10.0.0.5", "xunit");

    private static ChangePasswordHandler Handler(AuthHandlerTestContext context) => new(
        context.UserRepository,
        context.Hasher,
        context.Policy,
        context.TokenFactory,
        context.Clock,
        context.UnitOfWork,
        context.Emails);

    [Fact]
    public async Task Wrong_current_password_is_invalid_credentials()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());

        var result = await Handler(context).Handle(new ChangePasswordCommand(user.Id, "Nope12345", "NewPass99", Client), CancellationToken.None);

        Assert.Equal("auth.invalid_credentials", result.Error.Code);
        Assert.Equal("hashed:Passw0rd1", user.PasswordHash.Value);
    }

    [Fact]
    public async Task Weak_new_password_is_rejected()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());

        var result = await Handler(context).Handle(new ChangePasswordCommand(user.Id, "Passw0rd1", "short", Client), CancellationToken.None);

        Assert.Equal("auth.password_policy", result.Error.Code);
    }

    [Fact]
    public async Task Success_changes_the_password_commits_then_issues_a_fresh_family_and_notifies()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());

        var result = await Handler(context).Handle(new ChangePasswordCommand(user.Id, "Passw0rd1", "NewPass99", Client), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("hashed:NewPass99", user.PasswordHash.Value);
        Assert.Contains(user.DomainEvents, domainEvent => domainEvent is UserPasswordChanged);
        Assert.Single(context.AddedRefreshTokens);
        await context.UnitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal("changed", Assert.Single(context.SentEmails()).Subject);
    }

    [Fact]
    public async Task Unknown_user_is_not_found()
    {
        var context = new AuthHandlerTestContext();

        var result = await Handler(context).Handle(new ChangePasswordCommand(Id.New(), "Passw0rd1", "NewPass99", Client), CancellationToken.None);

        Assert.Equal("auth.user_not_found", result.Error.Code);
    }
}

public sealed class GetCurrentUserHandlerTests
{
    [Fact]
    public async Task Maps_the_user_or_reports_not_found()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        var handler = new GetCurrentUserHandler(context.UserRepository);

        var found = await handler.Handle(new GetCurrentUserQuery(user.Id), CancellationToken.None);
        var missing = await handler.Handle(new GetCurrentUserQuery(Id.New()), CancellationToken.None);

        Assert.Equal("Ali Ahmad", found.Value.FullName);
        Assert.Equal("Customer", found.Value.Role);
        Assert.True(found.Value.IsEmailVerified);
        Assert.Equal("auth.user_not_found", missing.Error.Code);
    }
}
