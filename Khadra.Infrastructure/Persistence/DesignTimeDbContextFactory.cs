using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Khadra.Infrastructure.Persistence;

// Lets `dotnet ef migrations add` build the model without booting the API (and without real secrets).
// Migrations are generated from the model only; the placeholder connection is never opened.
//
// It is also what the MIGRATION BUNDLE runs through when a deployment applies the schema, and there
// the connection very much is opened. Hence the second name below: a deployment already sets
// ConnectionStrings__DefaultConnection for the API, and requiring it to set the same value again
// under a different name is how a migrator ends up quietly pointed at localhost -- which is exactly
// what happened the first time this ran in a container.
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<KhadraDbContext>
{
    public KhadraDbContext CreateDbContext(string[] args)
    {
        // Most specific first: an explicit design-time override, then the connection the deployment
        // already uses, then a placeholder that exists so `migrations add` works on a laptop with no
        // database at all. The placeholder is never opened; if it ever is, failing to reach
        // localhost is the correct and obvious outcome.
        var connectionString =
            Environment.GetEnvironmentVariable("KHADRA_DESIGNTIME_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Database=khadra;Username=postgres;Password=placeholder";

        // Normalised for the same reason the application normalises it: the value above may be a
        // platform URL, and Npgsql reads only keyword form. Reading the deployment's connection
        // string without converting it is how the migrator failed the first time it was handed one.
        var options = new DbContextOptionsBuilder<KhadraDbContext>()
            .UseNpgsql(PostgresConnectionString.Normalise(connectionString), npgsql => npgsql.MigrationsAssembly(typeof(KhadraDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new KhadraDbContext(options);
    }
}
