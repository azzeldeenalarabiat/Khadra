namespace Khadra.Infrastructure.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    // Apply pending migrations on startup. Development only; production applies them explicitly.
    public bool AutoMigrate { get; init; }
}
