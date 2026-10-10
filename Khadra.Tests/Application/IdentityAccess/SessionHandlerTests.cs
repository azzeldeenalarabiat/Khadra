using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.ChangePassword;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Application.IdentityAccess.GetCurrentUser;
using Khadra.Application.Common.Ports;
using Khadra.Application.IdentityAccess.Logout;
using Khadra.Application.IdentityAccess.MySecurity;
using Khadra.Application.IdentityAccess.ReadModels;
using Khadra.Application.IdentityAccess.RefreshTokens;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Domain.IdentityAccess.Events;
using Khadra.Domain.IdentityAccess.Repositories;
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

    [Fact]
    public async Task The_access_token_carries_the_family_it_belongs_to_across_a_rotation()
    {
        // The claim is what lets the server say which of somebody's devices is asking, on their own
        // Registered Devices screen. A family survives every rotation, so the value has to survive
        // one too — an id that changed on each refresh would mark a different row every quarter of
        // an hour, which is worse than marking none.
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        var current = Stored(context, user, out var raw);
        context.Clock.Advance(TimeSpan.FromHours(1));

        var result = await Handler(context).Handle(new RefreshTokensCommand(raw, Client), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(current.FamilyId, Assert.Single(context.AccessTokens.IssuedFor));
    }

    /// <summary>
    /// The loser of a race is told to try again, never that the session is over, and the family survives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Killing the family here was backwards: two refreshes of one token can only race because a client sent both,
    /// and the WINNER committed first holding a perfectly good replacement.
    /// </para>
    /// <para>
    /// Refusing the loser with 401 <c>auth.invalid_refresh_token</c> was the same mistake one step later (pre-launch
    /// item 240). Every client reads a 401 from the refresh endpoint as a verdict and ends the session, and the app
    /// never presents again after one. A loser whose answer the client WAS waiting for — the app's second
    /// presentation after a timeout, while the first is still in the handler — signed the customer out of a live
    /// family. The loser is answered 503 <c>auth.refresh_conflict</c> instead. Every build from 1.1.0 keeps its token
    /// on a 5xx, so a client holding the winner's answer keeps it, and one that is not presents again later, where the
    /// reuse grace hands it the winner's replacement.
    /// </para>
    /// <para>
    /// Not a fresh pair issued to the loser through that grace path: when the client is listening to the WINNER, that
    /// would retire the very token it was just handed, and its next rotation would be a replay.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_lost_race_tells_the_loser_to_try_again_and_never_ends_the_session()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        var current = Stored(context, user, out var raw);
        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new ConcurrencyConflictException("xmin changed"));

        var result = await Handler(context).Handle(new RefreshTokensCommand(raw, Client), CancellationToken.None);

        Assert.Equal("auth.refresh_conflict", result.Error.Code);
        Assert.Equal(ErrorKind.Unavailable, result.Error.Kind);
        Assert.NotEqual(ErrorKind.Unauthorized, result.Error.Kind);
        await context.RefreshTokens.DidNotReceive()
            .RevokeFamilyAsync(current.FamilyId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        context.UnitOfWork.Received(1).DiscardChanges();
    }

    /// <summary>The loser issues nothing of its own: the winner's replacement is the only live token.</summary>
    [Fact]
    public async Task A_lost_race_retires_nothing_and_hands_out_nothing()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        Stored(context, user, out var raw);
        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new ConcurrencyConflictException("xmin changed"));

        var result = await Handler(context).Handle(new RefreshTokensCommand(raw, Client), CancellationToken.None);

        Assert.True(result.IsFailure);
        // One save attempted, the one that lost. No second pass through the grace path that would issue a pair
        // from, and so retire, the winner's replacement.
        await context.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await context.RefreshTokens.DidNotReceive().GetByIdAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>());
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
    /// Two in-grace retries that race on the replacement: the loser is told to try again, as on the direct path.
    /// </summary>
    /// <remarks>
    /// Both find the same unused replacement and both rotate it; the second save loses on its <c>xmin</c>. The answer
    /// comes from the same place as the direct path's (the conflict is caught where both issue), so it is 503
    /// <c>auth.refresh_conflict</c> and the family stands (pre-launch item 240).
    /// </remarks>
    [Fact]
    public async Task Two_retries_that_race_inside_the_grace_tell_the_loser_to_try_again()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        var current = Stored(context, user, out var raw);
        var replacement = Users.ActiveRefreshToken(user, context.OpaqueTokens, Users.Now, out _);
        current.Rotate(Users.Now, replacement.Id);
        context.RefreshTokens.GetByIdAsync(replacement.Id, Arg.Any<CancellationToken>()).Returns(replacement);
        context.Clock.UtcNow = Users.Now.AddSeconds(5);
        context.UnitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new ConcurrencyConflictException("xmin changed"));

        var result = await Handler(context).Handle(new RefreshTokensCommand(raw, Client), CancellationToken.None);

        Assert.Equal("auth.refresh_conflict", result.Error.Code);
        Assert.Equal(ErrorKind.Unavailable, result.Error.Kind);
        await context.RefreshTokens.DidNotReceive()
            .RevokeFamilyAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        context.UnitOfWork.Received(1).DiscardChanges();
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
    private readonly IPushDeviceRepository pushDevices = Substitute.For<IPushDeviceRepository>();

    private LogoutHandler Handler(AuthHandlerTestContext context) =>
        new(context.RefreshTokens, pushDevices, context.OpaqueTokens, context.Clock);

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
        // The phone that signed out is no longer woken for this account.
        await pushDevices.Received(1).RevokeForSessionAsync(user.Id, token.FamilyId, Users.Now, Arg.Any<CancellationToken>());
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
        await pushDevices.DidNotReceive().RevokeForSessionAsync(
            Arg.Any<Id>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
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
        var handler = new GetCurrentUserHandler(context.UserRepository, context.Legal.Consents, context.Legal.Site, context.Clock);

        var found = await handler.Handle(new GetCurrentUserQuery(user.Id), CancellationToken.None);
        var missing = await handler.Handle(new GetCurrentUserQuery(Id.New()), CancellationToken.None);

        Assert.Equal("Ali Ahmad", found.Value.FullName);
        Assert.Equal("Customer", found.Value.Role);
        Assert.True(found.Value.IsEmailVerified);
        Assert.Equal("auth.user_not_found", missing.Error.Code);
    }

    /// <summary>
    /// Every installed customer app reads <c>/auth/me</c>'s fields at the top level (the advisor's review, blocking): the
    /// consent list is added BESIDE them, and the answer stays a superset of <see cref="UserDto"/>, name for name.
    /// </summary>
    [Fact]
    public async Task The_answer_stays_flat_and_carries_every_field_an_installed_app_reads()
    {
        var context = new AuthHandlerTestContext();
        var user = context.KnownUser(Users.Customer());
        var handler = new GetCurrentUserHandler(context.UserRepository, context.Legal.Consents, context.Legal.Site, context.Clock);

        var current = (await handler.Handle(new GetCurrentUserQuery(user.Id), CancellationToken.None)).Value;

        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var before = System.Text.Json.JsonSerializer.SerializeToElement(UserDto.From(user), web)
            .EnumerateObject().Select(property => property.Name).ToList();
        var now = System.Text.Json.JsonSerializer.SerializeToElement(current, web);
        var names = now.EnumerateObject().Select(property => property.Name).ToList();

        Assert.Subset(names.ToHashSet(), before.ToHashSet());
        Assert.Contains("pendingConsents", names);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, now.GetProperty("pendingConsents").ValueKind);
        // Flat: the user's own fields are values, never an object wrapped around them.
        Assert.Equal(System.Text.Json.JsonValueKind.String, now.GetProperty("email").ValueKind);
    }

    [Fact]
    public async Task It_names_the_texts_still_to_accept_and_never_asks_an_administrator()
    {
        var context = new AuthHandlerTestContext();
        var customer = context.KnownUser(Users.Customer());
        var admin = context.KnownUser(User.CreateInvitedAdmin(
            EmailAddress.Create("staff@khadra.jo").Value,
            PhoneNumber.Create("0790000001").Value,
            PersonName.Create("Dana Saleh").Value,
            PasswordHash.FromHash("unusable"),
            Users.Now));
        var terms = new Khadra.Application.Legal.ReadModels.PendingLegalVersion(
            Khadra.Domain.Legal.LegalDocumentKind.Terms, Id.New(), "2026-10", TestLegal.Published);
        context.Legal.Consents.PendingAsync(Arg.Any<Id>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([terms]);
        var handler = new GetCurrentUserHandler(context.UserRepository, context.Legal.Consents, context.Legal.Site, context.Clock);

        var forCustomer = (await handler.Handle(new GetCurrentUserQuery(customer.Id), CancellationToken.None)).Value;
        var forAdmin = (await handler.Handle(new GetCurrentUserQuery(admin.Id), CancellationToken.None)).Value;

        var pending = Assert.Single(forCustomer.PendingConsents);
        Assert.Equal(("Terms", terms.VersionId.Value), (pending.Kind, pending.VersionId));
        Assert.Empty(forAdmin.PendingConsents);
    }
}

