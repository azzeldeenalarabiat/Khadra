using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;
using Khadra.Infrastructure.Persistence;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;

namespace Khadra.Tests.Persistence;

/// <summary>
/// The race behind pre-launch item 240, on the engine that makes it a race: PostgreSQL's <c>xmin</c>.
/// </summary>
/// <remarks>
/// <c>KhadraDbContext</c> maps <c>xmin</c> as every entity's concurrency token on Npgsql only, so this is the one place
/// two rotations of one refresh token can be shown to commit once and fail once, rather than both silently revoking
/// the same row and issuing two live replacements. The handler's answer to the failure (503
/// <c>auth.refresh_conflict</c>) is pinned in <c>RefreshTokensHandlerTests</c> and <c>RefreshRaceTests</c>; this
/// proves the failure happens. Sequential and deterministic: both contexts read before either saves. Opt-in, like
/// every PostgreSQL proof: set <see cref="PostgresTestDatabase.VariableName"/> to a scratch database.
/// </remarks>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresRefreshRaceTests
{
    [PostgresFact]
    public async Task Two_rotations_of_one_token_commit_once_and_the_second_is_a_concurrency_conflict()
    {
        var options = await FreshDatabaseAsync("refresh");
        var user = Users.Customer();
        var now = DateTimeOffset.UtcNow;
        var token = RefreshToken.IssueNewFamily(user.Id, "race-hash-0", now, TimeSpan.FromDays(14), TimeSpan.FromDays(30), "127.0.0.1", "xunit");
        await using (var seed = new KhadraDbContext(options))
        {
            seed.Add(user);
            seed.Add(token);
            await seed.SaveChangesAsync();
        }

        // Both presentations load the token while it is still live, as two requests in the handler at once do.
        await using var winner = new KhadraDbContext(options);
        await using var loser = new KhadraDbContext(options);
        var winning = await winner.Set<RefreshToken>().SingleAsync(row => row.Id == token.Id);
        var losing = await loser.Set<RefreshToken>().SingleAsync(row => row.Id == token.Id);

        var winnersReplacement = winning.IssueReplacement("race-hash-1", now, TimeSpan.FromDays(14), "127.0.0.1", "xunit");
        Assert.True(winning.Rotate(now, winnersReplacement.Id).IsSuccess);
        winner.Add(winnersReplacement);
        await new UnitOfWork(winner, Substitute.For<IDomainEventDispatcher>()).SaveChangesAsync(CancellationToken.None);

        var losersReplacement = losing.IssueReplacement("race-hash-2", now, TimeSpan.FromDays(14), "127.0.0.1", "xunit");
        Assert.True(losing.Rotate(now, losersReplacement.Id).IsSuccess);
        loser.Add(losersReplacement);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            new UnitOfWork(loser, Substitute.For<IDomainEventDispatcher>()).SaveChangesAsync(CancellationToken.None));

        // The winner's rotation is the one on record, and the loser left nothing behind.
        await using var reading = new KhadraDbContext(options);
        var stored = await reading.Set<RefreshToken>().SingleAsync(row => row.Id == token.Id);
        Assert.Equal(winnersReplacement.Id, stored.ReplacedByTokenId);
        Assert.False(await reading.Set<RefreshToken>().AnyAsync(row => row.Id == losersReplacement.Id));
    }

    /// <summary>A fresh database named after the configured scratch one, migrated to the latest schema.</summary>
    private static async Task<DbContextOptions<KhadraDbContext>> FreshDatabaseAsync(string scenario)
    {
        var configured = PostgresTestDatabase.ConnectionString
            ?? throw new InvalidOperationException($"{PostgresTestDatabase.VariableName} is not set.");
        var fresh = new NpgsqlConnectionStringBuilder(configured);
        var name = $"{fresh.Database}_{scenario}_{Guid.NewGuid():N}";
        fresh.Database = name[..Math.Min(63, $"{fresh.Database}_{scenario}_".Length + 8)];

        var maintenance = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" };
        await using (var connection = new NpgsqlConnection(maintenance.ConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand(
                $"CREATE DATABASE \"{fresh.Database!.Replace("\"", "\"\"", StringComparison.Ordinal)}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseNpgsql(fresh.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new KhadraDbContext(options);
        await context.Database.MigrateAsync();
        return options;
    }
}
