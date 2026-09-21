using Npgsql;

namespace Khadra.Tests.Support;

/// <summary>
/// A fact that needs a real PostgreSQL, and reports itself SKIPPED rather than failing without one.
/// </summary>
/// <remarks>
/// <para>
/// Almost everything in this suite runs on SQLite, deliberately: it is fast, it needs nothing
/// installed, and the aggregate rules it exercises are the same on either engine. A few things are
/// not the same on either engine, and they are exactly the things that cost production an afternoon
/// — a SQLSTATE, a constraint name, an exclusion index. Those cannot be proved anywhere but against
/// the engine that will be running them.
/// </para>
/// <para>
/// Opt-in by environment variable, and NOT by probing localhost. Two reasons. A test that silently
/// attaches to whatever answers on 5432 would attach to a developer's own database, which is the
/// mistake <see cref="TestHostConfiguration"/> exists to prevent. And the connection string carries
/// a password, which must not be in a tracked file — so it is named by whoever runs it:
/// </para>
/// <code>
/// KHADRA_TEST_POSTGRES="Host=localhost;Database=khadra_pgchecks;Username=khadra;Password=..."
/// </code>
/// <para>
/// The database is created and migrated by the fixture if it does not exist, so the variable may
/// name a database that is not there yet. It must NOT name one that holds anything.
/// </para>
/// </remarks>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (PostgresTestDatabase.ConnectionString is null)
            Skip = $"Set {PostgresTestDatabase.VariableName} to a scratch PostgreSQL database to run this.";
    }
}

/// <summary>The scratch database those facts run against, created on first use.</summary>
public static class PostgresTestDatabase
{
    public const string VariableName = "KHADRA_TEST_POSTGRES";

    /// <summary>The configured connection string, or null when nobody asked for these tests.</summary>
    public static string? ConnectionString
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(VariableName);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <summary>
    /// A value nothing else in the process will collide with, for a row that must be unique.
    /// </summary>
    /// <remarks>
    /// The scratch database is reused across runs rather than dropped, because dropping databases
    /// from a test is one keystroke away from dropping the wrong one. So the rows have to not
    /// collide with yesterday's instead.
    /// </remarks>
    public static string Unique(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
