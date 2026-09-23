using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Persistence.Repositories;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Which phones a push may reach. The session join is the guard that matters: every way a session
/// ends — sign-out, revoke, expiry — must silence the phone, whether or not anyone revoked the
/// device row.
/// </summary>
public sealed class PushDevicePersistenceTests : IDisposable
{
    private static readonly DateTimeOffset Now = Users.Now;
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public PushDevicePersistenceTests()
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

    private static RefreshToken Session(User user, TimeSpan? lifetime = null) =>
        RefreshToken.IssueNewFamily(
            user.Id,
            new string('a', 48) + Guid.NewGuid().ToString("N")[..16],
            Now,
            lifetime ?? TimeSpan.FromDays(14),
            TimeSpan.FromDays(30),
            null,
            null);

    private async Task<(User User, RefreshToken Session, PushDevice Device)> Seed(string token = "fcm-token-1")
    {
        var user = Users.Customer();
        var session = Session(user);
        var device = PushDevice.Register(token, PushPlatform.Android, user.Id, session.FamilyId, Language.Arabic, "1.2.0+3", Now);
        await using var context = NewContext();
        context.Users.Add(user);
        context.RefreshTokens.Add(session);
        context.PushDevices.Add(device);
        await context.SaveChangesAsync();
        return (user, session, device);
    }

    [Fact]
    public async Task A_device_round_trips_with_its_platform_language_and_session()
    {
        var (_, session, device) = await Seed();

        await using var context = NewContext();
        var loaded = await new PushDeviceRepository(context).GetByTokenAsync("fcm-token-1");

        Assert.NotNull(loaded);
        Assert.Equal(device.Id, loaded.Id);
        Assert.Same(PushPlatform.Android, loaded.Platform);
        Assert.Same(Language.Arabic, loaded.Language);
        Assert.Equal(session.FamilyId, loaded.SessionFamilyId);
        Assert.Equal("1.2.0+3", loaded.AppVersion);
        Assert.Null(loaded.RevokedAt);
    }

    [Fact]
    public async Task A_device_on_a_live_session_is_deliverable()
    {
        var (user, _, device) = await Seed();

        await using var context = NewContext();
        var deliverable = await new PushDeviceRepository(context).ListDeliverableAsync(user.Id, Now.AddMinutes(1));

        Assert.Equal([device.Id], deliverable.Select(d => d.Id));
    }

    [Fact]
    public async Task Revoking_the_session_silences_the_device_even_if_nobody_revoked_the_device()
    {
        var (user, session, _) = await Seed();
        await using (var context = NewContext())
            await new RefreshTokenRepository(context).RevokeFamilyAsync(session.FamilyId, Now);

        await using var reader = NewContext();
        Assert.Empty(await new PushDeviceRepository(reader).ListDeliverableAsync(user.Id, Now.AddMinutes(1)));
    }

    [Fact]
    public async Task An_expired_session_silences_the_device()
    {
        var (user, _, _) = await Seed();

        await using var context = NewContext();
        Assert.Empty(await new PushDeviceRepository(context).ListDeliverableAsync(user.Id, Now.AddDays(15)));
    }

    [Fact]
    public async Task A_revoked_device_is_not_deliverable_and_re_registering_revives_it()
    {
        var (user, session, _) = await Seed();
        await using (var context = NewContext())
            Assert.Equal(1, await new PushDeviceRepository(context).RevokeForSessionAsync(user.Id, session.FamilyId, Now));

        await using (var context = NewContext())
            Assert.Empty(await new PushDeviceRepository(context).ListDeliverableAsync(user.Id, Now.AddMinutes(1)));

        await using (var context = NewContext())
        {
            var device = await new PushDeviceRepository(context).GetByTokenAsync("fcm-token-1");
            device!.Reregister(PushPlatform.Android, user.Id, session.FamilyId, Language.English, "1.2.0+3", Now.AddMinutes(2));
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        var revived = Assert.Single(await new PushDeviceRepository(reader).ListDeliverableAsync(user.Id, Now.AddMinutes(3)));
        Assert.Same(Language.English, revived.Language);
    }

    [Fact]
    public async Task Another_persons_session_does_not_make_a_device_deliverable()
    {
        var (user, _, _) = await Seed();
        var stranger = Users.Customer(email: "stranger@example.com", phone: "+962791111111");
        await using (var context = NewContext())
        {
            context.Users.Add(stranger);
            context.RefreshTokens.Add(Session(stranger));
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        Assert.Empty(await new PushDeviceRepository(reader).ListDeliverableAsync(stranger.Id, Now.AddMinutes(1)));
        Assert.Single(await new PushDeviceRepository(reader).ListDeliverableAsync(user.Id, Now.AddMinutes(1)));
    }

    [Fact]
    public async Task The_same_token_twice_is_refused_by_the_database()
    {
        var (user, session, _) = await Seed();

        await using var context = NewContext();
        context.PushDevices.Add(PushDevice.Register("fcm-token-1", PushPlatform.Android, user.Id, session.FamilyId, Language.Arabic, null, Now));
        await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task The_preferred_language_round_trips_and_null_stays_null()
    {
        var chosen = Users.Customer();
        chosen.ChoosePreferredLanguage(Language.Arabic);
        var silent = Users.Customer(email: "silent@example.com", phone: "+962792222222");
        await using (var context = NewContext())
        {
            context.Users.AddRange(chosen, silent);
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext();
        Assert.Same(Language.Arabic, (await reader.Users.SingleAsync(u => u.Id == chosen.Id)).PreferredLanguage);
        Assert.Null((await reader.Users.SingleAsync(u => u.Id == silent.Id)).PreferredLanguage);
    }
}
