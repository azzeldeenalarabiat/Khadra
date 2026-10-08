using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.PlatformSettings;
using Khadra.Infrastructure.Persistence;
using Khadra.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NSubstitute;
using Npgsql;

namespace Khadra.Tests.Persistence;

/// <summary>
/// Pre-launch item 52 on the engine that enforces it: one OFFERED name per lookup list, folded the way the handler
/// folds it, and a migration that refuses — by name, changing nothing — while duplicates are still offered.
/// </summary>
/// <remarks>Each scenario gets a fresh scratch database, left behind as in <c>PostgresAuditTrailTruncateTests</c>.</remarks>
[Collection(PostgresTestDatabase.Collection)]
public sealed class PostgresLookupNameIndexTests
{
    private const string Previous = "20261008113235_AggregateChildrenDeleteTheirOrphans";
    private const string ThisMigration = "20261008113858_OfferedLookupNamesAreUnique";

    private static readonly DateTimeOffset Now = Build.Now;

    [PostgresFact]
    public async Task It_refuses_to_apply_over_offered_duplicates_and_names_them()
    {
        var database = await FreshDatabaseAsync("lookupdup");
        await using (var context = new KhadraDbContext(database.Options))
        {
            await MigrateToAsync(context, Previous);
            context.Cities.AddRange(City.Create("Madaba", "مادبا", 1, Now).Value, City.Create("MADABA", "مادبا الجديدة", 2, Now).Value);
            await context.SaveChangesAsync();
        }

        await using (var context = new KhadraDbContext(database.Options))
        {
            var refusal = await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(context, ThisMigration));
            Assert.Equal("P0001", refusal.SqlState);
            Assert.Contains("cities.name_en", refusal.MessageText, StringComparison.Ordinal);
            Assert.Contains("Madaba", refusal.MessageText, StringComparison.Ordinal);
        }

        await using var check = await OpenAsync(database);
        Assert.Equal(0L, await ScalarAsync<long>(check, "SELECT count(*) FROM pg_indexes WHERE indexname LIKE 'ux_%_offered_%'"));
        Assert.Equal(2L, await ScalarAsync<long>(check, "SELECT count(*) FROM cities WHERE is_active"));
    }

    [PostgresFact]
    public async Task Once_applied_an_offered_name_cannot_be_offered_twice_and_a_retired_one_reserves_nothing()
    {
        var database = await FreshDatabaseAsync("lookupidx");
        await using (var context = new KhadraDbContext(database.Options))
        {
            await MigrateToAsync(context, Previous);
            var retired = City.Create("Madaba", "مادبا", 1, Now).Value;
            Assert.True(retired.Deactivate().IsSuccess);
            context.Cities.AddRange(City.Create("Madaba", "مادبا", 2, Now).Value, retired);
            context.CarTypes.Add(CarType.Create("SUV", "دفع رباعي", 1, Now).Value);
            await context.SaveChangesAsync();
            // A retired duplicate does not stop it.
            await MigrateToAsync(context, ThisMigration);
        }

        await using (var check = await OpenAsync(database))
        {
            Assert.Equal(4L, await ScalarAsync<long>(check, "SELECT count(*) FROM pg_indexes WHERE indexname LIKE 'ux_%_offered_%'"));
        }

        // Case folded in English; tashkeel and tatweel removed in Arabic — the handler's ComparisonKey.
        Assert.Equal("ux_cities_offered_name_en", await RefusedAsync(database, City.Create("madaba", "مدينة", 3, Now).Value));
        Assert.Equal("ux_cities_offered_name_ar", await RefusedAsync(database, City.Create("Madaba City", "مَادبـا", 3, Now).Value));
        Assert.Equal("ux_car_types_offered_name_en", await RefusedAsync(database, CarType.Create("suv", "رياضية", 2, Now).Value));

        // Retired, the same name may sit beside it as often as anyone likes.
        await using (var context = new KhadraDbContext(database.Options))
        {
            var again = City.Create("Madaba", "مادبا", 4, Now).Value;
            Assert.True(again.Deactivate().IsSuccess);
            context.Cities.Add(again);
            await context.SaveChangesAsync();
        }
    }

    /// <summary>The constraint the unit of work reports for the write, as the handler reads it.</summary>
    private static async Task<string?> RefusedAsync(Scratch database, LookupEntry entry)
    {
        await using var context = new KhadraDbContext(database.Options);
        if (entry is City city) context.Cities.Add(city);
        else context.CarTypes.Add((CarType)entry);
        var unitOfWork = new UnitOfWork(context, Substitute.For<IDomainEventDispatcher>());

        var refusal = await Assert.ThrowsAsync<UniqueConstraintConflictException>(() => unitOfWork.SaveChangesAsync());
        return refusal.ConstraintName;
    }

    private sealed record Scratch(DbContextOptions<KhadraDbContext> Options, string ConnectionString);

    private static Task MigrateToAsync(KhadraDbContext context, string target) =>
        context.GetService<IMigrator>().MigrateAsync(target);

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<NpgsqlConnection> OpenAsync(Scratch database)
    {
        var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<Scratch> FreshDatabaseAsync(string scenario)
    {
        var configured = PostgresTestDatabase.ConnectionString
            ?? throw new InvalidOperationException($"{PostgresTestDatabase.VariableName} is not set.");
        var fresh = new NpgsqlConnectionStringBuilder(configured);
        fresh.Database = $"{fresh.Database}_{scenario}_{Guid.NewGuid():N}"[..Math.Min(63, $"{fresh.Database}_{scenario}_".Length + 8)];

        var maintenance = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" };
        await using (var connection = new NpgsqlConnection(maintenance.ConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand(
                $"CREATE DATABASE \"{fresh.Database!.Replace("\"", "\"\"", StringComparison.Ordinal)}\"", connection);
            await create.ExecuteNonQueryAsync();
        }

        return new Scratch(
            new DbContextOptionsBuilder<KhadraDbContext>()
                .UseNpgsql(fresh.ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options,
            fresh.ConnectionString);
    }
}