/// <summary>
/// Which of somebody's devices is the one they are holding.
/// </summary>
/// <remarks>
/// The screen used to have no way to know, and the obvious guess — the most recently used active
/// row — is wrong on this data: <c>LastUsedAt</c> is the last REFRESH, so a second phone that
/// rotated a minute ago outranks the one in your hand. The answer comes from the caller's own
/// access token instead, which is the only place it exists.
/// </remarks>
public sealed class GetMySessionsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);

    private static SessionSummary Session(Guid familyId, string agent) =>
        new(familyId, Now.AddDays(-2), Now.AddMinutes(-1), Now.AddDays(12), "10.0.0.5", agent, IsActive: true);

    private static (GetMySessionsHandler Handler, ICurrentActor Actor) Build(
        Id userId,
        params SessionSummary[] sessions)
    {
        var reader = Substitute.For<ISessionReader>();
        reader.ListForUserAsync(userId, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<SessionSummary>>(_ => sessions);

        var tokens = Substitute.For<IAccessTokenSettings>();
        tokens.AccessTokenMinutes.Returns(15);

        var actor = Substitute.For<ICurrentActor>();
        actor.UserId.Returns(userId);

        return (new GetMySessionsHandler(reader, tokens, actor), actor);
    }

    [Fact]
    public async Task Marks_the_session_whose_family_the_access_token_names()
    {
        var userId = Id.New();
        var thisPhone = Guid.NewGuid();
        var otherPhone = Guid.NewGuid();
        var (handler, actor) = Build(userId, Session(thisPhone, "Khadra (Android 14)"), Session(otherPhone, "Khadra (iOS 18)"));
        actor.SessionId.Returns(thisPhone);

        var result = await handler.Handle(new GetMySessionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Sessions.Single(session => session.FamilyId == thisPhone).IsCurrent);
        Assert.False(result.Value.Sessions.Single(session => session.FamilyId == otherPhone).IsCurrent);
    }

    [Fact]
    public async Task Marks_nothing_for_a_token_issued_before_the_claim_existed()
    {
        // Worth at most one access token's lifetime after a deploy, and the honest answer is that
        // this build cannot tell — not a guess that would put "This device" on the wrong row.
        var userId = Id.New();
        var (handler, actor) = Build(userId, Session(Guid.NewGuid(), "Khadra (Android 14)"));
        actor.SessionId.Returns((Guid?)null);

        var result = await handler.Handle(new GetMySessionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.All(result.Value.Sessions, session => Assert.False(session.IsCurrent));
    }
}
