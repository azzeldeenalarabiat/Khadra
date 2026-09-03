using Microsoft.Extensions.DependencyInjection;

namespace Khadra.Infrastructure.Persistence.Seeding;

// The seeder itself stays internal, like every other infrastructure implementation. This is the one
// public door into it, so the host can ask for development data without taking a dependency on how
// it is produced.
public static class SeedingExtensions
{
    public static Task SeedDevelopmentDataAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredService<DevelopmentSeeder>().SeedAsync(cancellationToken);
    }
}
