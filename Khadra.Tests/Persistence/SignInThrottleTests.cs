using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Persistence;
using Khadra.Infrastructure.Security;
using Khadra.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The per-account sign-in ceiling (pre-launch item 51; owner, 2026-10-08) against the real table and the real
/// upsert, on SQLite. The handler's use of it is in <c>LoginHandlerTests</c>; the race on the engine that runs it in
/// production is in <c>PostgresSignInThrottleTests</c>.
/// </summary>
public sealed class SignInThrottleTests : IDisposable
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly EmailAddress Ali = EmailAddress.Create("ali@example.com").Value;

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<KhadraDbContext> _options;

    public SignInThrottleTests()
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

    private static SignInThrottle Throttle(KhadraDbContext context) =>
        new(context, Options.Create(new SignInThrottleOptions()));

    private async Task FailAsync(EmailAddress subject, int times, DateTimeOffset at)
    {
        for (var attempt = 0; attempt < times; attempt++)
        {
            await using var context = NewContext();
            await Throttle(context).RecordFailureAsync(subject, at);
        }
    }

    private async Task<TimeSpan?> BlockedForAsync(EmailAddress subject, DateTimeOffset at)
    {
        await using var context = NewContext();
        return await Throttle(context).BlockedForAsync(subject, at);
    }

    [Fact]
    public async Task Seven_failures_are_counted_and_the_eighth_refuses_the_name_for_fifteen_minutes()
    {
        await FailAsync(Ali, 7, Now);
        Assert.Null(await BlockedForAsync(Ali, Now));

        await FailAsync(Ali, 1, Now.AddMinutes(10));

        Assert.Equal(TimeSpan.FromMinutes(15), await BlockedForAsync(Ali, Now.AddMinutes(10)));
        Assert.Equal(TimeSpan.FromMinutes(1), await BlockedForAsync(Ali, Now.AddMinutes(24)));
        Assert.Null(await BlockedForAsync(Ali, Now.AddMinutes(25)));
    }

    [Fact]
    public async Task A_failure_after_the_window_starts_the_count_again()
    {
        await FailAsync(Ali, 7, Now);

        await FailAsync(Ali, 1, Now.AddMinutes(15));

        await using var context = NewContext();
        var row = await context.SignInThrottles.SingleAsync();
        Assert.Equal(1, row.Failures);
        Assert.Equal(Now.AddMinutes(15), row.WindowStartedAt);
        Assert.Null(row.BlockedUntil);
    }

    [Fact]
    public async Task A_reset_forgets_the_name_and_only_that_name()
    {
        var other = EmailAddress.Create("someone@example.com").Value;
        await FailAsync(Ali, 8, Now);
        await FailAsync(other, 2, Now);

        await using (var context = NewContext())
            await Throttle(context).ResetAsync(Ali);

        Assert.Null(await BlockedForAsync(Ali, Now));
        await using var check = NewContext();
        Assert.Equal(SignInThrottle.Hash(other), (await check.SignInThrottles.SingleAsync()).SubjectHash);
    }

    /// <summary>
    /// The table never holds what was typed: a stranger's guesses at addresses are not a list anybody can read back.
    /// The address is normalised first, so one account name is one row however it is capitalised.
    /// </summary>
    [Fact]
    public async Task The_row_is_keyed_on_a_hash_of_the_normalised_address_never_the_address()
    {
        await FailAsync(EmailAddress.Create("ALI@Example.com").Value, 1, Now);
        await FailAsync(Ali, 1, Now);

        await using var context = NewContext();
        var row = await context.SignInThrottles.SingleAsync();
        Assert.Equal(2, row.Failures);
        Assert.Equal(64, row.SubjectHash.Length);
        Assert.DoesNotContain("ali", row.SubjectHash, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_sweep_removes_rows_that_say_nothing_and_keeps_a_block_still_running()
    {
        var stale = EmailAddress.Create("stale@example.com").Value;
        var blocked = EmailAddress.Create("blocked@example.com").Value;
        await FailAsync(stale, 2, Now.AddMinutes(-30));
        // Its window opened twenty minutes ago and is over; the block its eighth failure started is not.
        await FailAsync(blocked, 7, Now.AddMinutes(-20));
        await FailAsync(blocked, 1, Now.AddMinutes(-6));

        // Any failure runs the sweep.
        await FailAsync(Ali, 1, Now);

        await using var context = NewContext();
        var hashes = await context.SignInThrottles.Select(row => row.SubjectHash).ToListAsync();
        Assert.DoesNotContain(SignInThrottle.Hash(stale), hashes);
        Assert.Contains(SignInThrottle.Hash(blocked), hashes);
        Assert.Contains(SignInThrottle.Hash(Ali), hashes);
        Assert.NotNull(await BlockedForAsync(blocked, Now));
    }
}
