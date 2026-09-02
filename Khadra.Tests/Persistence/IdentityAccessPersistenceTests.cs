using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Khadra.Tests.Persistence;

// Round-trips the aggregates through the real EF model on SQLite (in-memory) to prove the value-object
// and smart-enum conversions, indexes, soft-delete filter and bulk revocations behave.
public sealed class IdentityAccessPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public IdentityAccessPersistenceTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseSqlite(_connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new KhadraDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private KhadraDbContext NewContext() => new(_options);

    [Fact]
    public async Task User_round_trips_with_value_objects_and_smart_enums()
    {
        var user = Users.Customer();
        await using (var context = NewContext())
        {
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var loaded = await new UserRepository(reader).GetByEmailAsync(EmailAddress.Create("ALI@example.com").Value);

        Assert.NotNull(loaded);
        Assert.Equal(user.Id, loaded.Id);
        Assert.Equal("+962791234567", loaded.Phone.Value);
        Assert.Equal("Ali Ahmad", loaded.Name.Value);
        Assert.Same(UserRole.Customer, loaded.Role);
        Assert.Same(UserStatus.Active, loaded.Status);
        Assert.True(loaded.IsEmailVerified);
        Assert.Equal(user.SecurityStamp, loaded.SecurityStamp);
    }

    [Fact]
    public async Task Email_and_phone_are_unique_even_across_soft_deleted_rows()
    {
        var first = Users.Customer();
        await using (var context = NewContext())
        {
            context.Users.Add(first);
            await context.SaveChangesAsync();
            first.Delete(Users.Now);
            await context.SaveChangesAsync();
        }

        await using var context2 = NewContext();
        var repository = new UserRepository(context2);
        Assert.Null(await repository.GetByEmailAsync(first.Email));
        Assert.True(await repository.ExistsByEmailAsync(first.Email));
        Assert.True(await repository.ExistsByPhoneAsync(first.Phone));

        context2.Users.Add(Users.Customer());
        await Assert.ThrowsAsync<DbUpdateException>(() => context2.SaveChangesAsync());
    }

    [Fact]
    public async Task Modified_aggregates_get_an_updated_at_stamp()
    {
        var user = Users.Customer();
        await using (var context = NewContext())
        {
            context.Users.Add(user);
            await context.SaveChangesAsync();
            Assert.Null(context.Entry(user).Property<DateTimeOffset?>(KhadraDbContext.UpdatedAtShadowProperty).CurrentValue);

            user.RecordSuccessfulLogin(Users.Now.AddHours(1));
            await context.SaveChangesAsync();
            Assert.NotNull(context.Entry(user).Property<DateTimeOffset?>(KhadraDbContext.UpdatedAtShadowProperty).CurrentValue);
        }
    }

    [Fact]
    public async Task Refresh_tokens_persist_and_bulk_revocations_only_touch_active_rows()
    {
        var user = Users.Customer();
        var tokens = new FakeOpaqueTokens();
        var first = Users.ActiveRefreshToken(user, tokens, Users.Now, out var firstRaw);
        var second = Users.ActiveRefreshToken(user, tokens, Users.Now, out _);
        var alreadyRevoked = Users.ActiveRefreshToken(user, tokens, Users.Now, out _);
        alreadyRevoked.Revoke(Users.Now.AddMinutes(-5));

        await using (var context = NewContext())
        {
            context.Users.Add(user);
            context.RefreshTokens.AddRange(first, second, alreadyRevoked);
            await context.SaveChangesAsync();
        }

        await using var context2 = NewContext();
        var repository = new RefreshTokenRepository(context2);
        var loaded = await repository.GetByHashAsync(tokens.Hash(firstRaw));
        Assert.NotNull(loaded);
        Assert.Equal(first.FamilyId, loaded.FamilyId);
        Assert.Equal(user.Id, loaded.UserId);

        var revoked = await repository.RevokeAllForUserAsync(user.Id, Users.Now);
        Assert.Equal(2, revoked);
        Assert.Equal(0, await repository.RevokeFamilyAsync(first.FamilyId, Users.Now));
        Assert.Equal(Users.Now.AddMinutes(-5), (await context2.RefreshTokens.SingleAsync(token => token.Id == alreadyRevoked.Id)).RevokedAt);
    }

    [Fact]
    public async Task Verification_tokens_are_looked_up_by_hash_and_purpose_and_invalidated_in_bulk()
    {
        var user = Users.Customer(verified: false);
        var hashA = new string('a', 64);
        var hashB = new string('b', 64);
        var hashC = new string('c', 64);
        await using (var context = NewContext())
        {
            context.Users.Add(user);
            context.VerificationTokens.AddRange(
                VerificationToken.Issue(user.Id, VerificationPurpose.EmailVerification, hashA, Users.Now, TimeSpan.FromHours(24)),
                VerificationToken.Issue(user.Id, VerificationPurpose.EmailVerification, hashB, Users.Now, TimeSpan.FromHours(24)),
                VerificationToken.Issue(user.Id, VerificationPurpose.PasswordReset, hashC, Users.Now, TimeSpan.FromHours(1)));
            await context.SaveChangesAsync();
        }

        await using var context2 = NewContext();
        var repository = new VerificationTokenRepository(context2);
        Assert.NotNull(await repository.GetByHashAsync(hashA, VerificationPurpose.EmailVerification));
        Assert.Null(await repository.GetByHashAsync(hashA, VerificationPurpose.PasswordReset));

        var invalidated = await repository.InvalidateActiveAsync(user.Id, VerificationPurpose.EmailVerification, Users.Now);

        Assert.Equal(2, invalidated);
        Assert.Null((await repository.GetByHashAsync(hashC, VerificationPurpose.PasswordReset))!.ConsumedAt);
    }

    [Fact]
    public async Task Unit_of_work_dispatches_domain_events_only_after_a_successful_commit()
    {
        var dispatcher = Substitute.For<IDomainEventDispatcher>();
        var user = User.RegisterCustomer(
            EmailAddress.Create("event@example.com").Value,
            PhoneNumber.Create("0791112223").Value,
            PersonName.Create("Event Tester").Value,
            PasswordHash.FromHash("hashed:x"),
            Users.Now);

        await using var context = NewContext();
        context.Users.Add(user);
        var unitOfWork = new UnitOfWork(context, dispatcher);

        await unitOfWork.SaveChangesAsync();

        await dispatcher.Received(1).DispatchAsync(
            Arg.Is<IReadOnlyCollection<IDomainEvent>>(events => events.Count == 1),
            Arg.Any<CancellationToken>());
        Assert.Empty(user.DomainEvents);
    }
}
