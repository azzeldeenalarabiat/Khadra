namespace Khadra.Infrastructure.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    // Apply pending migrations on startup. Development only; production applies them explicitly.
    public bool AutoMigrate { get; init; }

    // The most connections one pool may open (pre-launch item 237). Each process builds three pools from
    // the connection string — the DbContext's, the readiness probe's and the startup check's — and every
    // open connection is a client of the database's pooler. Applied by ResolveConnectionString; a smaller
    // "Maximum Pool Size" in the connection string itself wins. Null leaves Npgsql's default of 100.
    public int? MaxPoolSize { get; init; }
}
