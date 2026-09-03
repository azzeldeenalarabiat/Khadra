namespace Khadra.Infrastructure.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    // Apply pending migrations on startup. Development only; production applies them explicitly.
    public bool AutoMigrate { get; init; }

    // Fill an empty development database with a realistic platform. Ignored outside Development and
    // skipped entirely once any user exists, so it can never overwrite real data.
    public bool SeedDevelopmentData { get; init; }
}
